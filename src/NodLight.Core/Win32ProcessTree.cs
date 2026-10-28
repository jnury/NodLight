using System.Runtime.InteropServices;

namespace NodLight.Core;

/// <summary>Process tree backed by one Toolhelp32 snapshot (pid -> parent, exe name).</summary>
public sealed class Win32ProcessTree : IProcessTree
{
    readonly Dictionary<int, (int Parent, string Name)> map = BuildMap();

    public ProcessInfo? Get(int pid)
    {
        if (!map.TryGetValue(pid, out var entry)) return null;
        return new ProcessInfo(pid, entry.Parent, entry.Name, StartUtcOf(pid));
    }

    /// <summary>
    /// Counters of every process below <paramref name="rootPid"/> (the root itself excluded). Processes that cannot be
    /// opened (other users, protected) are skipped; one whose I/O counters cannot be read reports zero I/O.
    /// </summary>
    public IReadOnlyList<ProcessCounters> DescendantCounters(int rootPid)
    {
        var starts = new Dictionary<int, DateTime>();
        DateTime StartOf(int pid)
        {
            if (!starts.TryGetValue(pid, out var start)) starts[pid] = start = StartUtcOf(pid);
            return start;
        }

        var children = map.ToLookup(kv => kv.Value.Parent, kv => kv.Key);
        var result = new List<ProcessCounters>();
        foreach (var pid in ProcessTreeWalk.Descendants(rootPid, children, StartOf))
            if (ReadCounters(pid, map[pid].Name, StartOf(pid)) is { } counters) result.Add(counters);
        return result;
    }

    static ProcessCounters? ReadCounters(int pid, string name, DateTime start)
    {
        var withIo = true;
        var h = OpenProcess(ProcessQueryLimitedInformation | ProcessQueryInformation, false, pid);
        if (h == IntPtr.Zero)
        {
            withIo = false;
            h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        }
        if (h == IntPtr.Zero) return null;
        try
        {
            if (!GetProcessTimes(h, out _, out _, out var kernel, out var user)) return null;
            var memory = new ProcessMemoryCounters { cb = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
            var workingSet = K32GetProcessMemoryInfo(h, ref memory, memory.cb) ? (long)memory.WorkingSetSize : 0;
            var io = withIo && GetProcessIoCounters(h, out var counters)
                ? (long)(counters.ReadTransferCount + counters.WriteTransferCount)
                : 0;
            return new ProcessCounters(pid, name, start, kernel + user, workingSet, io);
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>
    /// The current directory of a 64-bit process of the same user, read from its PEB; null when it cannot be read.
    /// Only valid from a 64-bit process (the PEB offsets below are the x64 ones).
    /// </summary>
    public static string? CurrentDirectoryOf(int pid)
    {
        if (!Environment.Is64BitProcess) return null;
        var h = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var info = new ProcessBasicInformation();
            if (NtQueryInformationProcess(h, 0, ref info, (uint)Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0
                || info.PebBaseAddress == IntPtr.Zero)
                return null;

            // PEB.ProcessParameters at +0x20; RTL_USER_PROCESS_PARAMETERS.CurrentDirectory.DosPath (UNICODE_STRING) at +0x38.
            var parameters = new IntPtr[1];
            if (!ReadProcessMemory(h, info.PebBaseAddress + 0x20, parameters, (IntPtr)IntPtr.Size, out _)
                || parameters[0] == IntPtr.Zero)
                return null;

            var unicode = new UnicodeString();
            if (!ReadProcessMemory(h, parameters[0] + 0x38, ref unicode, (IntPtr)Marshal.SizeOf<UnicodeString>(), out _)
                || unicode.Length == 0 || unicode.Buffer == IntPtr.Zero)
                return null;

            var bytes = new byte[unicode.Length];
            if (!ReadProcessMemory(h, unicode.Buffer, bytes, (IntPtr)unicode.Length, out _)) return null;
            var path = System.Text.Encoding.Unicode.GetString(bytes);
            return path.Length > 3 ? path.TrimEnd('\\') : path;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>Every process in the snapshot.</summary>
    public IEnumerable<ProcessInfo> All() => map.Keys.Select(pid => Get(pid)!);

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
    const uint ProcessQueryInformation = 0x0400;
    const uint ProcessVmRead = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(
        IntPtr h, int infoClass, ref ProcessBasicInformation info, uint size, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ReadProcessMemory(IntPtr h, IntPtr address, [Out] IntPtr[] buffer, IntPtr size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ReadProcessMemory(IntPtr h, IntPtr address, ref UnicodeString buffer, IntPtr size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ReadProcessMemory(IntPtr h, IntPtr address, [Out] byte[] buffer, IntPtr size, out IntPtr read);

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessMemoryCounters
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool K32GetProcessMemoryInfo(IntPtr h, ref ProcessMemoryCounters counters, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetProcessIoCounters(IntPtr h, out IoCounters counters);
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
