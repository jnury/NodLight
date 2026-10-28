using System;
using System.IO;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace NodLight.Installer
{
    public static class CustomActions
    {
        const string DesktopRuntime = "Microsoft.WindowsDesktop.App";

        /// <summary>
        /// Sets DOTNET_DESKTOP_FOUND to "1" when a release of the x64 Windows Desktop Runtime with the required major
        /// version (property DOTNET_REQUIRED_MAJOR) is installed, and leaves it empty otherwise. The launch condition in
        /// the package stops setup with a message when it is empty. If the lookup itself fails the check lets setup
        /// continue: a broken check must not block an install on a machine that may well have the runtime.
        /// </summary>
        [CustomAction]
        public static ActionResult CheckDotNetDesktopRuntime(Session session)
        {
            int major;
            if (!int.TryParse(session["DOTNET_REQUIRED_MAJOR"], out major)) major = 10;

            try
            {
                var version = FindInRegistry(major) ?? FindOnDisk(major);
                if (version != null) session.Log("Found the .NET " + major + " Desktop Runtime " + version);
                else session.Log("The .NET " + major + " Desktop Runtime (x64) is not installed.");
                session["DOTNET_DESKTOP_FOUND"] = version != null ? "1" : "";
            }
            catch (Exception ex)
            {
                session.Log("Could not check for the .NET Desktop Runtime, continuing: " + ex);
                session["DOTNET_DESKTOP_FOUND"] = "1";
            }

            return ActionResult.Success;
        }

        // The .NET installers record each installed shared runtime as a value named by its version ("10.0.1" = 1).
        // The x64 entries live in the 32-bit registry view; the 64-bit view is read too in case that changes.
        static string FindInRegistry(int major)
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\" + DesktopRuntime))
                {
                    if (key == null) continue;
                    foreach (var name in key.GetValueNames())
                        if (IsRelease(name, major)) return name;
                }
            }
            return null;
        }

        // The folder layout is what the app host reads, so it also answers when the registry entry is missing
        // (a runtime unpacked from a zip, for example). Looks in the default install location.
        static string FindOnDisk(int major)
        {
            var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetEnvironmentVariable("ProgramFiles");
            if (string.IsNullOrEmpty(programFiles)) return null;

            var shared = Path.Combine(programFiles, "dotnet", "shared", DesktopRuntime);
            if (!Directory.Exists(shared)) return null;
            foreach (var dir in Directory.GetDirectories(shared))
            {
                var name = Path.GetFileName(dir);
                if (IsRelease(name, major) && File.Exists(Path.Combine(dir, "PresentationFramework.dll"))) return name;
            }
            return null;
        }

        // "10.0.1" counts; a preview such as "10.0.0-rc.1" does not.
        static bool IsRelease(string version, int major) =>
            version.StartsWith(major + ".", StringComparison.Ordinal) && version.IndexOf('-') < 0;
    }
}
