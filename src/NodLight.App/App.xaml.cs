using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NodLight.Core;

namespace NodLight.App;

public partial class App : Application
{
    Mutex? single;
    PipeServer? pipe;
    WindowService? windows;
    DispatcherTimer? timer;

    const long LogMaxBytes = 256 * 1024;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        single = new Mutex(true, "NodLight.Single." + Environment.UserName, out var first);
        if (!first)
        {
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

        var store = new SessionStore();
        windows = new WindowService();
        var deck = new DeckViewModel(store, windows, () => DateTimeOffset.Now);
        var strip = new StripWindow(deck);

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

        MainWindow = strip;
        strip.Show();

        // After the strip is up so the deck is usable while the (modal) message box is open.
        Setup.EnsureInstalled();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        timer?.Stop();
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
