using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using Bloom.Native;

namespace Bloom.Core;

/// Records the moments the UI thread stops answering. A watcher thread posts a no-op at Input priority (where typed
/// keys wait) and times it on the unbiased clock (the PC's sleep doesn't count). A no-op that waited `Noted` or longer
/// goes to the log with how long; one still waiting at `DumpAt` also gets a dump taken while the UI thread is still
/// held, so its stack shows what held it. At most one dump per `DumpGap`.
internal sealed class StallWatch : IDisposable
{
    public static readonly TimeSpan Every = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Noted = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DumpAt = TimeSpan.FromMilliseconds(800);
    public static readonly TimeSpan DumpGap = TimeSpan.FromMinutes(10);
    public const int KeptDumps = 5;

    private readonly Dispatcher ui;
    private readonly Action<string> log;
    private readonly Func<string?> dump;
    private readonly Thread thread;
    private volatile bool stopped;
    private long sent = -1; // when the waiting no-op went out (Now); -1: none out
    private long answered = -1; // when it ran

    /// `dump` writes a dump of this process and returns its file name, or null when that failed.
    public StallWatch(Dispatcher ui, Action<string> log, Func<string?> dump)
    {
        this.ui = ui;
        this.log = log;
        this.dump = dump;
        thread = new Thread(Run) { IsBackground = true, Name = "stall watch", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    /// Milliseconds on the unbiased interrupt clock: it stands still while the PC sleeps or hibernates.
    private static long Now()
    {
        _ = Win32.QueryUnbiasedInterruptTime(out ulong t);
        return (long)(t / 10_000);
    }

    private void Run()
    {
        long lastDump = long.MinValue / 2;
        bool dumpTried = false;
        string? stallDump = null;
        long dumpMs = 0;
        while (true)
        {
            Thread.Sleep(Every);
            if (stopped || ui.HasShutdownStarted) return; // quitting: its saving may hold the UI thread on purpose
            long s = Interlocked.Read(ref sent), a = Interlocked.Read(ref answered);
            if (s >= 0 && a >= 0)
            {
                long waited = a - s;
                if (waited >= Noted.TotalMilliseconds)
                    log($"ui stall {waited} ms" + (stallDump is not null ? $" · dump {stallDump} (writing it {dumpMs} ms)" : dumpTried ? " · dump failed" : ""));
                dumpTried = false;
                stallDump = null;
                s = -1;
            }
            if (s < 0)
            {
                Interlocked.Exchange(ref answered, -1);
                Interlocked.Exchange(ref sent, Now());
                ui.BeginInvoke(DispatcherPriority.Input, Answer);
                continue;
            }
            long now = Now();
            if (!dumpTried && now - s >= DumpAt.TotalMilliseconds && now - lastDump >= DumpGap.TotalMilliseconds)
            {
                dumpTried = true;
                lastDump = now;
                stallDump = dump();
                dumpMs = Now() - now;
            }
        }
    }

    private void Answer() => Interlocked.Exchange(ref answered, Now());

    /// A triage dump of this process (every thread's stack, about 1 MB, no heap) into `dir`, written by the runtime at
    /// the request `dotnet-dump collect` sends over its diagnostics pipe; returns its file name, or null when it failed
    /// (logged). Only the newest `KeptDumps` stay.
    public static string? TriageDump(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string name = $"stall-{DateTime.Now:yyyyMMdd-HHmmss}.dmp";
            string file = Path.Combine(dir, name);
            using (var pipe = new NamedPipeClientStream(".", "dotnet-diagnostic-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture), PipeDirection.InOut))
            {
                pipe.Connect(3000);
                pipe.Write(DumpRequest(file));
                var reply = new byte[24];
                pipe.ReadExactly(reply);
                uint result = BitConverter.ToUInt32(reply, 20);
                if (reply[16] != 0xFF || reply[17] != 0 || result != 0)
                {
                    Log.Write($"stall dump: the runtime said {reply[17]:X2} {result:X8}");
                    return null;
                }
            }
            foreach (var old in new DirectoryInfo(dir).GetFiles("stall-*.dmp").OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(KeptDumps)) old.Delete();
            return name;
        }
        catch (Exception e)
        {
            Log.Error("stall dump", e);
            return null;
        }
    }

    /// The diagnostics IPC's GenerateCoreDump (command set 1, command 1): the header ("DOTNET_IPC_V1\0", the whole
    /// size, set, command, reserved), then the file name (its length in UTF-16 units with the \0, the units), the dump
    /// type (3: triage) and no diagnostics. The reply is a header with set 0xFF, command 0 (OK) and a 32-bit HRESULT.
    internal static byte[] DumpRequest(string file)
    {
        byte[] name = Encoding.Unicode.GetBytes(file + "\0");
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("DOTNET_IPC_V1\0"));
        w.Write((ushort)(20 + 4 + name.Length + 4 + 4));
        w.Write((byte)1);
        w.Write((byte)1);
        w.Write((ushort)0);
        w.Write((uint)(file.Length + 1));
        w.Write(name);
        w.Write(3u);
        w.Write(0u);
        return ms.ToArray();
    }

    public void Dispose() => stopped = true;
}
