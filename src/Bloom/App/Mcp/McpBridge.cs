using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bloom.App.Mcp;

/// `Bloom.exe --mcp <pipe> [<conversation>]`: what Claude Code or Codex starts as the "bloom" MCP server. It only
/// carries the stdio lines to the running Bloom over a named pipe that only this Windows user can open (McpServer
/// answers them) and back — no window, no WPF, no data folder. It ends when either side closes. A conversation id
/// (Codex's runs) goes first as a line of its own, so Bloom knows whose chat to ask before an action.
internal static class McpBridge
{
    public static int Run(string pipe, string? conversation)
    {
        try
        {
            using var link = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            link.Connect(3000);
            if (conversation is not null)
            {
                byte[] hello = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { bloom_conversation = conversation }) + "\n");
                link.Write(hello, 0, hello.Length);
            }
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();
            var up = input.CopyToAsync(link);
            var down = link.CopyToAsync(output);
            Task.WaitAny(up, down);
            return 0;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // Bloom is not running (or is another user's): the CLI sees the server fail and goes on without it
            Console.Error.WriteLine("Bloom is not running: " + e.Message);
            return 1;
        }
    }
}
