using Bloom.Ai;

namespace Bloom.Ui;

/// What a ChatView asks of the window it sits in: the launcher (quick chat under the search line) or the AI chat window.
internal interface IChatHost
{
    /// A Bloom Lens picture waiting to go with the first message (shown in the empty conversation).
    string? AttachedImage { get; }

    void SendChat();

    void PickFiles();

    void ShowChat(ChatThread t);

    /// The list of conversations (the launcher's view, the window's side list).
    void ShowThreads();

    void PickModel();

    void SwitchProvider();

    void CycleEffort();

    void ShowToast(string text);
}
