# NodLight

An always-on-top strip on the right edge of the screen with one card per Alacritty window running Claude Code or
Codex. Cards show whether the session is working, idle, or waiting for you (a permission prompt or "your turn");
click a card to bring its terminal to the front.

NodLight learns about sessions from agent hooks: a small Native AOT helper (`nodlight-hook.exe`) is called by
Claude Code and Codex on each event and forwards it to the deck over a named pipe.

## Prerequisites

- Windows 10/11, .NET 10 SDK.
- Visual Studio 2022 (or Build Tools) with the **Desktop development with C++** workload: the helper is published
  with Native AOT, which needs the MSVC linker.
- Native AOT finds the linker through `vswhere.exe`. The build prepends the default VS Installer directory
  (`C:\Program Files (x86)\Microsoft Visual Studio\Installer`) to `PATH` for that step; if Visual Studio is installed
  elsewhere, put the directory containing `vswhere.exe` on `PATH` yourself:

  ```powershell
  $env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"
  ```

## Build, test, run

```powershell
dotnet build
dotnet test
.\src\NodLight.App\bin\Debug\net10.0-windows\NodLight.exe
```

A publish folder you can copy elsewhere (it needs the .NET 10 Desktop Runtime; the helper inside is native):

```powershell
dotnet publish src/NodLight.App -c Release -r win-x64 -o .\publish
.\publish\NodLight.exe
```

## First run

On start, NodLight copies the helper to `%LOCALAPPDATA%\NodLight\nodlight-hook.exe` and, when hooks are missing or
out of date, adds its hook entries to `~/.claude/settings.json` and `~/.codex/hooks.json` (each file is backed up
once as `<file>.nodlight.bak`; other settings and hooks are left alone). Later starts only refresh the helper.

**Codex needs one manual step:** run `/hooks` inside Codex once and trust the NodLight hooks. Codex does not run new
hooks until they are trusted.

Right-click the strip for **Install hooks**, **Remove hooks** and **Exit**. Errors are logged to
`%LOCALAPPDATA%\NodLight\deck.log`.

## Uninstall

1. Right-click the strip, choose **Remove hooks** (only NodLight's entries are removed), then **Exit**.
2. Delete the helper and log:

   ```powershell
   Remove-Item -Recurse -Force "$env:LOCALAPPDATA\NodLight"
   ```

## Trying it in a sandbox

Two environment variables redirect everything NodLight reads and writes, so you can run it without touching your real
configuration:

- `NODLIGHT_PROFILE_DIR` replaces your user profile folder (`.claude\settings.json` and `.codex\hooks.json` go
  under it).
- `NODLIGHT_INSTALL_DIR` replaces `%LOCALAPPDATA%\NodLight` (helper and `deck.log`).

```powershell
$env:NODLIGHT_PROFILE_DIR = "$env:TEMP\nodlight-sandbox\profile"
$env:NODLIGHT_INSTALL_DIR = "$env:TEMP\nodlight-sandbox\install"
.\src\NodLight.App\bin\Debug\net10.0-windows\NodLight.exe
```

Only one deck runs per user at a time; exit your normal NodLight first.
