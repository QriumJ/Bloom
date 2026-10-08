using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Native;

namespace Bloom.App;

/// Where AI 빠른 동작 stands: the text it works on, the action running or done, its answer.
internal sealed class QuickState
{
    public string Source = "";
    public bool FromSelection; // taken from a selection (the answer can replace it) or from the clipboard
    public IntPtr Target; // the window the text came from: where 바꿔 넣기 / 붙여넣기 goes
    public string? Action; // the QuickActions key, or "custom"
    public string? Custom; // the user's own words for "custom"
    public string? Result, Error;
    public bool Busy;
    public CancellationTokenSource? Cts;

    public string ActionTitle => Action == "custom" ? Custom ?? "" : QuickActions.Find(Action ?? "") is { } a ? QuickActions.Title(a) : "";
}

/// AI 빠른 동작: the hotkey copies the selection of the window in front (or takes what is on the clipboard),
/// the launcher lists the actions, the answer can go back over the selection.
internal sealed partial class Host
{
    public QuickState Quick { get; } = new();

    /// From the hotkey (`selection`: Ctrl+C into the window in front first) or the 명령 (the clipboard as it is).
    public async void StartQuick(bool selection)
    {
        Quick.Cts?.Cancel();
        IntPtr target = selection ? Win32.GetForegroundWindow() : Clipboard.LastForeground;
        _ = Win32.GetWindowThreadProcessId(target, out uint pid);
        if (pid == Environment.ProcessId) target = Clipboard.LastForeground; // Bloom itself was in front
        string? text = null;
        bool fromSelection = false;
        if (selection && target != IntPtr.Zero)
        {
            text = await CopySelection(target);
            fromSelection = text is not null;
        }
        text ??= ClipboardText();
        Quick.Source = text?.Trim() ?? "";
        Quick.FromSelection = fromSelection;
        Quick.Target = target;
        Quick.Action = Quick.Custom = Quick.Result = Quick.Error = null;
        Quick.Busy = false;
        OpenView(Mode.Quick);
    }

    /// Ctrl+C into `target` once the hotkey's own keys are up; the copy, or null when nothing was selected
    /// (the clipboard did not change within half a second).
    private static async Task<string?> CopySelection(IntPtr target)
    {
        for (int i = 0; i < 40 && Win32.ModifiersDown(); i++) await Task.Delay(20);
        uint before = GetClipboardSequenceNumber();
        Win32.SetForegroundWindow(target);
        Win32.SendChord(0x11, 0x43); // Ctrl+C
        for (int i = 0; i < 25; i++)
        {
            await Task.Delay(20);
            if (GetClipboardSequenceNumber() != before)
            {
                await Task.Delay(30); // the app may put several formats one after another
                return ClipboardText() is { Length: > 0 } t ? t : null;
            }
        }
        return null;
    }

    private static string? ClipboardText()
    {
        for (int i = 0; i < 5; i++)
            try
            {
                return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(30); // another app holds the clipboard for a moment
            }
        return null;
    }

    /// Runs an action (or the user's own words) on the text; the launcher shows it working, then the answer.
    public async void RunQuick(string action, string? custom = null)
    {
        if (Quick.Source.Length == 0) return;
        Quick.Cts?.Cancel();
        var cts = Quick.Cts = new CancellationTokenSource();
        Quick.Action = action;
        Quick.Custom = custom;
        Quick.Result = Quick.Error = null;
        Quick.Busy = true;
        Later();
        string provider = Settings.Current.TranslateProvider == "chatgpt" ? "chatgpt" : "claude";
        var (text, error) = await OneShot.Ask(QuickActions.System(action, custom, Quick.Source), Quick.Source, provider, Translator.Model(provider), Translator.Effort(provider), "quick", cts.Token);
        if (cts.IsCancellationRequested) return;
        Quick.Busy = false;
        Quick.Result = text;
        Quick.Error = text is null ? error ?? Tr("답을 받지 못했어요", "No answer came back") : null;
        Log.Write($"quick: {action} → {(text is null ? "error" : text.Length + " chars")}");
        // the answer comes back in front even if the launcher was closed while it worked
        if (!Window.IsVisible || Window.Mode != Mode.Quick) OpenView(Mode.Quick);
        else Later();
    }

    public void CancelQuick()
    {
        Quick.Cts?.Cancel();
        Quick.Busy = false;
        Quick.Action = null;
        Later();
    }

    /// The answer over the selection it came from (or into that window when it came from the clipboard).
    public void PasteQuick()
    {
        if (Quick.Result is not { } text) return;
        Window.Dismiss();
        _ = Clipboard.PasteText(text, Quick.Target);
    }

    /// The text and the answer go on in an AI chat.
    public void QuickToChat()
    {
        string intro = Quick.Result is { } r
            ? Tr($"아래 글에 ‘{Quick.ActionTitle}’를 했고, 결과는 이랬어요. 이어서 이야기해요.\n\n[원문]\n{Quick.Source}\n\n[결과]\n{r}", $"I ran ‘{Quick.ActionTitle}’ on the text below; this was the result. Let's continue.\n\n[Text]\n{Quick.Source}\n\n[Result]\n{r}")
            : Quick.Source;
        Window.ShowChat(Chats.New(), intro);
    }

    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
