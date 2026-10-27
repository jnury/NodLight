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
    }

    /// <summary>The first visible, unowned top-level window of the process, or null.</summary>
    public long? FindMainWindow(int pid)
    {
        if (pid <= 0) return null;
        long? found = null;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != (uint)pid || !IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != 0) return true;
            found = hwnd;
            return false;
        }, 0);
        return found;
    }

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
        ForegroundChanged?.Invoke(hwnd);
    }

    public void Dispose()
    {
        if (hook == 0) return;
        UnhookWinEvent(hook);
        hook = 0;
    }
}
