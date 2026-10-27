using System.Diagnostics;
using System.Text;
using static NodLight.App.Native;

namespace NodLight.App;

/// <summary>
/// Top-level window queries and focus for terminal windows, plus a foreground-change notification.
/// Create and use on the UI thread: the WinEvent hook is out-of-context, so its callback is delivered
/// through the creating thread's message loop.
/// </summary>
public sealed class WindowService : IDisposable
{
    // Held in a field so the GC cannot collect the delegate while user32 still calls it.
    readonly WinEventProc foregroundProc;
    nint hook;

    /// <summary>Raised on the UI thread with the new foreground window's handle.</summary>
    public event Action<long>? ForegroundChanged;

    public WindowService()
    {
        foregroundProc = OnWinEvent;
        hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, foregroundProc, 0, 0,
            WINEVENT_OUTOFCONTEXT);
        if (hook == 0)
            Trace.WriteLine("NodLight: SetWinEventHook(EVENT_SYSTEM_FOREGROUND) failed; cards will not clear on focus.");
    }

    /// <summary>
    /// The terminal window of the process, or null. Assumes one alacritty.exe process per window (the plan's premise):
    /// the current foreground window is preferred when it belongs to the process (the hook fired from the window the user
    /// is in), otherwise the first candidate in Z-order. Candidates are visible, unowned, not tool windows and not cloaked.
    /// </summary>
    public long? FindMainWindow(int pid)
    {
        if (pid <= 0) return null;
        var foreground = GetForegroundWindow();
        if (foreground != 0 && IsCandidate(foreground, pid)) return foreground;

        long? found = null;
        EnumWindows((hwnd, _) =>
        {
            if (!IsCandidate(hwnd, pid)) return true;
            found = hwnd;
            return false;
        }, 0);
        return found;
    }

    static bool IsCandidate(nint hwnd, int pid)
    {
        GetWindowThreadProcessId(hwnd, out var owner);
        return owner == (uint)pid
            && IsWindowVisible(hwnd)
            && GetWindow(hwnd, GW_OWNER) == 0
            && (GetExStyle(hwnd) & WS_EX_TOOLWINDOW) == 0
            && !IsCloaked(hwnd);
    }

    // Cloaked windows (other virtual desktops, suspended UWP frames) report visible but are not on screen.
    static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    public string GetTitle(long hwnd)
    {
        var length = GetWindowTextLengthW((nint)hwnd);
        if (length <= 0) return "";
        var sb = new StringBuilder(length + 1);
        GetWindowTextW((nint)hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public bool IsAlive(long hwnd) => IsWindow((nint)hwnd);

    /// <summary>Restores the window if minimised and brings it to the front. Best effort; never throws.</summary>
    public void Focus(long hwnd)
    {
        var h = (nint)hwnd;
        if (!IsWindow(h)) return;
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        if (SetForegroundWindow(h)) return;

        // Foreground lock: borrow the current foreground thread's input state, which carries the right to change it.
        var me = GetCurrentThreadId();
        var fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            BringWindowToTop(h);
            SetForegroundWindow(h);
        }
        finally
        {
            if (attached) AttachThreadInput(me, fgThread, false);
        }
    }

    void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread,
        uint dwmsEventTime)
    {
        if (hwnd == 0 || idObject != OBJID_WINDOW) return;
        // Called from native code: an escaping exception would take the process down.
        try
        {
            ForegroundChanged?.Invoke(hwnd);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"NodLight: foreground handler failed: {ex}");
        }
    }

    public void Dispose()
    {
        if (hook == 0) return;
        UnhookWinEvent(hook);
        hook = 0;
    }
}
