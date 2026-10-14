using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// The tools an API chat offers and how each call runs: Bloom's own "pc" tool (IPcTools, the MCP server's handlers)
/// and file and command tools in the chat's folder. As in the CLI chats, look-ups go straight through — the pc tool's
/// find_* and reading or listing inside the chat's folder (really inside: RealPath) — and everything else goes by
/// 설정's 권한 (ToolPermissions): 사용 안 함 is refused here (and its tool not offered), the rest waits for the user's
/// yes on the approval card (`ask`: Chats.Asked, which lets 항상 허용 and 이 대화에서 항상 허용 through): reading
/// elsewhere, every write, edit and command, every pc action. No result carries a stored key or a hidden variable's
/// value (Secrets).
internal sealed class ApiToolbox
{
    public const string ReadFile = "read_file", ListFolder = "list_folder", WriteFile = "write_file", EditFile = "edit_file", RunCommand = "run_command";

    /// A file read gives at most this much text; a result at most MaxResult (both cut with a note).
    public const int MaxRead = 100_000, MaxResult = 40_000;

    private const int MaxEntries = 300;
    private const string Declined = "The user declined this. Don't try it again unless they ask.";
    private const string SwitchedOff = "The user switched this off in Bloom's settings. Don't try it again.";
    private const string Redacted = "***";

    private readonly string folder;
    private readonly IPcTools? pc;
    private readonly IReadOnlyDictionary<string, string>? rules;
    private readonly Func<ToolAsk, Task<bool>> ask;
    private readonly string[] secrets;
    private readonly string? pcName;

    /// `folder`: the chat's own (relative paths start there, commands run there); `pc`: Bloom's tool, when offered;
    /// `rules`: 설정's 권한 as the turn began (Settings.Permissions; null: no file or command tools at all); `secrets`:
    /// text no result may carry (Secrets()).
    public ApiToolbox(string folder, IPcTools? pc, IReadOnlyDictionary<string, string>? rules, Func<ToolAsk, Task<bool>> ask, IEnumerable<string>? secrets = null)
    {
        this.folder = Path.GetFullPath(folder);
        this.pc = pc;
        this.rules = rules;
        this.ask = ask;
        // the longest first, so one that holds another is taken out whole
        this.secrets = (secrets ?? []).Where(s => s.Length > 0).Distinct().OrderByDescending(s => s.Length).ToArray();
        var tools = new List<ApiTool>();
        if (pc is not null && PcTool(pc) is { } bloom)
        {
            tools.Add(bloom);
            pcName = bloom.Name;
        }
        // reading and listing stay (the chat's own folder needs no permission); an action switched off is not offered
        if (rules is not null) tools.AddRange(FileTools.Where(t => ActionOf(t.Name) is not { } action || ToolPermissions.Of(rules, action) != ToolPermissions.Off));
        Tools = tools;
    }

    public IReadOnlyList<ApiTool> Tools { get; }

    public bool Offers(string tool) => Tools.Any(t => t.Name == tool);

    /// The 권한 a file or command tool asks under (reading: only outside the chat's folder, so none here).
    private static string? ActionOf(string tool) => tool switch
    {
        WriteFile => ToolPermissions.Write,
        EditFile => ToolPermissions.Edit,
        RunCommand => ToolPermissions.Command,
        _ => null,
    };

    private string Rule(string action) => rules is null ? ToolPermissions.Off : ToolPermissions.Of(rules, action);

    /// What no tool result may carry, on a worker thread (it opens the stored keys): every API key kept in 설정 > AI 계정
    /// and the values ApiCommand keeps out of a command's environment.
    public static List<string> Secrets()
    {
        var list = ApiProviders.All.Select(p => ApiKeys.Get(p.Id)).OfType<string>().ToList();
        list.AddRange(ApiCommand.Environment().Hidden);
        return list;
    }

    private static readonly ApiTool[] FileTools =
    {
        new(ReadFile, "Read a text file on the user's PC. Relative paths start in your working folder.",
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),
        new(ListFolder, "List a folder on the user's PC (folders end with \\). Relative paths start in your working folder; \".\" is it.",
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),
        new(WriteFile, "Write a whole text file (UTF-8), made with its folders when missing, replacing one that is there. Waits for the user's OK.",
            """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}"""),
        new(EditFile, "Replace old_text, which must occur exactly once, with new_text in a text file. Waits for the user's OK.",
            """{"type":"object","properties":{"path":{"type":"string"},"old_text":{"type":"string"},"new_text":{"type":"string"}},"required":["path","old_text","new_text"]}"""),
        new(RunCommand, $"Run a Windows PowerShell command in your working folder; gives its output (at most {ApiCommand.MaxOutput} characters) and exit code; stopped after {(int)ApiCommand.Timeout.TotalSeconds} s, and whatever it starts ends with it. Waits for the user's OK.",
            """{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}"""),
    };

    /// The pc tool's MCP definition as a function (its name, description and input schema as they are).
    private static ApiTool? PcTool(IPcTools pc)
    {
        var d = JsonSerializer.SerializeToElement(pc.Definition);
        return d.TryGetProperty("inputSchema", out var schema)
            ? new ApiTool(ApiJson.Str(d, "name"), ApiJson.Str(d, "description"), schema.GetRawText())
            : null;
    }

    /// The chat's status line while a call runs.
    public static string Doing(ApiToolCall call) => call.Name switch
    {
        ReadFile => Tr("파일 읽는 중…", "Reading a file…"),
        ListFolder => Tr("폴더 보는 중…", "Looking in a folder…"),
        WriteFile or EditFile => Tr("파일 쓰는 중…", "Writing a file…"),
        RunCommand => Tr("명령 실행 중…", "Running a command…"),
        _ => Tr("PC에서 작업하는 중…", "Working on the PC…"),
    };

    /// One call: asked first when it must be, then run. → (its result for the model, failed). Cancelling `stop` ends a
    /// command and throws OperationCanceledException.
    public async Task<(string Text, bool Error)> RunAsync(ApiToolCall call, CancellationToken stop)
    {
        JsonElement a;
        try
        {
            using var doc = JsonDocument.Parse(call.Arguments.Length > 0 ? call.Arguments : "{}");
            a = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return ("The arguments were not valid JSON; call the tool again with a JSON object.", true);
        }
        if (a.ValueKind != JsonValueKind.Object) return ("The arguments must be a JSON object.", true);
        var (text, error) = await Run(call, a, stop).ConfigureAwait(false);
        foreach (string secret in secrets) text = text.Replace(secret, Redacted, StringComparison.Ordinal);
        // never an empty result (an empty file): some providers refuse a tool message with no content
        return (text.Length > MaxResult ? text[..MaxResult] + "\n… (cut)" : text.Length == 0 ? "(empty)" : text, error);
    }

    private async Task<(string, bool)> Run(ApiToolCall call, JsonElement a, CancellationToken stop)
    {
        if (pc is not null && call.Name == pcName)
        {
            // Chats words the card and the key from the pc tool's own policy (Says and Key left empty)
            if (!pc.Quiet(call.Name, call.Arguments) && !await Asked(new ToolAsk(call.Id, call.Name, call.Arguments, ""), stop).ConfigureAwait(false)) return (Declined, true);
            return await pc.Call(call.Name, a).ConfigureAwait(false);
        }
        if (rules is null || FileTools.All(t => t.Name != call.Name)) return ($"No tool named {call.Name}.", true);
        if (ActionOf(call.Name) is { } action && Rule(action) == ToolPermissions.Off) return (SwitchedOff, true);
        // the path as written: no %VARIABLE% is expanded, so neither the card nor an error text can show a variable's value
        string path = ApiJson.Str(a, "path").Trim().Trim('"');
        switch (call.Name)
        {
            case ReadFile or ListFolder:
                {
                    if (Resolve(path) is not { } full) return ("path must be a file or folder path.", true);
                    bool list = call.Name == ListFolder;
                    // really inside the chat's folder it is a look-up, as Claude Code's Read there; elsewhere 권한 decides
                    if (!RealPath.Within(folder, full))
                    {
                        if (Rule(ToolPermissions.ReadOutside) == ToolPermissions.Off) return (SwitchedOff, true);
                        string shown = ToolText.Visible(full);
                        var question = PathAsk(call, list ? Tr($"폴더 보기: {shown}", $"List a folder: {shown}") : Tr($"파일 읽기: {shown}", $"Read a file: {shown}"), full, ToolPermissions.ReadOutside);
                        if (!await Asked(question, stop).ConfigureAwait(false)) return (Declined, true);
                    }
                    return list ? List(full) : Read(full);
                }
            case WriteFile:
                {
                    if (Resolve(path) is not { } full) return ("path must be a file path.", true);
                    string content = ApiJson.Str(a, "content"), shown = ToolText.Visible(full);
                    if (!await Asked(PathAsk(call, Tr($"파일 쓰기: {shown}", $"Write a file: {shown}"), full, ToolPermissions.Write), stop).ConfigureAwait(false)) return (Declined, true);
                    return Write(full, content);
                }
            case EditFile:
                {
                    if (Resolve(path) is not { } full) return ("path must be a file path.", true);
                    string shown = ToolText.Visible(full);
                    if (!await Asked(PathAsk(call, Tr($"파일 고치기: {shown}", $"Edit a file: {shown}"), full, ToolPermissions.Edit), stop).ConfigureAwait(false)) return (Declined, true);
                    return Edit(full, ApiJson.Str(a, "old_text"), ApiJson.Str(a, "new_text"));
                }
            default: // RunCommand
                {
                    string command = ApiJson.Str(a, "command").Trim();
                    if (command.Length == 0) return ("command must not be empty.", true);
                    string line = ToolText.Line(command);
                    // the card shows the whole command (Detail); the ✓ line under the answer its first 80 characters
                    var question = new ToolAsk(call.Id, call.Name, call.Arguments, Tr($"명령 실행: {line}", $"Run a command: {line}"), ToolPermissions.Command) { Detail = ToolText.Visible(command) };
                    if (!await Asked(question, stop).ConfigureAwait(false)) return (Declined, true);
                    Directory.CreateDirectory(folder);
                    return await ApiCommand.RunAsync(command, folder, stop).ConfigureAwait(false);
                }
        }
    }

    /// A question about a path: the card shows it whole, and where it really leads when a link takes it elsewhere.
    private static ToolAsk PathAsk(ApiToolCall call, string says, string full, string key)
    {
        string detail = ToolText.Visible(full);
        if (RealPath.Of(full) is { } real && !real.Equals(full, StringComparison.OrdinalIgnoreCase))
        {
            string really = ToolText.Visible(real);
            detail += "\n" + Tr($"실제 위치: {really}", $"Really at: {really}");
        }
        return new ToolAsk(call.Id, call.Name, call.Arguments, says, key) { Detail = detail };
    }

    /// The user's answer; a stopped turn answers no (and then throws, ending the loop).
    private async Task<bool> Asked(ToolAsk question, CancellationToken stop)
    {
        bool allowed = await ask(question).ConfigureAwait(false);
        stop.ThrowIfCancellationRequested();
        return allowed;
    }

    private string? Resolve(string path)
    {
        if (path.Length == 0) return null;
        try
        {
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(folder, path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static (string, bool) Read(string full)
    {
        try
        {
            if (Directory.Exists(full)) return ($"{full} is a folder; use {ListFolder}.", true);
            if (!File.Exists(full)) return ($"There is no file at {full}.", true);
            using var stream = File.OpenRead(full);
            var bytes = new byte[Math.Min(stream.Length, MaxRead * 4L)];
            int n = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (Array.IndexOf(bytes, (byte)0, 0, n) >= 0) return ($"{full} is not a text file.", true);
            string text = Encoding.UTF8.GetString(bytes, 0, n);
            return text.Length > MaxRead || stream.Length > n ? (text[..Math.Min(text.Length, MaxRead)] + "\n… (the rest of the file is not shown)", false) : (text, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ($"Could not read {full}: {e.Message}", true);
        }
    }

    private static (string, bool) List(string full)
    {
        try
        {
            if (!Directory.Exists(full)) return ($"There is no folder at {full}.", true);
            var dir = new DirectoryInfo(full);
            var entries = dir.EnumerateFileSystemInfos().Take(MaxEntries + 1).ToList();
            var sb = new StringBuilder();
            foreach (var e in entries.Take(MaxEntries))
                sb.Append(e is DirectoryInfo ? e.Name + "\\" : $"{e.Name} ({((FileInfo)e).Length:N0} bytes)").Append('\n');
            if (entries.Count > MaxEntries) sb.Append($"… (more than {MaxEntries} entries)");
            return (sb.Length == 0 ? "(empty)" : sb.ToString().TrimEnd(), false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ($"Could not list {full}: {e.Message}", true);
        }
    }

    private static (string, bool) Write(string full, string content)
    {
        try
        {
            if (Path.GetDirectoryName(full) is { } dir) Directory.CreateDirectory(dir);
            File.WriteAllText(full, content, new UTF8Encoding(false));
            return ($"Wrote {full} ({content.Length:N0} characters).", false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ($"Could not write {full}: {e.Message}", true);
        }
    }

    private static (string, bool) Edit(string full, string oldText, string newText)
    {
        try
        {
            if (!File.Exists(full)) return ($"There is no file at {full}.", true);
            if (oldText.Length == 0) return ("old_text must not be empty.", true);
            string text = File.ReadAllText(full);
            int at = text.IndexOf(oldText, StringComparison.Ordinal);
            if (at < 0) return ("old_text is not in the file; read it again and copy the text exactly.", true);
            if (text.IndexOf(oldText, at + oldText.Length, StringComparison.Ordinal) >= 0) return ("old_text occurs more than once; include more of the text around it.", true);
            File.WriteAllText(full, text[..at] + newText + text[(at + oldText.Length)..], new UTF8Encoding(false));
            return ($"Edited {full}.", false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ($"Could not edit {full}: {e.Message}", true);
        }
    }
}
