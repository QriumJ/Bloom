using System.Collections.Generic;

namespace Bloom.Core;

/// One global shortcut the user gave to one item (Ctrl+K › 단축키 지정): the combo and the item, kept as a favorite
/// entry so it runs the same way a pinned one does (an app, a folder, a file, a site, a command, a window layout …).
internal sealed class ItemKey
{
    public string Combo { get; set; } = ""; // "ctrl+alt+m" (App\Hotkeys.cs Parse)
    public Favorites.Entry Item { get; set; } = new();
}

/// The shortcuts given to single items, in Settings.ItemKeys (they move with 설정 내보내기). Registered by Host next to
/// Bloom's own shortcuts, which win a clash.
internal static class ItemKeys
{
    public static IReadOnlyList<ItemKey> All => Settings.Current.ItemKeys;

    public static string? For(string id) => Settings.Current.ItemKeys.Find(k => k.Item.Id == id)?.Combo;

    /// `combo` null or empty takes the shortcut off.
    public static void Set(Favorites.Entry item, string? combo)
    {
        var list = Settings.Current.ItemKeys;
        list.RemoveAll(k => k.Item.Id == item.Id);
        if (!string.IsNullOrEmpty(combo))
            list.Add(new ItemKey
            {
                Combo = combo,
                Item = new Favorites.Entry { Id = item.Id, Kind = item.Kind, Title = item.Title, Subtitle = item.Subtitle, Target = item.Target },
            });
        Settings.Save();
    }
}
