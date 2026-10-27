using System.IO;
using System.Windows;
using NodLight.Core;

namespace NodLight.App;

public enum SetupStatus { Done, Unchanged, Invalid, Failed }

/// <summary>Outcome for one file (or the helper); Detail carries the error text for Invalid/Failed.</summary>
public sealed record SetupOutcome(string Path, SetupStatus Status, string? Detail = null);

public sealed record SetupResult(IReadOnlyList<SetupOutcome> Outcomes)
{
    public bool Ok => Outcomes.All(o => o.Status is SetupStatus.Done or SetupStatus.Unchanged);
}

/// <summary>
/// Installs/removes the NodLight hooks. Install/Remove/EnsureInstalledResult return results and never touch the UI;
/// the Show* methods are the thin message-box layer. One unreadable file never blocks the other agent's file.
/// </summary>
public static class Setup
{
    const string TrustMessage = "NodLight hooks installed. In Codex, run /hooks once and trust the NodLight hooks.";

    public static SetupPaths Paths { get; set; } = SetupPaths.FromEnvironment();

    /// <summary>Directory holding the bundled helper (next to NodLight.exe).</summary>
    public static string BundleDir { get; set; } = AppContext.BaseDirectory;

    public static SetupResult Install()
    {
        var outcomes = new List<SetupOutcome>();
        var helper = Paths.InstalledHelper;
        var bundled = Path.Combine(BundleDir, "nodlight-hook.exe");

        if (!File.Exists(bundled))
        {
            // Never point hooks at a helper that is not there.
            outcomes.Add(new SetupOutcome(bundled, SetupStatus.Failed, "The bundled nodlight-hook.exe is missing."));
            return new SetupResult(outcomes);
        }

        try
        {
            outcomes.Add(CopyHelper(bundled, helper));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            outcomes.Add(new SetupOutcome(helper, SetupStatus.Failed, ex.Message));
            return new SetupResult(outcomes);
        }

        outcomes.Add(Run(Paths.ClaudeSettings, () => HookInstaller.InstallFile(Paths.ClaudeSettings, AgentKind.Claude, helper)));
        outcomes.Add(Run(Paths.CodexHooks, () => HookInstaller.InstallFile(Paths.CodexHooks, AgentKind.Codex, helper)));
        return new SetupResult(outcomes);
    }

    public static SetupResult Remove() => new([
        Run(Paths.ClaudeSettings, () => HookInstaller.UninstallFile(Paths.ClaudeSettings)),
        Run(Paths.CodexHooks, () => HookInstaller.UninstallFile(Paths.CodexHooks)),
    ]);

    /// <summary>True when either agent's file lacks NodLight hooks (a missing file counts as lacking them).</summary>
    public static bool NeedsInstall()
    {
        return Lacks(Paths.ClaudeSettings, AgentKind.Claude) || Lacks(Paths.CodexHooks, AgentKind.Codex);

        static bool Lacks(string path, AgentKind kind)
        {
            try
            {
                return !HookInstaller.IsInstalled(File.Exists(path) ? File.ReadAllText(path) : null, kind);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return true; // let Install report the real problem for this file
            }
        }
    }

    /// <summary>Startup path: null when nothing needed doing.</summary>
    public static SetupResult? EnsureInstalledResult() => NeedsInstall() ? Install() : null;

    // ---- UI layer ----

    public static void EnsureInstalled()
    {
        var result = EnsureInstalledResult();
        if (result is not null) Show(result, TrustMessage);
    }

    public static void InstallFromMenu() => Show(Install(), TrustMessage);

    public static void RemoveFromMenu() => Show(Remove(), "NodLight hooks removed.");

    static void Show(SetupResult result, string success)
    {
        if (result.Ok)
        {
            MessageBox.Show(success, "NodLight", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lines = result.Outcomes
            .Where(o => o.Status is SetupStatus.Invalid or SetupStatus.Failed)
            .Select(o => o.Status == SetupStatus.Invalid
                ? $"{o.Path} could not be parsed and was left untouched: {o.Detail}"
                : $"{o.Path} failed: {o.Detail}");
        var text = string.Join(Environment.NewLine + Environment.NewLine, lines);
        if (result.Outcomes.Any(o => o.Status == SetupStatus.Done && o.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            text += Environment.NewLine + Environment.NewLine + "The other file was processed. " + success;
        MessageBox.Show(text, "NodLight", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // Same size and timestamp means the same build of the helper; anything else is replaced.
    static SetupOutcome CopyHelper(string source, string dest)
    {
        var s = new FileInfo(source);
        var d = new FileInfo(dest);
        if (d.Exists && d.Length == s.Length && d.LastWriteTimeUtc == s.LastWriteTimeUtc)
            return new SetupOutcome(dest, SetupStatus.Unchanged);

        Directory.CreateDirectory(Paths.InstallDir);
        File.Copy(source, dest, overwrite: true);
        File.SetLastWriteTimeUtc(dest, s.LastWriteTimeUtc);
        return new SetupOutcome(dest, SetupStatus.Done);
    }

    static SetupOutcome Run(string path, Action action)
    {
        try
        {
            action();
            return new SetupOutcome(path, SetupStatus.Done);
        }
        catch (InvalidDataException ex)
        {
            return new SetupOutcome(path, SetupStatus.Invalid, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SetupOutcome(path, SetupStatus.Failed, ex.Message);
        }
    }
}
