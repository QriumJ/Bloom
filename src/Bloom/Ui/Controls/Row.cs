using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Bloom.Search;

namespace Bloom.Ui;

/// One selectable line in a list (results, actions, settings).
internal sealed class Row : SmoothBorder
{
    public readonly ResultItem? Item;
    public readonly Shape Bar;
    public bool Selectable = true;
    private bool selected, hover;
    public Action? Clicked, RightClicked;
    public Action? Tick; // once a second while shown (a live accessory)
    public Action<ActionItem>? SideClicked; // the item's side button (관리자로 실행) was pressed
    private FrameworkElement? side;
    public readonly Grid Cells;

    public Row(ResultItem? item, double height = 42)
    {
        Item = item;
        Height = height;
        CornerRadius = new CornerRadius(8);
        Margin = new Thickness(8, 0, 8, 0);
        Padding = new Thickness(0, 0, 16, 0);
        Background = Brushes.Transparent;
        Cells = new Grid();
        Cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
        Child = Cells;
        Bar = new Rectangle { Width = 3, Height = 18, RadiusX = 1.5, RadiusY = 1.5, Fill = Theme.P.Accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), SnapsToDevicePixels = true, Visibility = Visibility.Hidden };
        Cells.Children.Add(Bar);
        MouseEnter += (_, _) =>
        {
            hover = true;
            Paint();
        };
        MouseLeave += (_, _) =>
        {
            hover = false;
            Paint();
        };
        MouseLeftButtonUp += (_, e) =>
        {
            Clicked?.Invoke();
            e.Handled = true;
        };
        MouseRightButtonUp += (_, e) =>
        {
            RightClicked?.Invoke();
            e.Handled = true;
        };
    }

    public void Add(UIElement el, GridLength width)
    {
        Cells.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        Grid.SetColumn(el, Cells.ColumnDefinitions.Count - 1);
        Cells.Children.Add(el);
    }

    public bool Selected
    {
        get => selected;
        set
        {
            selected = value;
            Paint();
        }
    }

    private void Paint()
    {
        if (CalcFrame is not null)
        {
            CalcFrame.BorderBrush = selected ? Theme.P.Accent : Theme.P.CardLine;
            Background = Brushes.Transparent;
            return;
        }
        Background = selected ? Theme.P.Selected : hover && Selectable ? Theme.P.Hover : Brushes.Transparent;
        Bar.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
        if (side is not null) side.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }

    /// The item's side action as a small button at the row's end, shown only on the selected row (not on hover).
    private void AddSide(ResultItem item)
    {
        if (item.Side is not { } act) return;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Theme.Glyph(act.Glyph, 11, Theme.P.TextDim);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.Margin = new Thickness(0, 1, 5, 0);
        sp.Children.Add(g);
        sp.Children.Add(Theme.T(Tr("관리자로 실행", "Run as admin"), 12, Theme.P.TextDim, Theme.Medium, false));
        var b = new SmoothBorder
        {
            Child = sp,
            Background = Theme.P.Key,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 9, 4),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = act.Keys,
            Visibility = Visibility.Collapsed,
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            SideClicked?.Invoke(act);
            e.Handled = true;
        };
        side = b;
        Add(b, GridLength.Auto);
    }

    /// The standard result line: icon, title, dim subtitle, right accessory.
    public static Row Result(ResultItem item)
    {
        var r = new Row(item);
        var icon = Icons.Build(item.Icon);
        icon.Margin = new Thickness(0, 0, 12, 0);
        r.Add(icon, GridLength.Auto);
        var title = Theme.T(Core.Hangul.KeepAll(item.Title), 14, Theme.P.Text, Theme.Medium);
        title.MaxWidth = item.Subtitle is null ? 600 : 400;
        r.Add(title, GridLength.Auto);
        var sub = Theme.T(item.Subtitle ?? "", 12.5, Theme.P.TextFaint);
        sub.Margin = new Thickness(10, 1, 8, 0);
        r.Add(sub, new GridLength(1, GridUnitType.Star));
        if ((item.Accessory ?? item.Live?.Invoke()) is { } acc)
        {
            var a = Theme.T(acc, 12, Theme.P.TextFaint, Theme.Regular, false);
            r.Add(a, GridLength.Auto);
            if (item.Live is { } live) r.Tick = () => a.Text = live();
        }
        r.AddSide(item);
        return r;
    }

    /// The narrow line next to the preview pane: icon and title (the pane shows the rest).
    public static Row Compact(ResultItem item, bool accessory)
    {
        var r = new Row(item);
        var icon = Icons.Build(item.Icon);
        icon.Margin = new Thickness(0, 0, 12, 0);
        r.Add(icon, GridLength.Auto);
        r.Add(Theme.T(Core.Hangul.KeepAll(item.Title), 14, Theme.P.Text, Theme.Medium), new GridLength(1, GridUnitType.Star));
        if (accessory && (item.Accessory ?? item.Live?.Invoke()) is { } acc)
        {
            var a = Theme.T(acc, 11.5, Theme.P.TextFaint, Theme.Regular, false);
            a.Margin = new Thickness(8, 0, 0, 0);
            r.Add(a, GridLength.Auto);
            if (item.Live is { } live) r.Tick = () => a.Text = live();
        }
        r.AddSide(item);
        return r;
    }

    /// The calculator answer: the expression on the left, the result large on the right.
    public static Row Calc(ResultItem item)
    {
        var r = new Row(item, 96) { CornerRadius = new CornerRadius(12), Margin = new Thickness(8, 4, 8, 6), Padding = new Thickness(0) };
        r.Cells.Children.Remove(r.Bar);
        r.Cells.ColumnDefinitions.Clear();
        var card = new Grid();
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) };
        var expr = Theme.T(item.CalcExpr ?? "", 17, Theme.P.TextDim);
        left.Children.Add(expr);
        var cap1 = Theme.T(item.CalcCaption ?? Tr("수식", "Expression"), 11.5, Theme.P.TextFaint);
        cap1.Margin = new Thickness(0, 6, 0, 0);
        left.Children.Add(cap1);
        card.Children.Add(left);
        var arrow = Theme.Glyph('', 14, Theme.P.TextFaint);
        Grid.SetColumn(arrow, 1);
        card.Children.Add(arrow);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 22, 0) };
        var val = Theme.T(item.CalcValue ?? "", item.CalcValue is { Length: > 18 } ? 21 : 28, Theme.P.Text, Theme.Medium);
        Typography.SetNumeralAlignment(val, FontNumeralAlignment.Tabular);
        right.Children.Add(val);
        var cap2 = Theme.T(item.CalcDetail ?? Tr("결과 · Enter로 복사", "Result · Enter copies"), 11.5, Theme.P.TextFaint);
        cap2.Margin = new Thickness(0, 4, 0, 0);
        right.Children.Add(cap2);
        Grid.SetColumn(right, 2);
        card.Children.Add(right);
        var frame = new SmoothBorder { Background = Theme.P.Card, BorderBrush = Theme.P.CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = card };
        r.Child = frame;
        r.CalcFrame = frame;
        return r;
    }

    public Border? CalcFrame;
}

internal static class Section2
{
    /// A list header: the title, a small note on the right and, when given, a ↻ after the note.
    public static FrameworkElement Header(string title, string? note, Action? reload = null)
    {
        var g = new Grid { Margin = new Thickness(22, 12, 22, 5) };
        g.Children.Add(Theme.T(title, 11.5, Theme.P.TextFaint, Theme.Semi));
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (note is not null)
        {
            var n = Theme.T(note, 11.5, Theme.P.TextFaint);
            n.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(n);
        }
        if (reload is not null)
        {
            var icon = Theme.Glyph(G.Refresh, 11, Theme.P.TextFaint);
            var b = new Border { Background = Brushes.Transparent, Padding = new Thickness(7, 2, 0, 2), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = Tr("새로고침", "Refresh"), Child = icon };
            b.MouseEnter += (_, _) => icon.Foreground = Theme.P.Text;
            b.MouseLeave += (_, _) => icon.Foreground = Theme.P.TextFaint;
            b.MouseLeftButtonDown += (_, e) => e.Handled = true; // the search line keeps the focus
            b.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                reload();
            };
            right.Children.Add(b);
        }
        g.Children.Add(right);
        return g;
    }
}
