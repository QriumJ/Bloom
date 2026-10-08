using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Bloom.Core;

namespace Bloom.Search;

/// One app as Kill Process lists it: every process of the same program in this Windows session.
internal sealed class ProcGroup
{
    public string Key = ""; // exe path, or the name when the path is hidden
    public string Name = "";
    public string Exe = "";
    public string? Path;
    public readonly List<int> Pids = new();
    public long Memory; // private working set, bytes (Task Manager's "메모리")
    public double Cpu = -1; // % of the whole machine; -1 = not measured yet
    public bool Protected; // Windows needs it to keep running
    public bool Self;
    public MatchTarget Match = null!;
}

/// 프로세스 종료: the session's programs with their CPU and memory, from one
/// NtQuerySystemInformation snapshot (no handle per process, except to learn a new exe path once).
internal sealed class Processes
{
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "wininit", "winlogon", "smss", "services", "lsass", "svchost", "dwm", "fontdrvhost", "sihost", "ctfmon",
        "conhost", "dllhost", "runtimebroker", "taskhostw", "startmenuexperiencehost", "shellexperiencehost", "searchhost",
        "textinputhost", "lockapp", "userinit", "audiodg", "spoolsv", "memory compression", "registry", "system", "secure system",
    };

    private readonly object gate = new();
    private Dictionary<int, (long create, long cpu)> last = new();
    private long lastAt;
    private readonly Dictionary<(int pid, long create), string?> paths = new();
    private readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
    private List<ProcGroup> groups = new();
    private int sampling;

    public IReadOnlyList<ProcGroup> Groups
    {
        get
        {
            lock (gate) return groups;
        }
    }

    public bool Measured { get; private set; }

    /// A new snapshot; CPU is the share since the previous one.
    public void Sample()
    {
        if (Interlocked.Exchange(ref sampling, 1) == 1) return;
        try
        {
            var rows = Snapshot();
            long now = Stopwatch.GetTimestamp();
            double wall = lastAt == 0 ? 0 : (now - lastAt) * 1e7 / Stopwatch.Frequency; // 100-ns units
            int session = Process.GetCurrentProcess().SessionId;
            int self = Environment.ProcessId;
            var map = new Dictionary<string, ProcGroup>(StringComparer.OrdinalIgnoreCase);
            var seen = new Dictionary<int, (long, long)>();
            foreach (var r in rows)
            {
                seen[r.pid] = (r.create, r.cpu);
                if (r.pid <= 4 || r.session != session) continue;
                string exe = r.name;
                string bare = System.IO.Path.GetFileNameWithoutExtension(exe);
                string? path = PathOf(r.pid, r.create);
                string key = path ?? exe;
                if (!map.TryGetValue(key, out var g))
                {
                    map[key] = g = new ProcGroup { Key = key, Exe = exe, Path = path, Name = DisplayName(path, bare), Protected = Critical.Contains(bare) };
                    g.Match = new MatchTarget(g.Name, new[] { bare, exe });
                }
                g.Pids.Add(r.pid);
                g.Memory += r.privateWs;
                if (r.pid == self) g.Self = true;
                if (wall > 0 && last.TryGetValue(r.pid, out var before) && before.create == r.create)
                    g.Cpu = Math.Max(0, g.Cpu) + (r.cpu - before.cpu) / wall / Environment.ProcessorCount * 100;
                else if (wall > 0 && g.Cpu < 0) g.Cpu = 0;
            }
            last = seen;
            lastAt = now;
            Measured = wall > 0;
            var list = map.Values.ToList();
            lock (gate) groups = list;
            // forget the paths of processes that are gone
            if (paths.Count > 4000) paths.Clear();
        }
        catch (Exception e)
        {
            Log.Error("processes", e);
        }
        finally
        {
            Interlocked.Exchange(ref sampling, 0);
        }
    }

    public void Reset()
    {
        last = new();
        lastAt = 0;
        Measured = false;
    }

    private string? PathOf(int pid, long create)
    {
        if (paths.TryGetValue((pid, create), out var p)) return p;
        p = Native.Win32.ProcessPath((uint)pid);
        paths[(pid, create)] = p;
        return p;
    }

    private string DisplayName(string? path, string bare)
    {
        if (path is null) return bare;
        if (names.TryGetValue(path, out var n)) return n;
        try
        {
            string? d = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            // some exes describe themselves as a sentence or leave it empty
            n = string.IsNullOrEmpty(d) || d.Length > 48 ? bare : d;
        }
        catch
        {
            n = bare;
        }
        names[path] = n;
        return n;
    }

    // ── ending them ─────────────────────────────────────────────────────────────────────────────
    /// Force: TerminateProcess on each. Otherwise each window is asked to close (apps may ask to save).
    /// → (ended, refused): refused = elevated or protected processes.
    public static (int ended, int refused) End(ProcGroup g, bool force)
    {
        int ended = 0, refused = 0;
        foreach (int pid in g.Pids)
        {
            if (pid == Environment.ProcessId) continue;
            try
            {
                using var p = Process.GetProcessById(pid);
                if (force)
                {
                    p.Kill();
                    ended++;
                }
                else if (p.CloseMainWindow()) ended++;
            }
            catch (ArgumentException)
            {
                ended++; // already gone
            }
            catch (Exception)
            {
                refused++; // access denied (elevated), or exiting
            }
        }
        return (ended, refused);
    }

    // ── the snapshot ────────────────────────────────────────────────────────────────────────────
    private readonly record struct Row(int pid, string name, int session, long create, long cpu, long privateWs);

    private static List<Row> Snapshot()
    {
        int size = 512 * 1024;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQuerySystemInformation(5 /* SystemProcessInformation */, buf, size, out int needed);
                if (status == unchecked((int)0xC0000004)) // STATUS_INFO_LENGTH_MISMATCH
                {
                    size = Math.Max(needed, size) + 64 * 1024;
                    continue;
                }
                if (status != 0) throw new InvalidOperationException($"NtQuerySystemInformation 0x{status:X8}");
                var rows = new List<Row>(400);
                long offset = 0;
                while (true)
                {
                    IntPtr p = buf + (int)offset;
                    int next = Marshal.ReadInt32(p, 0);
                    long privateWs = Marshal.ReadInt64(p, 8);
                    long create = Marshal.ReadInt64(p, 32);
                    long user = Marshal.ReadInt64(p, 40);
                    long kernel = Marshal.ReadInt64(p, 48);
                    ushort nameLen = (ushort)Marshal.ReadInt16(p, 56);
                    IntPtr namePtr = Marshal.ReadIntPtr(p, 64);
                    int pid = (int)Marshal.ReadIntPtr(p, 80);
                    int session = Marshal.ReadInt32(p, 100);
                    string name = namePtr == IntPtr.Zero ? (pid == 0 ? "Idle" : "System") : Marshal.PtrToStringUni(namePtr, nameLen / 2);
                    rows.Add(new Row(pid, name, session, create, user + kernel, privateWs));
                    if (next == 0) break;
                    offset += next;
                }
                return rows;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        return new List<Row>();
    }

    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int cls, IntPtr info, int length, out int returnLength);
}
