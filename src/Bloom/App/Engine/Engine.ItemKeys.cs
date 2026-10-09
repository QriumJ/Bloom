using System.Collections.Generic;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// Ctrl+K › 단축키 지정: a global shortcut for one item (anything that can be a favorite), asked in the search line.
internal sealed partial class Engine
{
    public List<ActionItem> ItemKeyActions(ResultItem item)
    {
        var list = new List<ActionItem>();
        if (item.AsFavorite is not { } entry) return list;
        string? now = ItemKeys.For(item.Id);
        list.Add(new ActionItem
        {
            Title = now is null ? Tr("단축키 지정…", "Set a shortcut…") : Tr($"단축키 바꾸기… ({Hotkeys.Label(now)})", $"Change the shortcut… ({Hotkeys.Label(now)})"),
            Glyph = G.Keyboard,
            StayOpen = true,
            Run = () => host.Window.StartForm(ItemKeyForm(entry, now)),
        });
        if (now is not null)
            list.Add(new ActionItem
            {
                Title = Tr("단축키 지우기", "Remove the shortcut"),
                Glyph = G.Delete,
                StayOpen = true,
                Run = () =>
                {
                    ItemKeys.Set(entry, null);
                    host.ApplyHotkeys();
                    host.Window.ShowToast(Tr($"‘{Clip(entry.Title, 20)}’ 단축키를 지웠어요", $"Removed the shortcut of ‘{Clip(entry.Title, 20)}’"));
                },
            });
        return list;
    }

    private FormSpec ItemKeyForm(Favorites.Entry entry, string? now)
    {
        var f = new FormSpec { Title = Tr($"단축키 · {Clip(entry.Title, 24)}", $"Shortcut · {Clip(entry.Title, 24)}") };
        f.Fields.Add(new FormField
        {
            Label = Tr("단축키", "Shortcut"),
            Hint = Tr("쓸 키를 그대로 누르세요 — 예) Ctrl+Alt+M · 어디서든 누르면 바로 실행돼요", "Press the keys — e.g. Ctrl+Alt+M · runs it from anywhere"),
            Value = now is null ? "" : Hotkeys.Label(now),
            Keys = true,
            Check = v =>
            {
                if (Hotkeys.Normal(v) is not { } combo)
                    return (null, KeysHint);
                if (host.Shortcuts?.Owner(combo) is { } owner && owner != "item:" + entry.Id)
                    return (null, owner.StartsWith("item:", System.StringComparison.Ordinal)
                        ? Tr($"‘{ItemKeyTitle(owner)}’에 이미 지정한 단축키예요", $"Already the shortcut of ‘{ItemKeyTitle(owner)}’")
                        : Tr("Bloom의 다른 단축키가 쓰고 있어요 (설정에서 바꿀 수 있어요)", "One of Bloom's own shortcuts uses it (change it in Settings)"));
                return (combo, null);
            },
        });
        f.Done = form =>
        {
            string combo = form[0];
            ItemKeys.Set(entry, combo);
            host.ApplyHotkeys();
            host.Window.Go(Mode.Root);
            host.Window.ShowToast(host.Shortcuts?.Status("item:" + entry.Id) == "taken"
                ? Tr($"{Hotkeys.Label(combo)}는 다른 앱이 쓰고 있어요 — 다른 조합으로 바꿔 주세요", $"Another app uses {Hotkeys.Label(combo)} — pick another combination")
                : Tr($"{Hotkeys.Label(combo)} → ‘{Clip(entry.Title, 20)}’", $"{Hotkeys.Label(combo)} → ‘{Clip(entry.Title, 20)}’"));
        };
        return f;
    }

    /// What a shortcut field says when the keys can't be one.
    public static string KeysHint => Tr("Ctrl · Alt · Shift · Win 중 하나 이상을 누른 채 글자 · 숫자 · F1~F12 · Space를 눌러 주세요", "Hold Ctrl, Alt, Shift or Win and press a letter, digit, F1–F12 or Space");

    private static string ItemKeyTitle(string name) =>
        Settings.Current.ItemKeys.Find(k => "item:" + k.Item.Id == name) is { } k ? Clip(k.Item.Title, 20) : name;
}
