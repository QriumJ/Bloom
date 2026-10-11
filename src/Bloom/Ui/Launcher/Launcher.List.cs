using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The result list: refresh, render, selection (with the favorites row), running items.
internal sealed partial class Launcher
{
    // ── list ────────────────────────────────────────────────────────────────────────────────────
    public ResultItem? Selected => inStrip && strip is { } st && stripSel < st.Items.Count ? st.Items[stripSel] : sel >= 0 && sel < rows.Count ? rows[sel].Item : null;

    private void OnText()
    {
        placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (suppressText) return;
        if (Mode is Mode.Chat)
        {
            UpdateChrome();
            return;
        }
        Refresh(false);
    }

    public void Refresh(bool keep)
    {
        int v = ++version;
        string text = box.Text;
        string? keepId = keep ? Selected?.Id : null;
        switch (Mode)
        {
            case Mode.Root:
                sections = Engine.Root(text);
                Render(sections, keepId);
                debounce.Stop();
                if (Settings.Current.FilesInRoot && text.Trim().Length >= 2) debounce.Start();
                break;
            case Mode.Form:
                if (form is not { } f) break;
                sections = Engine.FormView(f, text);
                Render(sections, "form:" + f.Step);
                break;
            case Mode.Files:
                debounce.Stop();
                debounce.Start();
                work.Working = true;
                break;
            default:
                var spec = View(Mode);
                if (spec.Build is null) break;
                sections = spec.Build(text);
                Render(sections, keepId, keepScroll: keep && spec.KeepScroll);
                break;
        }
        _ = v;
    }

    private bool slowBusy, slowAgain; // an Everything question is out; the text changed meanwhile (debounce)

    /// The slow half of a refresh: Everything, one question at a time; the text typed meanwhile goes next.
    private async Task Slow()
    {
        slowBusy = true;
        try
        {
            await SlowOnce();
        }
        finally
        {
            slowBusy = false;
        }
        if (!slowAgain) return;
        slowAgain = false;
        await Slow();
    }

    private async Task SlowOnce()
    {
        int v = version;
        string text = box.Text;
        if (Mode == Mode.Root)
        {
            void Insert(Section s)
            {
                int at = sections.FindIndex(x => x.Fallback);
                if (at < 0) at = sections.Count;
                sections.Insert(at, s);
                Render(sections, Selected?.Id, keepScroll: true);
            }
            var files = await Engine.RootFiles(text);
            if (v != version || Mode != Mode.Root) return;
            if (files is not null) Insert(files);
        }
        else if (Mode == Mode.Files)
        {
            work.Working = true;
            var result = await Engine.Files(text, fileKind);
            if (v != version || Mode != Mode.Files) return;
            work.Working = false;
            sections = result;
            Render(sections, null);
        }
    }

    /// Rows built at once; a longer list gets the rest right after, at Background priority, so it shows without a wait
    /// (a row costs 1–2 ms: the emoji view's 88 took ~200 ms in one go). The kept row is always among the first ones.
    /// While the launcher opens, only what a screen holds comes first (ScreenRows) and the rest waits for it to be on
    /// screen (afterReveal): built first, they held the reveal back by their own time and a second layout pass.
    private const int FirstRows = 24;

    /// The rows the list shows at once (its height as last laid out, over a row's 42 px), one more for a part shown:
    /// the home's 명령 alone, ~20 rows below the fold, took 30 ms of the opening's first frame (measured 10-12).
    private int ScreenRows() => scroll.ActualHeight > 0 ? Math.Min(FirstRows, (int)(scroll.ActualHeight / 42) + 1) : FirstRows;

    private int renderVersion;

    private void Render(List<Section> secs, string? keepId, bool keepScroll = false)
    {
        double offset = scroll.VerticalOffset;
        int v = ++renderVersion;
        int keep = -1, keepStrip = -1;
        inStrip = false;
        var shown = secs.Where(s => s.Widget is not null || s.Items.Count > 0).ToList();
        // the home: its blocks stay as the list holds them where they would show the same (Launcher.Kept.cs)
        string? look = null;
        List<string>? keys = null;
        var made = new Dictionary<Section, FrameworkElement>(); // widgets made for the keys, used if drawn anew
        if (Mode == Mode.Root && box.Text.Length == 0 && shown.Count > 0 && shown.All(s => s.HomeKey is not null))
        {
            look = HomeLook();
            keys = [];
            foreach (var s in shown)
            {
                keys.Add(SectionKey(s, out var widget));
                if (widget is not null) made[s] = widget;
            }
        }
        if (keys is not null && KeepHome(shown, look!, keys, made))
        {
            if (keepId is not null)
            {
                keep = rows.FindIndex(r => r.Item?.Id == keepId);
                keepStrip = strip?.Items.FindIndex(x => x.Id == keepId) ?? -1;
            }
        }
        else
        {
            home = null;
            list.Children.Clear();
            rows.Clear();
            strip = null;
            // the list as steps in order (a section's start, then its rows), so the late rows land where they belong
            var steps = new List<(Action run, bool row)>();
            Panel into = list;
            int first = opening ? ScreenRows() : FirstRows, need = first, counted = 0;
            var holders = new UIElement?[shown.Count];
            var starts = new int[shown.Count];
            var strips = new FavStrip?[shown.Count];
            for (int n = 0; n < shown.Count; n++)
            {
                var sec = shown[n];
                int at = n;
                // the favorites row comes in the first go wherever it is: the home opens on it (Enter runs the first one)
                if (sec.Strip) need = Math.Max(need, counted);
                steps.Add((() =>
                {
                    starts[at] = rows.Count;
                    into = StartSection(sec, keepId, made.GetValueOrDefault(sec), out int ks, out holders[at]);
                    if (ks >= 0) keepStrip = ks;
                    if (sec.Strip) strips[at] = strip;
                }, false));
                if (sec.Widget is not null || sec.Strip) continue;
                foreach (var item in sec.Items)
                {
                    if (keepId is not null && item.Id == keepId && need == first) need = Math.Max(first, counted + 1);
                    counted++;
                    var it = item;
                    steps.Add((() =>
                    {
                        if (keepId is not null && it.Id == keepId && keep < 0) keep = rows.Count;
                        into.Children.Add(AddRow(it));
                    }, true));
                }
            }
            if (keys is not null)
                steps.Add((() =>
                {
                    // the home whole (its late rows too), one box per block: kept for the next time
                    if (list.Children.Count != shown.Count) return;
                    var blocks = new List<Block>(shown.Count);
                    for (int n = 0; n < shown.Count; n++)
                    {
                        int end = n + 1 < shown.Count ? starts[n + 1] : rows.Count;
                        blocks.Add(new Block(keys[n], shown[n].HomeKey, holders[n]!, rows.GetRange(starts[n], end - starts[n]), strips[n]));
                    }
                    home = blocks;
                    homeLook = look;
                }, false));
            int built = 0, next = 0;
            for (; next < steps.Count; next++)
            {
                if (steps[next].row && built >= need) break;
                steps[next].run();
                if (steps[next].row) built++;
            }
            if (next < steps.Count)
            {
                int from = next;
                void Rest()
                {
                    if (v != renderVersion) return; // a newer list is showing
                    for (int i = from; i < steps.Count; i++) steps[i].run();
                    if (keepScroll) scroll.ScrollToVerticalOffset(offset);
                }
                if (opening) afterReveal = Rest;
                else Dispatcher.BeginInvoke(Rest, System.Windows.Threading.DispatcherPriority.Background);
            }
            if (rows.Count == 0 && strip is null)
            {
                ShowPreview(null);
                var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 64, 0, 0) };
                empty.Children.Add(Theme.Glyph(G.Search, 26, Theme.P.TextFaint));
                var t = Theme.T(Mode == Mode.Root && box.Text.Length == 0 ? Tr("검색어를 입력하세요", "Type to search") : Tr("검색 결과 없음", "Nothing found"), 13.5, Theme.P.TextFaint);
                t.Margin = new Thickness(0, 12, 0, 0);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(t);
                list.Children.Add(empty);
            }
        }
        sel = -1;
        // the empty view starts on the first favorite (Enter opens it, as before the row of icons)
        if (strip is not null && (keepStrip >= 0 || keep < 0)) SelectStrip(Math.Max(0, keepStrip));
        else Select(keep >= 0 ? keep : 0, false);
        if (keepScroll) scroll.ScrollToVerticalOffset(offset);
        else if (keep < 0) scroll.ScrollToTop();
        UpdateChrome();
    }

    /// A section's start in the list: a home block's own box (▲ ▼ 숨기기 while 홈 편집 is on), a widget, the favorites
    /// strip or a header. Returns the panel its rows go into; `keepStrip`: the kept item's place in the strip (or -1);
    /// `holder`: a home block's box. `widget`: the section's widget if already made (SectionKey).
    private Panel StartSection(Section s, string? keepId, FrameworkElement? widget, out int keepStrip, out UIElement? holder)
    {
        keepStrip = -1;
        holder = null;
        Panel into = list;
        if (s.HomeKey is { } homeKey)
        {
            var inner = new StackPanel();
            var blockBox = new Grid { Background = Brushes.Transparent };
            blockBox.Children.Add(inner);
            var tools = HomeTools(homeKey);
            // only while 홈 편집 is on (the footer button / Ctrl+E), never on a mere hover
            tools.Visibility = HomeEditing || ShowHomeToolsForQa ? Visibility.Visible : Visibility.Collapsed;
            blockBox.Children.Add(tools);
            list.Children.Add(blockBox);
            holder = blockBox;
            into = inner;
        }
        if (s.Widget is { } make)
        {
            if (s.Title is not null) into.Children.Add(Section2.Header(s.Title, s.Note, s.Reload));
            into.Children.Add(widget ?? make());
            return into;
        }
        if (s.Strip)
        {
            var st = new FavStrip(s.Items);
            st.Clicked = i =>
            {
                SelectStrip(i);
                RunSelected(null);
            };
            st.RightClicked = i =>
            {
                SelectStrip(i);
                OpenActions();
            };
            var row = s.Items;
            st.Reordered = (from, before) =>
            {
                SelectStrip(from); // the moved one stays selected after the refresh
                Favorites.MoveBefore(row[from].Id, before < row.Count ? row[before].Id : null);
                Refresh(true);
            };
            if (keepId is not null) keepStrip = s.Items.FindIndex(x => x.Id == keepId);
            strip = st;
            into.Children.Add(st);
            return into;
        }
        if (s.Title is not null) into.Children.Add(Section2.Header(s.Title, s.Note));
        return into;
    }

    /// One result row, wired to select and run; added to `rows` (the keyboard's order).
    private Row AddRow(ResultItem item)
    {
        Row r = item.Kind == ItemKind.Calc ? Row.Calc(item) : Split || Mode == Mode.Calendar ? Row.Compact(item, Mode is Mode.Clipboard or Mode.Calendar) : Row.Result(item);
        r.Clicked = () =>
        {
            Select(rows.IndexOf(r), false);
            RunSelected(null);
        };
        r.RightClicked = () =>
        {
            Select(rows.IndexOf(r), false);
            OpenActions();
        };
        r.SideClicked = act =>
        {
            Select(rows.IndexOf(r), false);
            RunAction(act);
        };
        rows.Add(r);
        return r;
    }

    private void Select(int index, bool bring = true)
    {
        if (rows.Count == 0)
        {
            sel = -1;
            return;
        }
        index = Math.Clamp(index, 0, rows.Count - 1);
        if (inStrip)
        {
            inStrip = false;
            strip?.Select(-1);
        }
        if (sel >= 0 && sel < rows.Count) rows[sel].Selected = false;
        sel = index;
        rows[sel].Selected = true;
        if (bring) rows[sel].BringIntoView(new Rect(0, -30, rows[sel].ActualWidth, rows[sel].ActualHeight + 60));
        if (Split) ShowPreview(rows[sel].Item);
        UpdateChrome();
    }

    private void SelectStrip(int i)
    {
        if (strip is null || strip.Items.Count == 0) return;
        if (sel >= 0 && sel < rows.Count) rows[sel].Selected = false;
        sel = -1;
        inStrip = true;
        stripSel = Math.Clamp(i, 0, strip.Items.Count - 1);
        strip.Select(stripSel);
        scroll.ScrollToTop();
        UpdateChrome();
    }

    /// Ctrl+1…9: the favorite in that place of the row as shown (ones that can't show — a removed app, a feature switched
    /// off — take no number), from any view of the root.
    private void RunFavorite(int i)
    {
        if (Favorites.Current.Items.Select(Engine.FromFavorite).OfType<ResultItem>().ElementAtOrDefault(i) is not { Run: { } run } item) return;
        if (!item.StayOpen) Dismiss();
        try
        {
            run();
        }
        catch (Exception e)
        {
            Log.Error("favorite " + item.Id, e);
        }
    }

    private void Move(int delta)
    {
        // the favorites row sits above the first line: ↓ leaves it, ↑ from the first line enters it
        if (inStrip)
        {
            if (rows.Count == 0) return;
            Select(delta > 0 ? 0 : delta == -1 ? rows.Count - 1 : 0);
            return;
        }
        if (strip is not null && ((sel == 0 && delta < 0) || (sel == rows.Count - 1 && delta == 1)))
        {
            SelectStrip(delta < 0 ? stripSel : 0);
            return;
        }
        if (rows.Count == 0) return;
        int i = sel + delta;
        if (i < 0) i = delta < -1 ? 0 : rows.Count - 1; // wrap on single steps
        else if (i >= rows.Count) i = delta > 1 ? rows.Count - 1 : 0;
        Select(i);
    }

    public void RunSelected(string? combo)
    {
        var item = Selected;
        if (item is null) return;
        if (combo is not null)
        {
            var act = ActionsFor(item).FirstOrDefault(a => a.Keys == combo);
            if (act is not null) RunAction(act);
            return;
        }
        if (item.Run is null) return;
        if (Mode == Mode.Root && box.Text.Trim().Length > 0) QueryMemory.Learn(box.Text, item.Id);
        if (!item.StayOpen) Dismiss();
        try
        {
            item.Run();
        }
        catch (Exception e)
        {
            Log.Error("run " + item.Id, e);
        }
    }
}
