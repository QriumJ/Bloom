using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// Favorites folders: a tile in the favorites row that opens a list of the apps (and anything else
/// pinned) put in it — one set per kind of work.
internal sealed partial class Engine
{
    /// The folder's tile: its first four icons in a small grid (Data = the items, for FavStrip).
    private ResultItem FromGroup(Favorites.Entry g)
    {
        var kids = (g.Items ?? new()).Select(FromFavorite).Where(i => i is not null).Select(i => i!).ToList();
        var own = GroupIcon(g.Icon);
        var item = new ResultItem
        {
            Id = g.Id,
            Kind = ItemKind.Group,
            Title = g.Title,
            Subtitle = kids.Count == 0 ? Tr("비어 있어요", "Empty") : string.Join(", ", kids.Take(3).Select(k => k.Title)) + (kids.Count > 3 ? Tr($" 외 {kids.Count - 3}개", $" and {kids.Count - 3} more") : ""),
            Accessory = Tr($"폴더 · {kids.Count}개", $"Folder · {kids.Count}"),
            Icon = own ?? IconSpec.Of(G.Folder),
            Data = own is null ? kids : null, // FavStrip draws the small icons only when the folder has no icon of its own
            PrimaryLabel = Tr("폴더 열기", "Open folder"),
            StayOpen = true,
            Run = () => host.Window.OpenGroup(g.Id),
        };
        item.MoreActions = () => GroupActions(g, kids);
        return item;
    }

    private List<ActionItem> GroupActions(Favorites.Entry g, List<ResultItem> kids)
    {
        var list = new List<ActionItem>();
        list.Add(new ActionItem { Title = Tr("폴더 이름 바꾸기", "Rename folder"), Glyph = G.Settings, StayOpen = true, Run = () => host.Window.StartForm(GroupForm(g, null)) });
        list.Add(new ActionItem { Title = Tr("아이콘 바꾸기…", "Change icon…"), Glyph = G.Palette, StayOpen = true, Run = () => host.Window.PickGroupIcon(g.Id) });
        list.Add(new ActionItem { Title = Tr("아이콘 그림 고르기…", "Pick an icon picture…"), Glyph = G.Palette, StayOpen = true, Run = () => host.Window.PickPicture(g.Id, g.Title) });
        list.Add(new ActionItem { Title = Tr("앞으로 옮기기", "Move left"), Keys = "Ctrl+Shift+Left", Glyph = G.Back, StayOpen = true, Run = () => { Favorites.Move(g.Id, -1); host.Window.Refresh(true); } });
        list.Add(new ActionItem { Title = Tr("뒤로 옮기기", "Move right"), Keys = "Ctrl+Shift+Right", Glyph = G.NewWindow, StayOpen = true, Run = () => { Favorites.Move(g.Id, 1); host.Window.Refresh(true); } });
        list.Add(new ActionItem
        {
            Title = Tr("폴더 풀기 (안의 항목은 즐겨찾기 줄로)", "Ungroup (the items go back to the favorites row)"),
            Glyph = G.Folder,
            StayOpen = true,
            Run = () =>
            {
                Favorites.DeleteGroup(g.Id, true);
                host.Window.Go(Mode.Root);
            },
        });
        list.Add(new ActionItem
        {
            Title = Tr("폴더 지우기 (안의 항목도 즐겨찾기에서 빠져요)", "Delete folder (its items leave the favorites too)"),
            Glyph = G.Delete,
            Danger = true,
            StayOpen = true,
            Run = () => host.Window.Confirm(Tr($"‘{g.Title}’ 폴더와 안의 {kids.Count}개를 즐겨찾기에서 지울까요? (앱 자체는 그대로예요)", $"Remove the folder ‘{g.Title}’ and its {kids.Count} items from the favorites? (The apps themselves stay.)"), new ActionItem
            {
                Title = Tr("지우기", "Delete"),
                Keys = "Enter",
                Glyph = G.Delete,
                Danger = true,
                StayOpen = true,
                Run = () =>
                {
                    Favorites.DeleteGroup(g.Id, false);
                    host.Window.Go(Mode.Root);
                },
            }),
        });
        return list;
    }

    /// Icons a folder can wear (Segoe Fluent): kinds of work.
    public static readonly (char glyph, string name)[] GroupGlyphs =
    {
        ('\uE714', Tr("영상", "Video")), ('\uE722', Tr("카메라", "Camera")), ('\uEB9F', Tr("사진", "Photo")), ('\uE790', Tr("색", "Colour")), ('\uE70F', Tr("그리기", "Drawing")), ('\uE943', Tr("코드", "Code")),
        ('\uE7FC', Tr("게임", "Games")), ('\uE8D6', Tr("음악", "Music")), ('\uE720', Tr("마이크", "Microphone")), ('\uE8A5', Tr("문서", "Documents")), ('\uE821', Tr("작업", "Work")), ('\uE77B', Tr("사람", "People")),
        ('\uE774', Tr("웹", "Web")), ('\uE8BD', Tr("대화", "Chat")), ('\uE734', Tr("별", "Star")), ('\uEB51', Tr("하트", "Heart")),
    };

    /// A folder's own icon: "glyph:E714" → that glyph on a rose tile; a picture → its thumbnail; a program
    /// or .ico → its icon; null / missing file → null (the small icons of what is inside).
    public static IconSpec? GroupIcon(string? icon)
    {
        if (string.IsNullOrEmpty(icon)) return null;
        if (icon.StartsWith("glyph:", StringComparison.Ordinal) && int.TryParse(icon[6..], System.Globalization.NumberStyles.HexNumber, null, out int code))
            return IconSpec.Of((char)code, true);
        if (!System.IO.File.Exists(icon)) return null;
        return Search.Detail.IsPicture(icon) ? IconSpec.Thumb(icon) : IconSpec.Shell(icon);
    }

    /// The folder's list: its items with their own actions plus the folder's.
    public List<Section> GroupView(string groupId, string text)
    {
        var sections = new List<Section>();
        if (Favorites.Group(groupId) is not { } g)
        {
            var gone = new Section(null);
            gone.Items.Add(new ResultItem { Id = "group:gone", Kind = ItemKind.Info, Title = Tr("폴더가 없어졌어요", "The folder is gone"), Icon = IconSpec.Of(G.Info) });
            sections.Add(gone);
            return sections;
        }
        var q = new Query(text);
        var kids = new List<ResultItem>();
        foreach (var e in g.Items ?? new())
        {
            if (FromFavorite(e) is not { } item) continue;
            if (!q.IsEmpty && Matcher.Score(q, new MatchTarget(item.Title, item.Subtitle is null ? null : new[] { item.Subtitle })) <= 0) continue;
            var more = item.MoreActions;
            string id = e.Id;
            item.MoreActions = () =>
            {
                var list = more?.Invoke() ?? new List<ActionItem>();
                list.Add(new ActionItem { Title = Tr("위로", "Up"), Keys = "Ctrl+Shift+Up", Glyph = G.Up, StayOpen = true, Run = () => { Favorites.MoveInGroup(groupId, id, -1); host.Window.Refresh(true); } });
                list.Add(new ActionItem { Title = Tr("아래로", "Down"), Keys = "Ctrl+Shift+Down", Glyph = G.Down, StayOpen = true, Run = () => { Favorites.MoveInGroup(groupId, id, 1); host.Window.Refresh(true); } });
                list.Add(new ActionItem { Title = Tr("즐겨찾기 줄로 꺼내기", "Take out to the favorites row"), Glyph = G.Up, StayOpen = true, Run = () => { Favorites.TakeOut(groupId, id); host.Window.Refresh(false); } });
                list.Add(new ActionItem { Title = Tr("이 폴더에서 빼기", "Remove from this folder"), Keys = "Ctrl+Delete", Glyph = G.Delete, Danger = true, StayOpen = true, Run = () => { Favorites.Remove(id); host.Window.Refresh(false); } });
                return list;
            };
            kids.Add(item);
        }
        var s = new Section(g.Title, Tr($"{(g.Items?.Count ?? 0)}개 · 검색 결과에서 Ctrl+K → 폴더에 넣기", $"{(g.Items?.Count ?? 0)} · from search results: Ctrl+K → add to a folder"));
        foreach (var k in kids) s.Items.Add(k);
        if (kids.Count == 0)
            s.Items.Add(new ResultItem
            {
                Id = "group:empty",
                Kind = ItemKind.Info,
                Title = q.IsEmpty ? Tr("아직 비어 있어요", "Still empty") : Tr("찾은 항목이 없어요", "Nothing found"),
                Subtitle = Tr("앱을 검색하고 Ctrl+K → ‘즐겨찾기 폴더에 넣기’로 넣어요", "Search for an app, then Ctrl+K → ‘Add to a favorites folder’"),
                Icon = IconSpec.Of(G.Info),
                MoreActions = () => GroupActions(g, kids),
            });
        sections.Add(s);
        return sections;
    }

    /// Name a new folder (with `first` in it), or rename one.
    public FormSpec GroupForm(Favorites.Entry? existing, Favorites.Entry? first)
    {
        var f = new FormSpec { Title = existing is null ? Tr("새 즐겨찾기 폴더", "New favorites folder") : Tr("폴더 이름 바꾸기", "Rename folder"), Back = existing is null ? Mode.Root : Mode.Group };
        f.Fields.Add(new FormField
        {
            Label = Tr("이름", "Name"),
            Hint = Tr("폴더 이름 — 예) 영상 편집, 디자인", "Folder name — e.g. Video editing, Design"),
            Value = existing?.Title ?? "",
            Check = v => v.Trim().Length > 0 ? (v.Trim(), null) : (null, Tr("이름을 넣어 주세요", "Please enter a name")),
        });
        f.Done = form =>
        {
            if (existing is not null)
            {
                Favorites.RenameGroup(existing.Id, form[0]);
                host.Window.OpenGroup(existing.Id);
                return;
            }
            var g = Favorites.AddGroup(form[0]);
            if (first is not null) Favorites.AddToGroup(g.Id, first);
            host.Window.Go(Mode.Root);
            host.Window.ShowToast(Tr($"‘{g.Title}’ 폴더를 만들었어요", $"Made the folder ‘{g.Title}’"));
        };
        return f;
    }

    public ResultItem NewGroupItem() => new()
    {
        Id = "group:new",
        Kind = ItemKind.Command,
        Title = Tr("즐겨찾기 폴더 만들기", "New favorites folder"),
        Subtitle = Tr("작업별로 앱을 묶어 두는 폴더", "A folder to group apps by task"),
        Accessory = Tr("명령", "Command"),
        Icon = IconSpec.Of(G.Folder),
        PrimaryLabel = Tr("만들기", "Create"),
        StayOpen = true,
        Run = () => host.Window.StartForm(GroupForm(null, null)),
    };
}
