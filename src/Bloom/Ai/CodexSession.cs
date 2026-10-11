using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// ChatGPT through the user's Codex CLI and its ChatGPT login: one `codex exec --json` per turn,
/// later turns continuing the same Codex thread with `codex exec resume <id>` (a new model or level
/// goes with the next turn). It runs read-only in the launcher's AI folder without the user's
/// config.toml (so no MCP servers start), and as a chat rather than a coding agent: our own base
/// instructions instead of Codex's, live web search, no sandbox or environment notes (about half the
/// input tokens). Codex reports whole messages, not text deltas. With 설정 > PC 작업 on, each run also gets Bloom's
/// own MCP server (`-c mcp_servers.bloom…`, the relay named after this conversation); Codex in exec mode can't ask the
/// user, so it is told to approve the tool itself and Bloom's server holds every action for the user's yes (Ask).
internal sealed class CodexSession : IChatSession
{
    /// Bloom's MCP server for a run (Host: starts it on first use); null when ChatGPT chats don't get it.
    public static Func<(string command, string pipe, string hint)?>? PcTools;

    /// The other MCP servers the user switched on, and Bloom's relay to put in front of each (Host).
    public static Func<List<McpServerDef>>? ExtraMcp;
    public static Func<(string command, string pipe)>? Relay;

    private Process? proc;
    private readonly object gate = new();
    private string? exe;
    private string cwd = "";
    private bool stopped;

    public string Model { get; }
    public string Effort { get; }
    public string? AccountHome { get; init; } // the chat account's folder (CODEX_HOME); null = Codex's default
    public string? SessionId { get; private set; }
    public bool Busy { get; private set; }
    public bool Alive { get; private set; }
    public DateTime LastUsed { get; private set; } = DateTime.UtcNow;

    public event Action<string>? SessionStarted;
    public event Action<string>? TextDelta
    {
        add { } // Codex sends whole messages (AssistantText)
        remove { }
    }
    public event Action<string?>? Status;
    public event Action<string>? AssistantText;
    public event Action<bool, string?>? TurnEnded;
    public event Action<string>? Exited;

    // Bloom's MCP server asks here before an action (Ask); the chat answers through Answer
    public event Action<ToolAsk>? ToolAsked;
    public event Action<string>? ToolAskWithdrawn;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<bool>> asks = new();

    /// An action Bloom's MCP server holds for the user: the chat shows the card, the task ends with their answer — or
    /// no, when the turn ends, it is stopped, or nobody answers within 9 minutes (Codex gives up on the call at 10).
    public Task<bool> Ask(string tool, string input)
    {
        var ask = new ToolAsk(Guid.NewGuid().ToString("N"), tool, input, "");
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        asks[ask.RequestId] = done;
        _ = Task.Delay(TimeSpan.FromMinutes(9)).ContinueWith(_ => Withdraw(ask.RequestId), TaskScheduler.Default);
        ToolAsked?.Invoke(ask);
        return done.Task;
    }

    public void Answer(ToolAsk ask, bool allow)
    {
        if (asks.TryRemove(ask.RequestId, out var done)) done.TrySetResult(allow);
    }

    private void Withdraw(string id)
    {
        if (!asks.TryRemove(id, out var done)) return;
        done.TrySetResult(false);
        ToolAskWithdrawn?.Invoke(id);
    }

    private void WithdrawAll()
    {
        foreach (string id in asks.Keys) Withdraw(id);
    }

    private readonly string files; // "files/<thread id>", relative to the working folder
    private readonly string conversation; // the relay's name for this chat, so Bloom's server knows where to ask
    private string? pc; // this run has Bloom's tools: the line telling them

    public CodexSession(string model, string effort, string files, string conversation)
    {
        Model = model;
        Effort = effort;
        this.files = files;
        this.conversation = conversation;
    }

    /// The Codex model: the one picked, else Codex's own default.
    private string? Slug => AiModels.GptSlug(Model) ?? GptCatalog.DefaultSlug();

    /// A reasoning level this model takes (an older thread's "fast" becomes its lowest).
    private string Reasoning => AiModels.Fit(Model, Effort);

    public static string? FindCli(string? custom)
    {
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom)) return custom;
        // npm's @openai/codex carries the native exe; codex.cmd / codex.ps1 only start node
        string npm = Path.Combine(Paths.AppData, @"npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe");
        if (File.Exists(npm)) return npm;
        try
        {
            string app = Path.Combine(Paths.LocalAppData, @"OpenAI\Codex\bin");
            if (Directory.Exists(app) && Directory.GetFiles(app, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() is { } desktop)
                return desktop;
        }
        catch
        {
            /* keep looking */
        }
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                string p = Path.Combine(dir.Trim(), "codex.exe");
                if (dir.Length > 0 && File.Exists(p)) return p;
            }
            catch
            {
                /* bad PATH entry */
            }
        }
        return null;
    }

    public bool Start(string? resume, out string? error)
    {
        error = null;
        exe = FindCli(Settings.Current.CodexPath);
        if (exe is null)
        {
            error = AiErrors.CodexMissing;
            return false;
        }
        cwd = Path.GetDirectoryName(Paths.InLocal(@"ai\.keep"))!;
        SessionId = resume;
        Alive = true;
        return true;
    }

    public bool Send(string text, IReadOnlyList<string> images)
    {
        lock (gate)
        {
            if (!Alive || Busy || exe is null) return false;
            var utf8 = new UTF8Encoding(false);
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = utf8,
                StandardErrorEncoding = utf8,
                StandardInputEncoding = utf8,
                WorkingDirectory = cwd,
            };
            var tools = PcTools?.Invoke(); // before the instructions, which mention the tools
            pc = tools?.hint;
            var args = new List<string> { "exec", "--json", "--skip-git-repo-check", "--ignore-user-config", "--sandbox", "read-only", "--cd", cwd, "--color", "never" };
            if (Slug is { Length: > 0 } model)
            {
                args.Add("--model");
                args.Add(model);
            }
            args.Add("-c");
            args.Add($"model_reasoning_effort=\"{Reasoning}\"");
            foreach (string c in new[]
            {
                $"model_instructions_file='{Instructions()}'", // a TOML literal string: the backslashes stay
                "web_search=\"live\"",
                "include_environment_context=false",
                "include_permissions_instructions=false",
            })
            {
                args.Add("-c");
                args.Add(c);
            }
            if (tools is { } t)
                foreach (string c in new[]
                {
                    "mcp_servers.bloom.command=" + Toml(t.command),
                    $"mcp_servers.bloom.args=[\"--mcp\", {Toml(t.pipe)}, {Toml(conversation)}]",
                    "mcp_servers.bloom.default_tools_approval_mode=\"approve\"", // Bloom asks itself, in the chat
                    "mcp_servers.bloom.tool_timeout_sec=600", // a call can wait that long for the user's yes
                    "mcp_servers.bloom.startup_timeout_sec=20",
                })
                {
                    args.Add("-c");
                    args.Add(c);
                }
            // the other servers switched on: each started through Bloom's proxy, which asks in the chat before a call
            // (web-address servers can't be put behind it, so ChatGPT goes without them)
            if (ExtraMcp?.Invoke().Where(d => d.Stdio && d.Name != "bloom").ToList() is { Count: > 0 } extra && Relay?.Invoke() is { } relay)
                foreach (var d in extra)
                {
                    string key = "mcp_servers." + TomlKey(d.Name);
                    foreach (string c in new[]
                    {
                        $"{key}.command={Toml(relay.command)}",
                        $"{key}.args=[\"--mcp-proxy\", {Toml(relay.pipe)}, {Toml(conversation)}, {Toml(d.Source)}, {Toml(d.Name)}]",
                        $"{key}.default_tools_approval_mode=\"approve\"",
                        $"{key}.tool_timeout_sec=600",
                        $"{key}.startup_timeout_sec=30",
                    })
                    {
                        args.Add("-c");
                        args.Add(c);
                    }
                }
            if (SessionId is { } id)
            {
                args.Add("resume");
                args.Add(id);
            }
            foreach (string image in images)
                if (File.Exists(image)) args.Add("--image=" + image); // one value each: "-" stays the prompt
            args.Add("-"); // the prompt comes on stdin
            foreach (string a in args) psi.ArgumentList.Add(a);
            Cli.CleanEnvironment(psi); // the ChatGPT login only
            AiAccounts.UseChatAccount(psi, "chatgpt", AccountHome);
            _ = CodexAuth.Unsaved(AccountHome); // tokens the light way refreshed go in first (they were the folder's before it was picked)
            Process p;
            try
            {
                p = Process.Start(psi)!;
            }
            catch (Exception e)
            {
                Log.Error("codex start", e);
                return false;
            }
            proc = p;
            stopped = false;
            Busy = true;
            LastUsed = DateTime.UtcNow;
            var stderr = new StringBuilder();
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (stderr)
                {
                    if (stderr.Length < 4000) stderr.AppendLine(e.Data);
                }
            };
            p.BeginErrorReadLine();
            new Thread(() => ReadLoop(p, stderr)) { IsBackground = true, Name = "codex-out" }.Start();
            try
            {
                p.StandardInput.Write(text);
                p.StandardInput.Close();
            }
            catch (Exception e)
            {
                Log.Error("codex stdin", e);
            }
            Log.Write($"codex: started pid {p.Id} model {Slug ?? "-"} effort {Reasoning} resume {SessionId ?? "-"}");
            return true;
        }
    }

    private void ReadLoop(Process p, StringBuilder stderr)
    {
        bool ended = false;
        string? error = null;
        try
        {
            string? line;
            while ((line = p.StandardOutput.ReadLine()) is not null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                try
                {
                    Handle(line, ref ended, ref error);
                }
                catch (Exception e)
                {
                    Log.Error("codex line", e);
                }
            }
            p.WaitForExit(3000);
        }
        catch (Exception e)
        {
            Log.Error("codex read", e);
        }
        lock (gate)
        {
            if (ReferenceEquals(proc, p)) proc = null;
        }
        WithdrawAll(); // the run is over: nothing it asked can still be answered
        if (!ended)
        {
            Busy = false;
            string tail;
            lock (stderr) tail = stderr.ToString().Trim();
            TurnEnded?.Invoke(false, stopped ? AiErrors.Stopped : error is { Length: > 0 } ? error : tail.Length > 0 ? Trim(tail) : AiErrors.CodexNoAnswer);
        }
        p.Dispose();
    }

    private static string Trim(string s) => s.Length > 600 ? s[^600..] : s;

    /// A TOML key: bare when it can be ("files-mcp"), else quoted.
    private static string TomlKey(string k) => k.Length > 0 && k.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? k : "\"" + k.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// A TOML string for a -c value: a literal one (backslashes stay) unless it holds a quote.
    private static string Toml(string s) => s.Contains('\'') ? "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : "'" + s + "'";

    /// The base instructions Codex reads instead of its own coding-agent ones; written before every
    /// turn so the local time in them stays current.
    private string Instructions()
    {
        string file = Path.Combine(cwd, $"chatgpt-{files.Replace('/', '-')}.md");
        try
        {
            File.WriteAllText(file, ClaudeSession.SystemPrompt(null,
                "You are ChatGPT, answering in a chat inside Bloom, a Windows launcher the user opens with the Win key. "
                + "This is a conversation, not a coding task: do not run commands and do not read or change files"
                + (pc is not null ? " (the bloom tool is the way to act on the PC). " + pc : ". ")
                + "Files the user attached come in the message: text files inside <attachment name=\"…\"> blocks, pictures as images. "
                + "When the user asks for a file, put its whole content in a fenced code block whose info string is file:<name> "
                + "(for example ```file:summary.md), one block per file; Bloom saves each as a file the user can open or save. ", pc is not null, Settings.Current.AiTitles));
        }
        catch (Exception e)
        {
            Log.Error("codex instructions", e);
        }
        return file;
    }

    /// Web-search citations arrive as private-use markers (U+E200 cite U+E202 turn0search0 U+E201)
    /// that only ChatGPT's own apps turn into links; here they would show as stray text.
    private static readonly System.Text.RegularExpressions.Regex Citations = new("[^]*[ \t]*");

    private static string Clean(string text) => Citations.Replace(text, "").TrimEnd();

    private static long Num(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.TryGetInt64(out long n) ? n : 0;

    private static string Str(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private void Handle(string line, ref bool ended, ref string? error)
    {
        using var doc = JsonDocument.Parse(line);
        var r = doc.RootElement;
        switch (Str(r, "type"))
        {
            case "thread.started":
                if (Str(r, "thread_id") is { Length: > 0 } id && id != SessionId)
                {
                    SessionId = id;
                    SessionStarted?.Invoke(id);
                }
                break;
            case "item.started":
                if (r.TryGetProperty("item", out var it))
                    Status?.Invoke(Str(it, "type") switch
                    {
                        "reasoning" => Tr("생각하는 중…", "Thinking…"),
                        "web_search" => Tr("웹 검색 중…", "Searching the web…"),
                        "command_execution" => Tr("파일 다루는 중…", "Working with files…"),
                        "mcp_tool_call" when Str(it, "server") == "bloom" => Tr("PC에서 작업하는 중…", "Working on the PC…"),
                        "mcp_tool_call" => Tr("도구 쓰는 중…", "Using a tool…"),
                        _ => null,
                    });
                break;
            case "item.completed":
                if (r.TryGetProperty("item", out var done) && Str(done, "type") == "agent_message" && Clean(Str(done, "text")) is { Length: > 0 } text)
                {
                    Status?.Invoke(null);
                    AssistantText?.Invoke(text);
                }
                break;
            case "turn.completed":
                ended = true;
                Busy = false;
                WithdrawAll();
                if (r.TryGetProperty("usage", out var u))
                    Log.Write($"codex turn: in {Num(u, "input_tokens")} · cached {Num(u, "cached_input_tokens")} · out {Num(u, "output_tokens")}");
                LastUsed = DateTime.UtcNow;
                TurnEnded?.Invoke(true, null);
                break;
            case "turn.failed":
                ended = true;
                Busy = false;
                WithdrawAll();
                string msg = "";
                if (r.TryGetProperty("error", out var err)) msg = Str(err, "message") is { Length: > 0 } m ? m : err.ToString();
                TurnEnded?.Invoke(false, msg.Length > 0 ? msg : error ?? Tr("Codex 오류", "Codex error"));
                break;
            case "error":
                error = Str(r, "message");
                break;
        }
    }

    public void Interrupt()
    {
        lock (gate)
        {
            WithdrawAll();
            if (proc is not { HasExited: false } p) return;
            stopped = true;
            try
            {
                p.Kill(true);
            }
            catch
            {
                /* already ending */
            }
        }
    }

    public void Dispose()
    {
        Alive = false;
        Interrupt();
        Exited?.Invoke("");
    }
}
