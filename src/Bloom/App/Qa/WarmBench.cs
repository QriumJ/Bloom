using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Ai;

namespace Bloom.App;

/// `--qa-warm <file>` (with --data): the time from sending to the first text of the answer through the real CLI, 3 rounds
/// of each way; the numbers go to the file and Bloom quits. Nothing touches the mouse or keyboard.
/// Claude Code (haiku, the default): a cold chat (the process starts with the message) and a pre-started one
/// (Chats.PreStart, then the message 0.3, 0.8, 1.5 or 3 s after the request — the time a user takes to type).
/// ChatGPT (`--qa-model chatgpt:<slug>`, `--qa-effort` its level): one `codex exec` per message (the old way), a cold
/// `codex app-server` and a pre-started one (sent 0.8 or 3 s after the request); then the idle memory and CPU of a
/// pre-started app-server and everything it started, over 30 s.
internal static class WarmBench
{
    private sealed record Way(string Name, int Lead, bool AppServer); // Lead -1: not pre-started

    public static async Task Run(Host host, string file, string? model = null, string? effort = null)
    {
        var text = new StringBuilder();
        bool gpt = model is not null && AiModels.IsChatGpt(model);
        Way[] ways = gpt
            ? [new("exec (one process per message)", -1, false), new("app-server cold", -1, true), new("app-server pre-started, sent 800 ms after", 800, true), new("app-server pre-started, sent 3000 ms after", 3000, true)]
            : [.. new[] { -1, 300, 800, 1500, 3000 }.Select(l => new Way(l < 0 ? "cold" : $"pre-started, sent {l} ms after", l, true))];
        var times = ways.ToDictionary(w => w, _ => new List<double>());
        await Task.Delay(4000); // start-up settles
        for (int round = 0; round < 3; round++)
        {
            foreach (var way in ways)
            {
                double? ms = await One(host, gpt ? model! : "haiku", gpt ? effort : null, way);
                if (ms is { } m) times[way].Add(m);
                text.AppendLine($"round {round + 1} · {way.Name} · {(ms is { } v ? $"{v:0} ms" : "no answer")}");
                await Task.Delay(1500);
            }
        }
        text.AppendLine();
        foreach (var way in ways)
        {
            var sorted = times[way].OrderBy(x => x).ToList();
            if (sorted.Count > 0) text.AppendLine($"{way.Name}: median {sorted[sorted.Count / 2]:0} ms of {sorted.Count} (min {sorted[0]:0}, max {sorted[^1]:0})");
        }
        if (gpt) await Idle(host, model!, effort, text);
        File.WriteAllText(file, text.ToString());
        host.Quit();
    }

    /// A pre-started app-server left alone: its process tree's memory, and the CPU it uses over 30 s.
    private static async Task Idle(Host host, string model, string? effort, StringBuilder text)
    {
        var chats = host.Chats;
        chats.CodexAppServer = true;
        chats.PreStartOn = true;
        var t = chats.New();
        t.Model = model;
        if (effort is not null) t.Effort = effort;
        chats.PreStart(t);
        var clock = Stopwatch.StartNew();
        int? pid = null;
        while (pid is null && clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(50);
            if (chats.SessionOf(t) is CodexAppSession { Alive: true, ProcessId: int id }) pid = id;
        }
        text.AppendLine();
        if (pid is not { } root)
        {
            text.AppendLine("idle: the pre-started app-server did not come up");
            chats.CancelPreStart(t);
            return;
        }
        text.AppendLine($"idle: pre-started app-server ready {clock.ElapsedMilliseconds} ms after the request");
        await Task.Delay(10000); // its MCP servers and the like settle
        var before = Cpu(Tree(root));
        var wall = Stopwatch.StartNew();
        await Task.Delay(30000);
        var tree = Tree(root);
        var after = Cpu(tree);
        double cpu = tree.Sum(p => after.GetValueOrDefault(p) - before.GetValueOrDefault(p));
        long working = 0, priv = 0;
        foreach (int id in tree)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                working += p.WorkingSet64;
                priv += p.PrivateMemorySize64;
                text.AppendLine($"  pid {id} {p.ProcessName}: working set {p.WorkingSet64 / 1048576.0:0.0} MB · private {p.PrivateMemorySize64 / 1048576.0:0.0} MB");
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { /* ended meanwhile */ }
        }
        text.AppendLine($"idle total ({tree.Count} processes): working set {working / 1048576.0:0.0} MB · private {priv / 1048576.0:0.0} MB · "
            + $"CPU {cpu:0} ms in {wall.Elapsed.TotalSeconds:0} s ({cpu / wall.Elapsed.TotalMilliseconds * 100:0.00} % of one core)");
        chats.CancelPreStart(t);
    }

    /// The process and every process under it.
    private static List<int> Tree(int root)
    {
        var all = Native.Win32.ProcessTree();
        var found = new List<int> { root };
        for (int i = 0; i < found.Count; i++)
            found.AddRange(all.Where(p => p.Value.parent == found[i] && p.Key != found[i] && !found.Contains(p.Key)).Select(p => p.Key));
        return found;
    }

    /// Each process' CPU time so far, in ms.
    private static Dictionary<int, double> Cpu(List<int> ids)
    {
        var cpu = new Dictionary<int, double>();
        foreach (int id in ids)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                cpu[id] = p.TotalProcessorTime.TotalMilliseconds;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { /* ended */ }
        }
        return cpu;
    }

    private static async Task<double?> One(Host host, string model, string? effort, Way way)
    {
        var chats = host.Chats;
        chats.PreStartOn = true;
        chats.CodexAppServer = way.AppServer;
        var t = chats.New();
        t.Model = model;
        if (effort is not null) t.Effort = effort;
        int lead = way.Lead;
        if (lead >= 0)
        {
            chats.PreStart(t); // the process starts after the 250 ms the request waits for the next one
            await Task.Delay(lead);
        }
        var clock = new Stopwatch();
        double? first = null;
        void OnUpdate(ChatThread x)
        {
            if (ReferenceEquals(x, t) && first is null && t.Messages.Count > 1 && t.Messages[^1].Text.Length > 0) first = clock.Elapsed.TotalMilliseconds;
        }
        chats.Updated += OnUpdate;
        clock.Start();
        chats.Send(t, "Reply with the single word: ok");
        while (t.Busy && clock.Elapsed < TimeSpan.FromSeconds(60)) await Task.Delay(5);
        chats.Updated -= OnUpdate;
        bool error = t.Messages.Count > 1 && t.Messages[^1].Error;
        chats.Delete(t);
        return error ? null : first;
    }
}
