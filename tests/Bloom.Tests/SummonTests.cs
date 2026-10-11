using System.Collections.Generic;
using Bloom.App;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.Tests;

/// The launcher's opening trace (Ui\Launcher\SummonTrace.cs) and the QA's flash check (App\Qa\SummonBench.cs).
internal static class SummonTests
{
    public static void TraceLine()
    {
        var r = new SummonTrace.Result(182.4, [("hop", 2.2), ("go", 41.0)], 8123, 0.4, true);
        Check.Eq(SummonTrace.Line(r), "182 ms (cold) · hop 2 · go 41 · faults 8123 · gc 0 ms", "cold opening");
        Check.Eq(SummonTrace.Line(r with { Cold = false, Steps = [] }), "182 ms · faults 8123 · gc 0 ms", "warm, no steps");
    }

    /// A home block's key (Launcher.Kept.cs) is what it shows: the same items give the same key, any word or icon that
    /// changes gives another (the block is drawn anew).
    public static void HomeBlockKey()
    {
        static Section Block(string subtitle, char glyph = 'a')
        {
            var s = new Section("명령") { HomeKey = "commands" };
            s.Items.Add(new ResultItem { Id = "cmd:x", Title = "X", Subtitle = subtitle, Accessory = "명령", Icon = IconSpec.Of(glyph) });
            return s;
        }
        string key = Launcher.SectionKey(Block("one"), out var widget);
        Check.True(widget is null, "no widget made for rows");
        Check.Eq(Launcher.SectionKey(Block("one"), out _), key, "the same block, the same key");
        Check.True(Launcher.SectionKey(Block("two"), out _) != key, "another subtitle");
        Check.True(Launcher.SectionKey(Block("one", 'b'), out _) != key, "another icon");
        var noted = Block("one");
        noted.Note = "1분 전";
        Check.True(Launcher.SectionKey(noted, out _) != key, "another note on the header");

        // a favorites folder's tile shows its items' icons: one of them drawn anew is another key
        static Section Folder(char glyph)
        {
            var s = new Section("즐겨찾기") { HomeKey = "favorites", Strip = true };
            List<ResultItem> kids = [new ResultItem { Id = "app:a", Title = "A", Icon = IconSpec.Of(glyph) }];
            s.Items.Add(new ResultItem { Id = "group:f", Kind = ItemKind.Group, Title = "F", Icon = IconSpec.Of('f'), Data = kids });
            return s;
        }
        Check.Eq(Launcher.SectionKey(Folder('a'), out _), Launcher.SectionKey(Folder('a'), out _), "the same folder");
        Check.True(Launcher.SectionKey(Folder('a'), out _) != Launcher.SectionKey(Folder('b'), out _), "a folder item's icon");
    }

    public static void FlashShare()
    {
        var a = new byte[4 * 4];
        var b = (byte[])a.Clone();
        Check.Eq(SummonBench.Changed(a, b), 0.0, "the same look");
        b[0] = 40; // one pixel of four, blue by 40
        Check.Eq(SummonBench.Changed(a, b), 25.0, "a moved pixel counts");
        b[0] = 20;
        Check.Eq(SummonBench.Changed(a, b), 0.0, "a small shift doesn't");
    }
}
