using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CSIViewer.Host.Login;

/// <summary>Launches only this installed executable, using its own SYSTEM token.</summary>
internal static class MachineBroker
{
    public static void Run(LoginConfiguration config, Func<bool>? keepRunning = null)
    {
        _ = config;
        using var singleton = new Mutex(true, @"Global\CSIViewer.Host.Login.Broker", out var created);
        if (!created) throw new InvalidOperationException("A machine broker already exists.");
        if (!OpenProcessToken(GetCurrentProcess(), 0xF01FF, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            foreach (var privilege in new[] { "SeTcbPrivilege", "SeAssignPrimaryTokenPrivilege", "SeIncreaseQuotaPrivilege" }) EnablePrivilege(token, privilege);
            using var job = new WorkerJob();
            Process? child = null;
            uint childSession = 0;
            File.Delete(LoginConfiguration.WorkerStopPath);
            try
            {
                while ((keepRunning?.Invoke() ?? true) && File.Exists(LoginConfiguration.ConfigPath) && !File.Exists(LoginConfiguration.StopPath))
                {
                    var session = WTSGetActiveConsoleSessionId();
                    if (child is not null && child.HasExited && child.ExitCode != 0)
                        throw new InvalidOperationException("The login worker failed; administrator review is required before restarting.");
                    if (child is not null && (child.HasExited || childSession != session))
                    {
                        File.WriteAllText(LoginConfiguration.WorkerStopPath, "console change");
                        if (!child.HasExited && !child.WaitForExit(5000)) child.Kill(entireProcessTree: true);
                        child.Dispose();
                        child = null;
                        File.Delete(LoginConfiguration.WorkerStopPath);
                    }
                    if (child is null && session is > 0 and < uint.MaxValue && !File.Exists(LoginConfiguration.RevokedPath))
                    {
                        InstallationSecurity.ValidateInstallation();
                        child = Launch(token, session, job.Handle);
                        childSession = session;
                    }
                    Thread.Sleep(2000);
                }
            }
            finally
            {
                // The worker sees stop.request and ends encrypted sessions first.
                if (child is not null)
                {
                    File.WriteAllText(LoginConfiguration.WorkerStopPath, "broker stopping");
                    if (!child.HasExited && !child.WaitForExit(5000)) child.Kill(entireProcessTree: true);
                    child.Dispose();
                }
            }
        }
        finally { CloseHandle(token); }
    }

    private static Process Launch(IntPtr sourceToken, uint session, IntPtr job)
    {
        if (!DuplicateTokenEx(sourceToken, 0xF01FF, IntPtr.Zero, 2, 1, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!SetTokenInformation(token, 12, ref session, sizeof(uint))) throw new Win32Exception(Marshal.GetLastWin32Error());
            var executable = Environment.ProcessPath!;
            var command = ($"\"{executable}\" --worker\0").ToCharArray();
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), desktop = @"winsta0\default" };
            // Suspended until confined to the broker-owned kill-on-close job.
            if (!CreateProcessAsUser(token, executable, command, IntPtr.Zero, IntPtr.Zero,
                    false, 0x08000004, IntPtr.Zero, LoginConfiguration.InstallDirectory, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!AssignProcessToJobObject(job, info.process)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var process = Process.GetProcessById((int)info.pid);
                _ = process.Handle; // Pin the process identity before resuming it.
                if (ResumeThread(info.thread) == uint.MaxValue) { process.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
                return process;
            }
            catch { TerminateProcess(info.process, 1); throw; }
            finally { CloseHandle(info.thread); CloseHandle(info.process); }
        }
        finally { CloseHandle(token); }
    }

    private static void EnablePrivilege(IntPtr token, string name)
    {
        if (!LookupPrivilegeValue(null, name, out var luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var privileges = new TokenPrivileges { count = 1, luid = luid, attributes = 2 };
        if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal static uint ActiveConsole => WTSGetActiveConsoleSessionId();
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint low; public int high; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint count; public Luid luid; public uint attributes; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb; public string? reserved; public string? desktop; public string? title;
        public uint x, y, width, height, charsX, charsY, fill, flags; public ushort show, reservedSize;
        public IntPtr reservedPointer, stdin, stdout, stderr;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr process, thread; public uint pid, tid; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges privileges, uint length, IntPtr previous, IntPtr returned);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int level, int type, out IntPtr duplicate);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(IntPtr token, int kind, ref uint session, int length);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessAsUser(IntPtr token, string app, [In, Out] char[] command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint code);

    private sealed class WorkerJob : IDisposable
    {
        public IntPtr Handle { get; }
        public WorkerJob()
        {
            Handle = CreateJobObject(IntPtr.Zero, null);
            if (Handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var limits = new ExtendedLimits { basic = new BasicLimits { flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(Handle, 9, ref limits, Marshal.SizeOf<ExtendedLimits>()))
            {
                var error = Marshal.GetLastWin32Error(); CloseHandle(Handle); throw new Win32Exception(error);
            }
        }
        public void Dispose() => CloseHandle(Handle);
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long processTime, jobTime; public uint flags; public UIntPtr minWorking, maxWorking; public uint processes; public UIntPtr affinity; public uint priority, scheduling; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong readOps, writeOps, otherOps, readBytes, writeBytes, otherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits basic; public IoCounters io; public UIntPtr processMemory, jobMemory, peakProcess, peakJob; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref ExtendedLimits limits, int length);
    }
}
