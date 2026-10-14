using System.Text.Json;
using System.Threading.Tasks;

namespace Bloom.Ai;

/// Bloom's own "pc" tool as the API chats offer it (Host's BloomTools, the same handlers the MCP server runs): its
/// definition and its calls. The chat asks the user before an action itself; Call then only runs it.
internal interface IPcTools : IToolPolicy
{
    /// The tool's MCP definition: { name, description, inputSchema }.
    object Definition { get; }

    Task<(string text, bool error)> Call(string tool, JsonElement args, string? conversation = null);
}
