using System;
using System.Collections.Generic;
using System.Linq;
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

    /// The slow half of a refresh: Everything.
    private async Task Slow()
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
    private const int FirstRows = 24;

    private int renderVersion;

    private void Render(List<Section> secs, string? keepId, bool keepScroll = false)
    {
        double offset = scroll.VerticalOffset;
        int v = ++renderVersion;
        list.Children.Clear();
        rows.Clear();
        strip = null;
        inStrip = false;
        int keep = -1, keepStrip = -1;
        // the list as steps in order (a section's start, then its rows), so the late rows land where they belong
        var steps = new List<(Action run, bool row)>();
        Panel into = list;
        int need = FirstRows, counted = 0;
        foreach (var s in secs)
        {
            if (s.Widget is null && s.Items.Count == 0) continue;
            var sec = s;
            steps.Add((() =>
            {
                into = StartSection(sec, keepId, out int ks);
                if (ks >= 0) keepStrip = ks;
            }, false));
            if (s.Widget is not null || s.Strip) continue;
            foreach (var item in s.Items)
            {
                if (keepId is not null && item.Id == keepId && need == FirstRows) need = Math.Max(FirstRows, counted + 1);
                counted++;
                var it = item;
                steps.Add((() =>
                {
                    if (keepId is not null && it.Id == keepId && keep < 0) keep = rows.Count;
                    into.Children.Add(AddRow(it));
                }, true));
            }
        }
        int made = 0, next = 0;
        for (; next < steps.Count; next++)
        {
            if (steps[next].row && made >= need) break;
            steps[next].run();
            if (steps[next].row) made++;
        }
        if (next < steps.Count)
        {
            int from = next;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                if (v != renderVersion) return; // a newer list is showing
                for (int i = from; i < steps.Count; i++) steps[i].run();
                if (keepScroll) scroll.ScrollToVerticalOffset(offset);
            });
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
        sel = -1;
        // the empty view starts on the first favorite (Enter opens it, as before the row of icons)
        if (strip is not null && (keepStrip >= 0 || keep < 0)) SelectStrip(Math.Max(0, keepStrip));
        else Select(keep >= 0 ? keep : 0, false);
        if (keepScroll) scroll.ScrollToVerticalOffset(offset);
        else if (keep < 0) scroll.ScrollToTop();
        UpdateChrome();
    }

    /// A section's start in the list: a home block's own box (▲ ▼ 숨기기 while 홈 편집 is on), a widget, the favorites
    /// strip or a header. Returns the panel its rows go into; `keepStrip`: the kept item's place in the strip (or -1).
    private Panel StartSection(Section s, string? keepId, out int keepStrip)
    {
        keepStrip = -1;
        Panel into = list;
        if (s.HomeKey is { } homeKey)
        {
            var inner = new StackPanel();
            var block = new Grid { Background = Brushes.Transparent };
            block.Children.Add(inner);
            var tools = HomeTools(homeKey);
            // only while 홈 편집 is on (the footer button / Ctrl+E), never on a mere hover
            tools.Visibility = HomeEditing || ShowHomeToolsForQa ? Visibility.Visible : Visibility.Collapsed;
            block.Children.Add(tools);
            list.Children.Add(block);
            into = inner;
        }
        if (s.Widget is { } widget)
        {
            if (s.Title is not null) into.Children.Add(Section2.Header(s.Title, s.Note, s.Reload));
            into.Children.Add(widget());
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
