using System.Collections.Generic;
using System.Windows;
using Bloom.App;

namespace Bloom.Ui;

/// Files and pictures for the AI chat: Ctrl+O (or 더보기 > 파일 첨부), Ctrl+V of copied files or a copied picture, or
/// dropping files on the launcher. They wait under the conversation until the next message takes them along.
internal sealed partial class Launcher
{
    private readonly List<string> attachedFiles = new();

    public IReadOnlyList<string> AttachedFiles => attachedFiles;

    private void WireAttachments()
    {
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) Attach(paths);
            e.Handled = true;
        };
    }

    /// Files to send with the next message; outside a conversation the last one (or a new one) opens with them.
    public void Attach(IEnumerable<string> paths)
    {
        if (Mode != Mode.Chat) ShowChat(thread ?? host.Chats.New());
        int skipped = ChatAttach.Add(attachedFiles, paths);
        if (skipped > 0) ShowToast(ChatAttach.Skipped(skipped));
        AttachmentsChanged();
    }

    public void Unattach(string path)
    {
        attachedFiles.Remove(path);
        AttachmentsChanged();
    }

    private void AttachmentsChanged()
    {
        chat.ShowPending(attachedFiles, Unattach);
        UpdateChrome();
        FocusBox();
    }

    /// Ctrl+O: the Windows file picker, several at once.
    public void PickFiles()
    {
        if (ChatAttach.Pick(this) is { } files) Attach(files);
        else FocusBox();
    }

    /// Ctrl+V in a conversation: copied files, or a copied picture (saved as a PNG first); plain text pastes as usual.
    private bool PasteAttachment()
    {
        if (ChatAttach.FromClipboard() is not { } files) return false;
        Attach(files);
        return true;
    }
}
