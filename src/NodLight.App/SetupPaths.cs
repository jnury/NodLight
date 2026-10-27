using System.IO;

namespace NodLight.App;

/// <summary>
/// Where Setup reads and writes. Defaults to the real locations; two optional environment variables redirect them
/// so the whole flow can run in a sandbox:
/// NODLIGHT_PROFILE_DIR replaces the user-profile root (".claude" and ".codex" are appended) and
/// NODLIGHT_INSTALL_DIR replaces the helper install directory.
/// </summary>
public sealed record SetupPaths(string ClaudeSettings, string CodexHooks, string InstallDir)
{
    public static SetupPaths FromEnvironment()
    {
        var profile = Environment.GetEnvironmentVariable("NODLIGHT_PROFILE_DIR");
        if (string.IsNullOrWhiteSpace(profile)) profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var install = Environment.GetEnvironmentVariable("NODLIGHT_INSTALL_DIR");
        if (string.IsNullOrWhiteSpace(install))
            install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NodLight");

        return new SetupPaths(
            Path.Combine(profile, ".claude", "settings.json"),
            Path.Combine(profile, ".codex", "hooks.json"),
            install);
    }

    public string InstalledHelper => Path.Combine(InstallDir, "nodlight-hook.exe");
}
