using System.Linq;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The chat view's side: sending, the model and the thinking level.
internal sealed partial class Launcher
{
    public void SendChat()
    {
        if (thread is null) return;
        string text = box.Text.Trim();
        string? image = attachedImage;
        if (text.Length == 0) text = ChatAttach.DefaultText(image is not null, attachedFiles.Count > 0);
        if (text.Length == 0 || thread.Busy) return;
        var page = attachment;
        var files = attachedFiles.ToList();
        attachment = null;
        attachedImage = null;
        attachedFiles.Clear();
        chat.ShowPending(attachedFiles, Unattach);
        suppressText = true;
        box.Clear();
        suppressText = false;
        host.Chats.Send(thread, text, page?.text is { } t ? ClipText(t, 15000) : null, page?.title, image, files);
        chat.Show(thread);
        UpdateChrome();
    }

    void IChatHost.ShowChat(ChatThread t) => ShowChat(t);

    /// Ctrl+Shift+O: this conversation in the AI chat window, with what was typed and attached so far.
    public void OpenInWindow()
    {
        var files = attachedFiles.ToList();
        string draft = box.Text;
        attachedFiles.Clear();
        chat.ShowPending(attachedFiles, Unattach);
        suppressText = true;
        box.Clear();
        suppressText = false;
        host.OpenChatWindow(thread ?? host.Chats.New(), draft, files);
    }

    /// A conversation was deleted (here or in the chat window): it isn't shown any longer.
    public void Forget(ChatThread t)
    {
        if (!ReferenceEquals(t, thread)) return;
        if (Mode == App.Mode.Chat) ShowChat(host.Chats.New());
        else thread = null;
    }

    /// Ctrl+L: the conversations as a list in the launcher.
    public void ShowThreads() => Go(App.Mode.Threads);

    private string CurrentModel => Ai.AiModels.Normalize(thread?.Model ?? Ai.AiModels.DefaultModel());

    /// Ctrl+M: every Claude and ChatGPT model in the action panel, the current one ticked.
    public void PickModel()
    {
        string cur = CurrentModel;
        var ids = Ai.AiModels.Claude.Concat(Ai.GptCatalog.All.Select(m => Ai.AiModels.Gpt(m.Slug))).ToList();
        if (!ids.Contains(cur)) ids.Add(cur);
        var list = ids.Select(m => new ActionItem
        {
            Title = Ai.AiModels.Who(m),
            Glyph = m == cur ? G.Check : G.Model,
            StayOpen = true,
            Run = () => SetModel(m),
        }).ToList();
        actions.Open(Tr("모델 고르기 · Claude 또는 ChatGPT", "Pick a model · Claude or ChatGPT"), list, ids.IndexOf(cur));
    }

    /// Ctrl+Shift+M: Claude ↔ ChatGPT, each side with the model last used there.
    public void SwitchProvider()
    {
        bool toGpt = !Ai.AiModels.IsChatGpt(CurrentModel);
        var s = Settings.Current;
        string next = s.AiModel;
        if (toGpt)
        {
            s.AiProvider = "chatgpt"; // DefaultModel() now answers for the ChatGPT side
            next = Ai.AiModels.DefaultModel();
        }
        SetModel(next);
    }

    /// The conversation goes on with this model from the next message; it also becomes the default.
    public void SetModel(string model)
    {
        Chats.SetModel(thread, model);
        string level = Ai.AiModels.HasEffort(model) ? " · " + Ai.AiModels.EffortName(thread is null ? Ai.AiModels.DefaultEffort(model) : Chats.EffortOf(thread)) : "";
        ShowToast(Ai.AiModels.Who(model) + level);
        UpdateChrome();
    }

    /// Ctrl+E: the model's levels in turn (Claude 빠름 = thinking off … 최대; ChatGPT 낮음 … 울트라).
    public void CycleEffort()
    {
        string model = CurrentModel;
        if (!Ai.AiModels.HasEffort(model))
        {
            ShowToast(Tr("Haiku는 생각 수준을 바꿀 수 없어요", "Haiku has no thinking levels"));
            return;
        }
        string now = thread is null ? Ai.AiModels.DefaultEffort(model) : Chats.EffortOf(thread);
        string next = Ai.AiModels.Next(Ai.AiModels.Efforts(model), now);
        Chats.SetEffort(thread, next);
        ShowToast(Tr("생각 수준: ", "Thinking level: ") + Ai.AiModels.EffortName(next));
        UpdateChrome();
    }
}
