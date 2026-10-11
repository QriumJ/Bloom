using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// What can be done to a result: its actions panel, confirmations, pickers, home editing, favorites.
internal sealed partial class Launcher
{
    public void RunAction(ActionItem a)
    {
        actions.Close();
        if (!a.StayOpen) Dismiss();
        try
        {
            a.Run();
        }
        catch (Exception e)
        {
            Log.Error("action " + a.Title, e);
        }
        if (a.StayOpen && IsVisible && !actions.IsOpen) FocusBox();
    }

    /// A yes/no question in the action panel: Enter = the action, Esc = cancel.
    public void Confirm(string question, ActionItem yes) =>
        actions.Open(question, new List<ActionItem> { yes, new() { Title = Tr("취소", "Cancel"), Keys = "Esc", Glyph = G.Close, StayOpen = true, Run = () => { } } });

    public List<ActionItem> ActionsFor(ResultItem item)
    {
        var list = new List<ActionItem>();
        if (item.Run is not null && item.PrimaryLabel.Length > 0)
            list.Add(new ActionItem { Title = item.PrimaryLabel, Keys = "Enter", Glyph = item.Kind == ItemKind.Calc ? G.Copy : G.NewWindow, Run = item.Run, StayOpen = item.StayOpen });
        if (item.MoreActions?.Invoke() is { } more) list.AddRange(more);
        if (item.Pinnable)
        {
            bool fav = Favorites.Has(item.Id);
            list.Add(new ActionItem
            {
                Title = fav ? Tr("즐겨찾기에서 빼기", "Remove from favorites") : Tr("즐겨찾기에 추가", "Add to favorites"),
                Keys = "Ctrl+D",
                Glyph = fav ? G.Unstar : G.Star,
                StayOpen = true,
                Run = () => ToggleFavorite(item),
            });
            if (fav && Mode == Mode.Root && box.Text.Length == 0)
            {
                list.Add(new ActionItem { Title = Tr("앞으로 옮기기", "Move left"), Keys = "Ctrl+Shift+Left", Glyph = G.Back, StayOpen = true, Run = () => MoveFavorite(item, -1) });
                list.Add(new ActionItem { Title = Tr("뒤로 옮기기", "Move right"), Keys = "Ctrl+Shift+Right", Glyph = G.NewWindow, StayOpen = true, Run = () => MoveFavorite(item, 1) });
            }
            if (fav)
            {
                list.Add(new ActionItem { Title = Tr("아이콘 그림 고르기…", "Pick an icon picture…"), Glyph = G.Palette, StayOpen = true, Run = () => PickPicture(item.Id, item.Title) });
                list.Add(new ActionItem { Title = Tr("AI로 아이콘 그리기…", "Draw an icon with AI…"), Glyph = G.Sparkle, StayOpen = true, Run = () => StartForm(Engine.AiIconForm(item.Id, item.Title)) });
                if (Favorites.Find(item.Id)?.Mono is not null)
                    list.Add(new ActionItem { Title = Tr("원래 아이콘으로", "Back to the original icon"), Glyph = G.Refresh, StayOpen = true, Run = () => { Favorites.SetMono(item.Id, null); Refresh(true); } });
            }
            list.Add(new ActionItem { Title = Tr("즐겨찾기 폴더에 넣기…", "Add to a favorites folder…"), Glyph = G.Folder, StayOpen = true, Run = () => PickGroup(item) });
            list.AddRange(Engine.ItemKeyActions(item));
            if (Home.ForCommand(item.Id) is { } widget)
            {
                bool on = Home.IsOn(widget);
                list.Add(new ActionItem { Title = on ? Tr($"홈에서 빼기 ({Home.Name(widget)})", $"Remove from home ({Home.Name(widget)})") : Tr($"홈에 고정 ({Home.Name(widget)})", $"Pin to home ({Home.Name(widget)})"), Glyph = on ? G.Unstar : G.Star, StayOpen = true, Run = () => { Home.Set(widget, !on); ShowToast(on ? Tr($"홈에서 ‘{Home.Name(widget)}’ 제거 완료", $"Removed ‘{Home.Name(widget)}’ from home") : Tr($"홈에 ‘{Home.Name(widget)}’ 고정 완료", $"Pinned ‘{Home.Name(widget)}’ to home")); Refresh(true); } });
            }
            if (Usage.Current.Items.ContainsKey(item.Id) && Mode == Mode.Root && box.Text.Length == 0 && !fav)
                list.Add(new ActionItem { Title = Tr("최근 사용에서 지우기", "Remove from recent"), Glyph = G.Delete, StayOpen = true, Run = () => { Usage.Forget(item.Id); Refresh(false); } });
        }
        if (Mode == Mode.Root && box.Text.Length == 0)
            list.Add(new ActionItem { Title = Tr("홈 화면 꾸미기…", "Customize the home…"), Glyph = G.Settings, Run = () => host.OpenSettings("home") });
        return list;
    }

    /// The symbols a favorite (or folder) can wear on its black tile; ticked = the one it has.
    public void PickPicture(string id, string title)
    {
        string? now = Favorites.Find(id)?.Mono;
        var list = Icons.Pictures.Select(p => new ActionItem
        {
            Title = p.name,
            Glyph = p.value == now ? G.Check : p.value == "pic:sparkle" ? G.Sparkle : (char)int.Parse(p.value.AsSpan(4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture),
            StayOpen = true,
            Run = () =>
            {
                Favorites.SetMono(id, p.value);
                Refresh(true);
            },
        }).ToList();
        list.Insert(0, new ActionItem { Title = Tr("AI로 그리기… (원하는 그림을 말로)", "Draw with AI… (describe the picture)"), Glyph = G.Sparkle, StayOpen = true, Run = () => StartForm(Engine.AiIconForm(id, title)) });
        if (now is not null) list.Add(new ActionItem { Title = Tr("원래 아이콘으로", "Back to the original icon"), Glyph = G.Refresh, StayOpen = true, Run = () => { Favorites.SetMono(id, null); Refresh(true); } });
        actions.Open(Tr($"‘{ClipText(title, 16)}’ 그림 아이콘", $"Picture icon for ‘{ClipText(title, 16)}’"), list, Math.Max(0, Array.FindIndex(Icons.Pictures, p => p.value == now) + 1));
    }

    public static bool ShowHomeToolsForQa;

    /// 홈 편집: the blocks show ▲ ▼ 숨기기, the AI usage cards ‹ › ✎, and the hidden blocks come back as rows at the end.
    public bool HomeEditing { get; private set; }

    public void ToggleHomeEdit()
    {
        HomeEditing = !HomeEditing;
        if (HomeEditing && (Mode != Mode.Root || box.Text.Length > 0)) Go(Mode.Root);
        else Refresh(true);
        if (HomeEditing) ShowToast(Tr("홈 편집 · 블록은 ▲ ▼ ✕ · AI 사용량 카드는 ‹ › 순서, ✎ 이름 · 끝내려면 Esc 또는 ‘편집 끝’", "Editing home · ▲ ▼ ✕ on blocks · ‹ › order and ✎ names the AI usage cards · Esc or ‘Done’ to finish"));
    }

    /// AI draws a favorite's icon (one Claude call); the toast says when it starts and how it ended.
    public async void DrawIcon(string id, string name, string? idea)
    {
        ShowToast(Tr($"‘{ClipText(name, 14)}’ 아이콘 생성 중… (사용량 1회)", $"Creating icon for ‘{ClipText(name, 14)}’… (1 use)"));
        var (icon, error) = await Ai.IconMaker.Draw(name, idea, System.Threading.CancellationToken.None);
        if (icon is null)
        {
            ShowToast(error ?? Tr("아이콘 생성 실패 · 다시 시도하세요", "Icon creation failed. Try again."));
            return;
        }
        Favorites.SetMono(id, icon);
        if (IsVisible && Mode == Mode.Root) Refresh(true);
        ShowToast(Tr($"‘{ClipText(name, 14)}’ 아이콘 변경 완료 · Ctrl+K로 다시 그리기", $"Icon changed for ‘{ClipText(name, 14)}’ · Ctrl+K to draw again"));
    }

    /// ▲ ▼ 숨기기 on a home block (top right, over its header): order and hide right on the home.
    private FrameworkElement HomeTools(string key)
    {
        var (first, last) = Home.Ends();
        void Move(int by)
        {
            Home.MoveShown(key, by);
            Refresh(true);
        }
        void Hide()
        {
            Home.Set(key, false);
            ShowToast(Tr($"‘{Home.Name(key)}’ 숨김 · 홈 편집에서 다시 표시", $"‘{Home.Name(key)}’ hidden · restore in Edit home"));
            Refresh(true);
        }
        var bar = HomeViews.EditBar((G.Up, Tr("위로", "Up"), key != first, () => Move(-1)), (G.Down, Tr("아래로", "Down"), key != last, () => Move(1)), (G.Close, Tr("홈에서 숨기기", "Hide from home"), true, Hide));
        bar.Margin = new Thickness(0, 6, 16, 0);
        return bar;
    }

    /// Which folder an item goes into (or a new one).
    private void PickGroup(ResultItem item)
    {
        if (item.AsFavorite is not { } entry) return;
        var groups = Favorites.Current.Items.Where(g => g.Kind == Favorites.GroupKind).ToList();
        string? now = Favorites.GroupOf(item.Id)?.Id;
        var list = groups.Select(g => new ActionItem
        {
            Title = g.Title + (g.Id == now ? Tr("  (지금 여기)", "  (here now)") : Tr($"  · {g.Items?.Count ?? 0}개", $"  · {g.Items?.Count ?? 0}")),
            Glyph = g.Id == now ? G.Check : G.Folder,
            StayOpen = true,
            Run = () =>
            {
                Favorites.AddToGroup(g.Id, entry);
                ShowToast(Tr($"‘{g.Title}’에 추가 완료", $"Added to ‘{g.Title}’"));
                Refresh(true);
            },
        }).ToList();
        list.Add(new ActionItem { Title = Tr("새 폴더 만들기…", "New folder…"), Glyph = G.Add, StayOpen = true, Run = () => StartForm(Engine.GroupForm(null, entry)) });
        actions.Open(Tr($"즐겨찾기 폴더에 넣기 · {ClipText(item.Title, 20)}", $"Add to a favorites folder · {ClipText(item.Title, 20)}"), list);
    }

    private void ToggleFavorite(ResultItem item)
    {
        if (item.AsFavorite is not { } e) return;
        if (Favorites.Has(item.Id))
        {
            Favorites.Remove(item.Id);
            ShowToast(Tr("즐겨찾기에서 제거 완료", "Removed from favorites"));
        }
        else
        {
            Favorites.Add(e);
            ShowToast(Tr("즐겨찾기 추가 완료", "Added to favorites"));
        }
        Refresh(true);
    }

    private void MoveFavorite(ResultItem item, int delta)
    {
        if (Mode == Mode.Group && groupId is not null) Favorites.MoveInGroup(groupId, item.Id, delta);
        else if (Favorites.Current.Items.Exists(e => e.Id == item.Id)) Favorites.Move(item.Id, delta); // the row: items and folders
        else return;
        Refresh(true);
    }

    public void OpenActions()
    {
        if (Mode == Mode.Chat)
        {
            var items = chat.Actions(thread);
            items.Insert(Math.Min(4, items.Count), new ActionItem { Title = Tr("AI 채팅 창에서 열기", "Open in the chat window"), Keys = "Ctrl+Shift+O", Glyph = G.NewWindow, Run = OpenInWindow });
            actions.Open(Tr("AI 채팅", "AI chat"), items);
            return;
        }
        if (Selected is not { } item) return;
        var list = ActionsFor(item);
        if (list.Count == 0) return;
        actions.Open(item.Title, list, 0, SelectedBox());
    }

    public void OpenActions(ResultItem item)
    {
        var list = ActionsFor(item);
        if (list.Count > 0) actions.Open(item.Title, list);
    }

    /// The selected row's box in the body (the 더보기 menu opens on it); null when it isn't a list row.
    private Rect? SelectedBox()
    {
        if (rows.Find(r => r.Selected) is not { IsVisible: true } row || row.ActualHeight <= 0) return null;
        try
        {
            return row.TransformToAncestor(body).TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void ActionsClosed() => FocusBox();

    public void CloseActionsForQa() => actions.Close();
}
