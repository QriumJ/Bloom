using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Bloom.Ai;

/// A Windows job object holding one run_command (ApiCommand) and everything it starts: KILL_ON_JOB_CLOSE and no
/// breakaway, so ending the job — stop, the time limit, the command's own end, or this object disposed — ends every
/// process in it, detached ones (Start-Process) included. Processes a child creates join its job by themselves.
internal sealed class CommandJob : IDisposable
{
    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public JobHandle() : base(true) { }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }

    private const uint KillOnJobClose = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; BREAKAWAY_OK is left out on purpose
    private const int BasicAccountingInformation = 1, ExtendedLimitInformation = 9; // JOBOBJECTINFOCLASS

    private readonly JobHandle job;

    private CommandJob(JobHandle job) => this.job = job;

    /// A new job, its limits set; null when Windows refused (the command is then not run).
    public static CommandJob? Create()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            job.Dispose();
            return null;
        }
        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
        if (!SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            job.Dispose();
            return null;
        }
        return new CommandJob(job);
    }

    /// Puts a process just created, still suspended (ChildProcess), into the job: it has run nothing yet, so nothing it
    /// starts can be outside the job.
    public bool Add(IntPtr process) => AssignProcessToJobObject(job, process);

    /// Ends every process in the job, and waits (up to KillWait) until they are gone: termination only begins when
    /// TerminateJobObject returns, and until a process is gone it holds the chat's folder (its working folder) and the
    /// output pipe.
    public void Kill()
    {
        Terminate();
        _ = WaitEmpty((int)KillWait.TotalMilliseconds);
    }

    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

    /// Ends every process in the job without waiting for them (WaitEmpty does).
    public void Terminate() => _ = TerminateJobObject(job, 1); // false: the job is empty already

    /// True once no process is left in the job, within `milliseconds`; false while one is, or it can't be told.
    public bool WaitEmpty(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Active() is not 0)
        {
            if (Environment.TickCount64 >= deadline) return false;
            Thread.Sleep(10);
        }
        return true;
    }

    /// Processes still in the job (null when it can't be told).
    private uint? Active() =>
        QueryInformationJobObject(job, BasicAccountingInformation, out var info, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero) ? info.ActiveProcesses : null;

    public void Dispose() => job.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern JobHandle CreateJobObjectW(IntPtr security, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(JobHandle job, int infoClass, ref ExtendedLimits info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(JobHandle job, int infoClass, out Accounting info, uint size, IntPtr returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(JobHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(JobHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
