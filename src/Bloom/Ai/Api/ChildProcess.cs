using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Bloom.Ai;

/// A program started for the API chats (a stdio MCP server, run_command's PowerShell) with its standard handles on
/// pipes, no window, and an environment given whole. It is created SUSPENDED, put into its CommandJob, and only then
/// resumed: it has run no instruction before the job holds it, so nothing it starts can be outside the job. The pipes
/// are the only handles it inherits (PROC_THREAD_ATTRIBUTE_HANDLE_LIST), and they are inheritable only for the length of the
/// CreateProcess call, so a process started elsewhere meanwhile (Process.Start passes on every inheritable handle) cannot
/// get them but within that call. The three streams belong to whoever reads and writes them; disposing this closes only the process.
internal sealed class ChildProcess : IDisposable
{
    private const uint CreateSuspended = 0x4, CreateUnicodeEnvironment = 0x400, ExtendedStartupInfo = 0x80000, CreateNoWindow = 0x08000000;
    private const uint UseStdHandles = 0x100;
    private const int HandleListAttribute = 0x20002, InheritFlag = 1;

    private readonly SafeProcessHandle process;
    private readonly ManualResetEvent exited;

    private ChildProcess(SafeProcessHandle process, int id, FileStream input, FileStream output, FileStream error)
    {
        this.process = process;
        Id = id;
        Input = input;
        Output = output;
        Error = error;
        exited = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false) }; // a process handle signals when it ends
    }

    public int Id { get; }

    /// What the program reads (its standard input), what it writes and its errors.
    public FileStream Input { get; }
    public FileStream Output { get; }
    public FileStream Error { get; }

    /// Starts `commandLine` (its first word the program, quoted when it has spaces) in `directory` (null: Bloom's) with
    /// exactly `environment`, inside `job`. Throws Win32Exception when Windows refuses and InvalidOperationException when
    /// the job will not take it (the program is then ended).
    public static ChildProcess Start(string commandLine, string? directory, IReadOnlyDictionary<string, string> environment, CommandJob job)
    {
        SafeFileHandle? inRead = null, inWrite = null, outRead = null, outWrite = null, errRead = null, errWrite = null;
        IntPtr list = IntPtr.Zero, handles = IntPtr.Zero, env = IntPtr.Zero;
        try
        {
            // every handle starts non-inheritable: a process started elsewhere meanwhile (the CLI chats' Process.Start, which
            // passes every inheritable handle on) gets none of them
            MakePipe(out inRead, out inWrite);
            MakePipe(out outRead, out outWrite);
            MakePipe(out errRead, out errWrite);
            var inherited = new[] { inRead.DangerousGetHandle(), outWrite.DangerousGetHandle(), errWrite.DangerousGetHandle() };
            handles = Marshal.AllocHGlobal(IntPtr.Size * inherited.Length);
            for (int i = 0; i < inherited.Length; i++) Marshal.WriteIntPtr(handles, i * IntPtr.Size, inherited[i]);
            IntPtr size = IntPtr.Zero;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size); // fails by design: it tells the size
            list = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size) ||
                !UpdateProcThreadAttribute(list, 0, (IntPtr)HandleListAttribute, handles, (IntPtr)(IntPtr.Size * inherited.Length), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new StartupInfoEx
            {
                Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = UseStdHandles, StdInput = inherited[0], StdOutput = inherited[1], StdError = inherited[2] },
                AttributeList = list,
            };
            env = Marshal.StringToHGlobalUni(EnvironmentBlock(environment));
            var line = new StringBuilder(commandLine, commandLine.Length + 1);
            // the three program-side ends are inheritable only across this call (the handle list names them, so this child
            // takes no other); the window in which another launch could see them is the call itself
            var childEnds = new[] { inRead, outWrite, errWrite };
            ProcessInformation created;
            lock (LaunchGate) // the whole window in which the ends are inheritable: no other launch (Launching) runs in it
            {
                try
                {
                    foreach (var h in childEnds) Inheritable(h, true);
                    if (!CreateProcessW(null, line, IntPtr.Zero, IntPtr.Zero, true, CreateSuspended | CreateUnicodeEnvironment | ExtendedStartupInfo | CreateNoWindow, env, directory, ref info, out created))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally
                {
                    foreach (var h in childEnds) Inheritable(h, false);
                }
            }
            var handle = new SafeProcessHandle(created.Process, ownsHandle: true);
            try
            {
                if (!job.Add(created.Process)) throw new InvalidOperationException("The program could not join its job.");
                if (ResumeThread(created.Thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch
            {
                _ = TerminateProcess(created.Process, 1);
                handle.Dispose();
                throw;
            }
            finally
            {
                _ = CloseHandle(created.Thread);
            }
            // the parent's ends stay; the program's are closed here so that its exit shows as the end of its pipes
            inRead.Dispose();
            outWrite.Dispose();
            errWrite.Dispose();
            return new ChildProcess(handle, created.ProcessId, new FileStream(inWrite, FileAccess.Write, 1, false), new FileStream(outRead, FileAccess.Read, 1, false),
                new FileStream(errRead, FileAccess.Read, 1, false));
        }
        catch
        {
            foreach (var h in new[] { inRead, inWrite, outRead, outWrite, errRead, errWrite }) h?.Dispose();
            throw;
        }
        finally
        {
            if (list != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(list);
                Marshal.FreeHGlobal(list);
            }
            if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
            if (env != IntPtr.Zero) Marshal.FreeHGlobal(env);
        }
    }

    /// Taken by every launch that passes inheritable handles on: this class' own across its CreateProcess call (Start) and
    /// any other (Process.Start, which passes on every inheritable handle there is) through Launching and Spawn. So no
    /// launch ever runs while another's pipe ends are inheritable.
    private static readonly object LaunchGate = new();

    /// `start` (a launch of another program) run while no other launch is, so it cannot inherit this class' pipe ends.
    public static T Launching<T>(Func<T> start)
    {
        lock (LaunchGate) return start();
    }

    /// Process.Start under the launch gate (Launching); a ShellExecute start passes no handles on and needs none.
    public static Process? Spawn(ProcessStartInfo info) => info.UseShellExecute ? Process.Start(info) : Launching(() => Process.Start(info));

    /// A pipe, both ends not inheritable.
    private static void MakePipe(out SafeFileHandle read, out SafeFileHandle write)
    {
        if (!CreatePipe(out read, out write, IntPtr.Zero, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void Inheritable(SafeFileHandle handle, bool on)
    {
        if (!SetHandleInformation(handle, InheritFlag, on ? InheritFlag : 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// name=value\0 … \0\0, sorted by name as Windows asks of a Unicode block.
    private static string EnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in environment.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            sb.Append(name).Append('=').Append(value).Append('\0');
        return sb.Append('\0').ToString();
    }

    public bool HasExited => exited.WaitOne(0);

    /// Waits up to `milliseconds` for the program to end.
    public bool WaitForExit(int milliseconds) => exited.WaitOne(milliseconds);

    /// Completes when the program ends (not waiting for its pipes), or cancels with `ct`.
    public async Task WaitForExitAsync(CancellationToken ct)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = ThreadPool.RegisterWaitForSingleObject(exited, (_, _) => done.TrySetResult(), null, Timeout.Infinite, executeOnlyOnce: true);
        try
        {
            using (ct.Register(() => done.TrySetCanceled(ct))) await done.Task.ConfigureAwait(false);
        }
        finally
        {
            wait.Unregister(null);
        }
    }

    public int ExitCode => GetExitCodeProcess(process, out int code) ? code : -1;

    /// The program ends now (what it started is the job's to end).
    public void Kill() => _ = TerminateProcess(process.DangerousGetHandle(), 1);

    public void Dispose()
    {
        exited.Dispose();
        process.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute;
        public uint Flags;
        public short ShowWindow, ReservedBytes;
        public IntPtr ReservedData, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo Startup;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string? application, StringBuilder commandLine, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment,
        string? directory, ref StartupInfoEx startup, out ProcessInformation created);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr security, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, int mask, int flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out int exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
