using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Bloom.Ai;

/// How a stdio MCP server's command line starts without a shell: most configs name `npx`, `uvx` or `node` bare, and
/// npx is a .cmd, which CreateProcess neither finds on PATH nor runs. So the command is looked up on PATH with
/// PATHEXT's extensions. A program (.exe, .com…) is started directly with its arguments quoted for the C runtime
/// (Quote) — no shell sees them. Only a .cmd or .bat needs cmd.exe (/d: no AutoRun; /v:off: no delayed expansion; /s:
/// the line as written here), and cmd reads a line by other rules than the C runtime: an argument is quoted for it
/// (BatchQuote), and one that cannot be written safely — it holds a quote, a % or a control character, or ends in a
/// backslash — is refused with McpException rather than let cmd.exe run part of it.
internal static class McpLaunch
{
    /// → the program to start and its argument line. Throws McpException for a batch file's argument that can't be passed
    /// safely.
    public static (string File, string Arguments) Resolve(string command, IReadOnlyList<string> args)
    {
        string file = Find(command) ?? command;
        string ext = Path.GetExtension(file);
        if (!ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            return (file, string.Join(" ", args.Select(Quote)));
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        string line = string.Join(" ", new[] { file }.Concat(args).Select(BatchQuote));
        return (cmd, "/d /v:off /s /c \"" + line + "\"");
    }

    /// The command line for CreateProcess: the program (quoted when it has a space) then its arguments.
    public static string CommandLine(string file, string arguments) => Quote(file) + (arguments.Length > 0 ? " " + arguments : "");

    /// An argument as cmd.exe passes it on to a batch file: bare when it holds only letters, digits and _ - . : / \ + @;
    /// else in double quotes, in which & | < > ^ ( ) are plain text. A quote can't be escaped for cmd, % is expanded even
    /// inside quotes, a line break ends the command, and a backslash before the closing quote would escape it for the
    /// program the batch file hands the argument on to — all refused.
    public static string BatchQuote(string arg)
    {
        if (arg.Any(c => c is '"' or '%' || char.IsControl(c)) || arg.EndsWith('\\'))
            throw new McpException("An argument of the server's command can't be passed safely to a batch file.");
        return arg.Length > 0 && arg.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':' or '/' or '\\' or '+' or '@') ? arg : "\"" + arg + "\"";
    }

    /// The command as a full path: as written when it has a folder or exists; else the first PATH folder holding it, as
    /// written or with one of PATHEXT's extensions. Null: not found (CreateProcess then tries it as written).
    private static string? Find(string command)
    {
        if (Path.IsPathRooted(command) || command.Contains('\\') || command.Contains('/')) return File.Exists(command) ? Path.GetFullPath(command) : null;
        string[] exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
        bool hasExt = Path.HasExtension(command);
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (hasExt && File.Exists(Path.Combine(dir, command))) return Path.Combine(dir, command);
                foreach (string ext in exts)
                    if (Path.Combine(dir, command + ext) is var full && File.Exists(full)) return full;
            }
            catch (ArgumentException)
            {
                /* a PATH entry with characters a path can't have */
            }
        }
        return null;
    }

    /// One argument as the C runtime reads it back (CommandLineToArgvW): quoted when empty or holding a space, a tab or a
    /// quote; inner quotes escaped, backslashes before them (and before the closing quote) doubled.
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        var sb = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            if (c == '"')
            {
                sb.Append('\\', slashes * 2 + 1).Append('"');
                slashes = 0;
                continue;
            }
            sb.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }
}
