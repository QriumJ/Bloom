namespace Bloom.Ai;

/// How the chat treats the AI's tool calls (Host hands it BloomTools): which ones run without asking, how a call reads on
/// the approval card, and what 이 대화에서 항상 허용 remembers. Each takes the tool's name and its input JSON.
internal interface IToolPolicy
{
    /// A call that only looks something up (Bloom's find_*): allowed without asking.
    bool Quiet(string tool, string input);

    /// How the call reads in the chat: "옮기기: a.pdf → D:\문서".
    string Says(string tool, string input);

    /// What 이 대화에서 항상 허용 remembers: one action ("pc:move"), else the tool's name.
    string Key(string tool, string input);
}
