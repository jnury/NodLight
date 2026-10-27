using System.Runtime.InteropServices;
using NodLight.Core;

namespace NodLight.Hook;

/// <summary>Process tree backed by one Toolhelp32 snapshot (pid -> parent, exe name).</summary>
internal sealed class Win32ProcessTree : IProcessTree
{
    readonly Dictionary<int, (int Parent, string Name)> map = BuildMap();

    public ProcessInfo? Get(int pid)
    {
        if (!map.TryGetValue(pid, out var entry)) return null;
        return new ProcessInfo(pid, entry.Parent, entry.Name, StartUtcOf(pid));
    }

    /// <summary>Pids in parent order starting at <paramref name="startPid"/>, for --dump-chain.</summary>
    public IEnumerable<ProcessInfo> Chain(int startPid)
    {
        var seen = new HashSet<int>();
        var cur = Get(startPid);
        while (cur is not null && seen.Add(cur.Pid))
        {
            yield return cur;
            cur = Get(cur.ParentPid);
        }
    }

    static DateTime StartUtcOf(int pid)
    {
        var h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == IntPtr.Zero) return DateTime.MinValue;
        try
        {
            if (!GetProcessTimes(h, out var creation, out _, out _, out _)) return DateTime.MinValue;
            return DateTime.FromFileTimeUtc(creation);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    static Dictionary<int, (int, string)> BuildMap()
    {
        var result = new Dictionary<int, (int, string)>();
        var snap = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snap == InvalidHandle) return result;
        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (var ok = Process32FirstW(snap, ref entry); ok; ok = Process32NextW(snap, ref entry))
                result[(int)entry.th32ProcessID] = ((int)entry.th32ParentProcessID, entry.szExeFile);
        }
        finally
        {
            CloseHandle(snap);
        }
        return result;
    }

    const uint Th32csSnapProcess = 0x2;
    const uint ProcessQueryLimitedInformation = 0x1000;
    static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32FirstW(IntPtr snap, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32NextW(IntPtr snap, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr h);
}
