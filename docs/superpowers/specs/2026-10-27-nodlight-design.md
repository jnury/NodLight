# NodLight — design

Date: 2026-10-27
Status: draft for review

## Why

Julien routinely runs 6–7 Alacritty windows on Windows, each with a Claude Code
or Codex session. The recurring pain: **he can't tell which session is waiting
for him.** Sessions sit idle on a permission prompt or a finished turn while he
watches a different window.

NodLight is a small always-visible strip at the side of the screen with one
card per agent session. Each card shows the session's status at a glance, and
clicking a card brings that Alacritty window to the front.

Inspiration: [ghossty-dock](https://github.com/andrewbakercloudscale/ghossty-dock)
(a patched Ghostty on macOS with a session sidebar). NodLight is deliberately
different: it is a **separate app watching unmodified Alacritty windows from
outside**, not a terminal fork.

Success: at any moment, one glance at the strip tells Julien which sessions
need him and which one has waited longest, and one click takes him there.

## Goals

- One card per Alacritty window running Claude Code or Codex.
- Status per card: **Working**, **Waiting** (permission prompt, question, or
  finished turn), **Idle**.
- Click a card → that Alacritty window comes to the front (restored if
  minimised).
- Zero manual setup beyond running the app once (hooks installed on first run).

## Non-goals (for now)

- macOS / Linux.
- Other terminals (Windows Terminal, Ghostty, …).
- Answering prompts from the deck (no Allow/Deny buttons).
- Cost, context size, git branch, PR, ports (ghossty-dock has these; not wanted
  now).
- Sound, toast notifications, flashing. **Colour change is enough** (Julien's
  call).
- Reserving screen space (AppBar). The strip **floats** over other windows for
  now (Julien's call).
- Modifying Alacritty.

## Anti-goals

- **Slowing down or breaking an agent.** The hook helper must never block,
  prompt, or fail a Claude Code / Codex turn — even when the deck isn't running.
- **Damaging the user's config.** Installing hooks must not lose or reorder
  existing hooks/settings in `~/.claude/settings.json` or touch Codex's existing
  `notify` entry.
- **Wrong window.** Clicking a card and landing in a different session is worse
  than no deck.

## Platform and stack (Claude's call)

- Windows 11 only. Alacritty 0.17 is what's installed.
- **C# / .NET 10 (SDK present), WPF** for the deck. Chosen for first-class
  Win32 interop (enumerate windows, read titles, focus/restore, always-on-top
  tool window) with no extra runtime layer.
- A small **hook helper** console exe (`nodlight-hook.exe`), invoked by Claude
  Code and Codex hooks. Can be the same assembly or a separate tiny project —
  builder's choice — but it must start fast (consider Native AOT).

## The strip (decided with Julien — mockup "C")

- Thin vertical strip docked to the **right edge of the primary monitor**,
  always on top, floating (doesn't reserve space). Dark theme. Not shown in the
  taskbar / Alt-Tab.
- One card per session, showing:
  - **Window title** (the primary label — Julien identifies sessions by it;
    Claude Code updates it with a task summary).
  - **Agent badge**: `claude` or `codex`.
  - **Status + time in that status** (e.g. "Waiting · 4 min").
  - **Project folder** name (last segment of the session's cwd).
  - For Waiting: **what it's asking** (e.g. "Allow Bash: npm test?") or
    "Your turn" for a finished turn.
- Colour per status: Waiting = warm/orange and visually loudest, Working =
  green, Idle = grey.
- Ordering (Claude's call): Waiting first, longest-waiting at the top; then
  Working; then Idle.
- Click → bring that Alacritty window to the foreground; restore if minimised.
- Reference mockup: `.superpowers/brainstorm/*/content/strip-layout.html`,
  option C.

## Status detection

Status comes from **agent hooks**, not from screen scraping.

### Hook helper

Claude Code and Codex call `nodlight-hook.exe <agent> <event>` with the hook's
JSON payload on stdin. The helper:

1. Reads the payload (session id, cwd, transcript path, tool name/input or
   notification message, as available).
2. Identifies its Alacritty window: walk the parent-process chain until
   `alacritty.exe`, then find that process's top-level window (HWND).
3. Sends one message to the deck (local IPC, e.g. a named pipe) with: agent,
   event, session id, cwd, transcript path, detail text, alacritty PID, HWND,
   timestamp.
4. Exits 0 with no stdout. If the deck isn't running or doesn't answer within a
   short timeout (~200 ms), it drops the message and exits 0 anyway.

### Event → status mapping (Claude's call)

| Event (Claude Code / Codex) | Status | Detail |
|---|---|---|
| `SessionStart` | Idle | — |
| `UserPromptSubmit` | Working | — |
| `PreToolUse` / `PostToolUse` | Working | — |
| Claude `Notification` (permission prompt), Codex `PermissionRequest` | Waiting | the prompt / tool being asked about |
| Claude `Notification` (idle prompt, ~60 s after a turn) | no change | — |
| `Stop` | Waiting | "Your turn" |
| `SessionEnd` (Claude) | card removed | — |

- **Waiting → Idle** (Claude's call): a finished-turn Waiting becomes Idle once
  Julien has brought that window to the front (via the deck or otherwise), so
  "Waiting" keeps meaning "you haven't looked yet".
- **Esc interrupts** fire no hook. The deck watches the session's transcript
  file (path from the payload) and marks the session Idle when it sees an
  interrupt, as ghossty-dock does.

### One card per window; nested runs ignored (Claude's call)

- Cards are **keyed by Alacritty window (HWND)**. A terminal hosts one
  interactive agent at a time; if a new interactive session starts in the same
  window, it replaces the old one on that card.
- Hooks are global, so they also fire for agent runs started *from inside* a
  session (`claude -p`, `codex exec`, Claude subagents). These must not touch
  the card. The helper drops an event when another `claude`/`codex` process
  sits between its agent process and `alacritty.exe` in the parent chain, and
  also drops events the payload marks as non-interactive or subagent where that
  is available.
- Verified on this machine: one `alacritty.exe` process per window, and
  `alacritty.toml` launches `pwsh.exe` directly (no WSL/tmux in the chain).

### Which windows appear

- Only Alacritty windows from which a hook has fired. A fresh session appears on
  its first event (SessionStart), not when the window opens.
- A card disappears when its window closes (deck polls/watches HWND validity)
  or on `SessionEnd`.
- Card label = live window title, re-read periodically from the HWND.
- If the deck starts after sessions are already running, those sessions appear
  on their next hook event (Claude's call — no startup discovery in v1).

## First-run setup (decided: deck installs hooks itself)

On first run (and from a "Install / remove hooks" menu item afterwards):

- **Claude Code**: merge NodLight hook entries into `~/.claude/settings.json`
  for the events above.
- **Codex**: add entries to `~/.codex/hooks.json` (create if missing). Codex
  hooks are stable in the installed CLI (0.164; `codex features list` shows
  `hooks stable true`). **Do not touch `notify` in `config.toml`** — Julien's is
  already used by Codex computer-use and Codex allows only one.
- Codex requires new non-managed hooks to be **trusted once in the CLI**
  (`/hooks`). The deck can't do this; it shows a one-line instruction after
  install.
- Back up each file before writing; keep existing hooks and settings intact;
  re-running install is a no-op; "remove hooks" deletes only NodLight's
  entries.

## Left to the builder

- Project layout, IPC mechanism details, polling intervals.
- Exact strip width, fonts, colours within the "dark, Waiting is loudest" brief.
- Reliable foregrounding under Windows' focus-stealing rules.
- How the app starts (manual is fine; startup-with-Windows optional).
- Packaging / install location of the hook helper (hook entries need an
  absolute path).
- Tests: at minimum the event→status logic, config merge/unmerge, and
  parent-chain → window resolution.
