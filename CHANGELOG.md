# Changelog

## 1.0.1 — 2026-10-28

First release.

### Features

- **A strip of session cards** anchored to the bottom-right corner of the primary monitor, always on top, as tall as its cards. One card per Alacritty window running Claude Code or Codex.
- **Status at a glance.** Orange with a slow blink when a session waits for you (a permission prompt or a finished turn), green while it works, grey when idle. Each card shows the window title, a `claude` or `codex` badge, how long it has been in that status and the project folder.
- **Interrupts detected.** A turn you cancel with Esc is read from the session transcript, so the card does not stay on "working".
- **Click to focus.** Clicking a card brings its window to the front, restored if it was minimised. A finished turn goes grey once you have looked at that window.
- **Sessions already running are found at start** and shown as idle until their next event.
- **Resource use per session.** CPU, memory and disk/network I/O of everything running under the window, shown in red above 50% CPU, 4 GB or 50 MB/s.
- **Taskbar button.** Shows or hides the strip; its thumbnail counts sessions waiting, working and idle; right-click for New, Hide, Install hooks and Remove hooks.
- **Hide and + buttons** on the strip: tuck it away, or open a new Alacritty window.
- **Follows Windows** light or dark mode and accent colour.
- **Hook setup on first start.** Installs a small native hook helper and adds its hooks to Claude Code and Codex, after asking, with a one-time backup of each file. Your own hooks are left alone, and **Remove hooks** takes out only NodLight's.
- **Installer** for just you (no admin rights) or everyone on the computer. It checks for the .NET 10 Desktop Runtime, closes a running NodLight on update and offers to start it at the end. Also available as a portable zip.
- **Log file** at `%LOCALAPPDATA%\NodLight\deck.log`: start-up, sessions found, hook setup and errors.
