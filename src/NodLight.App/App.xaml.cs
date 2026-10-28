using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using NodLight.Core;

namespace NodLight.App;

public partial class App : Application
{
    Mutex? single;
    PipeServer? pipe;
    WindowService? windows;
    DispatcherTimer? timer;
    DispatcherTimer? resourceTimer;
    CancellationTokenSource? commands;
    StripWindow? strip;
    TaskbarWindow? taskbar;

    const long LogMaxBytes = 256 * 1024;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A jump-list task starts the exe with a command argument: hand it to the running deck and leave.
        var command = CommandPipe.FromArgs(e.Args);

        single = new Mutex(true, "NodLight.Single." + Environment.UserName, out var first);
        if (!first)
        {
            if (command is not null) CommandPipe.TrySend(command);
            single.Dispose();
            single = null;
            Shutdown();
            return;
        }


        // A throw past this point must not leave a windowless process holding the mutex: report it and exit.
        try
        {
            Start();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"NodLight: startup failed: {ex}");
            MessageBox.Show("NodLight could not start: " + ex.Message, "NodLight", MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    void Start()
    {
        Trace.AutoFlush = true;
        Trace.Listeners.Add(new FileTraceListener(Path.Combine(Setup.Paths.InstallDir, "deck.log"), LogMaxBytes));

        // Backstop for anything the guarded paths miss: an always-on-top strip should not vanish over one bad event.
        DispatcherUnhandledException += (_, args) =>
        {
            Trace.WriteLine($"NodLight: unhandled UI exception: {args.Exception}");
            args.Handled = true;
        };

        ThemeService.Start(this);
        ThemeService.Changed += SetUpJumpList; // the menu icons follow the Windows light/dark mode

        commands = new CancellationTokenSource();
        CommandPipe.Listen(command => Dispatcher.BeginInvoke(() => RunCommand(command)), commands.Token);
        SetUpJumpList();

        var store = new SessionStore();
        windows = new WindowService();
        var deck = new DeckViewModel(store, windows, () => DateTimeOffset.Now);
        strip = new StripWindow(deck);

        // Events arrive on a pipe thread; the store and the view models live on the UI thread.
        pipe = new PipeServer(PipeProtocol.PipeName, ev => Dispatcher.BeginInvoke(() => deck.OnHookEvent(ev)));
        try
        {
            pipe.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another deck (e.g. from a different session of the same user) already owns the pipe.
            Trace.WriteLine($"NodLight: pipe unavailable: {ex.Message}");
            pipe.Dispose();
            pipe = null;
            MessageBox.Show("Another NodLight is already receiving hook events (the pipe is in use). This one will exit.",
                "NodLight", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        windows.ForegroundChanged += hwnd =>
        {
            if (hwnd != strip.Handle) deck.OnForeground(hwnd);
        };

        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => deck.Tick(),
            Dispatcher);

        deck.DiscoverExisting(
            () =>
            {
                var tree = new Win32ProcessTree();
                return SessionDiscovery.Find(tree.All(), tree);
            },
            Win32ProcessTree.CurrentDirectoryOf);

        void MeasureResources()
        {
            deck.MeasureResources(roots =>
            {
                var tree = new Win32ProcessTree();
                return roots.ToDictionary(root => root, root => tree.DescendantCounters(root));
            });
            taskbar?.RefreshThumbnail();
        }
        MeasureResources(); // primes the meter: rates appear from the second sample
        resourceTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background,
            (_, _) => MeasureResources(), Dispatcher);

        MainWindow = strip;
        strip.Show();
        taskbar = new TaskbarWindow(strip, deck);
        taskbar.Show();

        // After the strip is up so the deck is usable while the (modal) message box is open.
        Setup.EnsureInstalled();
    }

    void RunCommand(string command)
    {
        switch (command)
        {
            case CommandPipe.New: AlacrittyLauncher.Start(); break;
            case CommandPipe.InstallHooks: Setup.InstallFromMenu(); break;
            case CommandPipe.RemoveHooks: Setup.RemoveFromMenu(); break;
            case CommandPipe.Hide: strip?.Hide(); break;
        }
    }

    // Tasks in the right-click menu of the taskbar button (Windows adds its own "Close window", which exits the deck).
    // Each task starts this exe with an argument, which the running deck receives through CommandPipe.
    void SetUpJumpList()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            var list = new JumpList { ShowRecentCategory = false, ShowFrequentCategory = false };
            // Monochrome icons in the menu's text colour: the "dark" folder holds white glyphs for the dark taskbar.
            var themeDir = Path.Combine(AppContext.BaseDirectory, "icons", ThemeService.SystemUsesLightTheme ? "light" : "dark");
            foreach (var (title, argument, icon) in new[]
                     {
                         ("New", "--new", "new.ico"),
                         ("Install hooks", "--install-hooks", "install.ico"),
                         ("Remove hooks", "--remove-hooks", "remove.ico"),
                         ("Hide", "--hide", "hide.ico"),
                     })
            {
                var iconFile = Path.Combine(themeDir, icon);
                list.JumpItems.Add(new JumpTask
                {
                    Title = title,
                    ApplicationPath = exe,
                    Arguments = argument,
                    // Falls back to the app icon if the file is missing.
                    IconResourcePath = File.Exists(iconFile) ? iconFile : exe,
                    IconResourceIndex = 0,
                });
            }
            JumpList.SetJumpList(this, list);
            list.Apply();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"NodLight: jump list unavailable: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        commands?.Cancel();
        timer?.Stop();
        resourceTimer?.Stop();
        pipe?.Dispose();
        windows?.Dispose();
        if (single is not null)
        {
            single.ReleaseMutex();
            single.Dispose();
        }
        base.OnExit(e);
    }
}
