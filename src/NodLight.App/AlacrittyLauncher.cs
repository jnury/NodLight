using System.Diagnostics;
using System.IO;
using System.Windows;
using NodLight.Core;

namespace NodLight.App;

/// <summary>Starts a new Alacritty window (its own alacritty.exe process, which is what the deck expects).</summary>
public static class AlacrittyLauncher
{
    public static void Start()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var exe = ExecutableLocator.Find("alacritty.exe", Environment.GetEnvironmentVariable("PATH"),
            [Path.Combine(programFiles, "Alacritty")], File.Exists);

        if (exe is null)
        {
            MessageBox.Show("alacritty.exe was not found on PATH or in Program Files\\Alacritty.", "NodLight",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"NodLight: could not start {exe}: {ex}");
            MessageBox.Show("Could not start Alacritty: " + ex.Message, "NodLight", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
