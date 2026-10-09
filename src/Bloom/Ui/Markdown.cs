using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ui;

/// A small Markdown renderer for chat answers: headings, paragraphs, lists, quotes, rules, pipe
/// tables, fenced code (with a copy button), **bold**, *italic*, `code`, ~~strike~~ and links — one document per
/// answer, so text can be dragged over and copied anywhere in it (Controls\ReadText.cs).
/// Korean words are kept together when lines wrap (display only; copying leaves the joiners out).
internal static class Markdown
{
    private static readonly Regex ListItem = new(@"^(\s*)([-*+]|\d{1,3}[.)])\s+(.*)$");
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
    private static readonly Regex RuleLine = new(@"^\s*([-*_])(\s*\1){2,}\s*$");
    private static readonly Regex TableSep = new(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$");

    /// The whole answer as one selectable document: drag across paragraphs, lists and tables to copy.
    public static ReadText Render(string md, Action<string> openLink, double size = 14)
    {
        var doc = ReadText.Doc(size);
        Blocks(md, openLink, size, doc.Blocks);
        if (doc.Blocks.LastBlock is { } last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        return new ReadText(doc);
    }

    private static void Blocks(string md, Action<string> openLink, double size, BlockCollection into)
    {
        string[] lines = md.Replace("\r\n", "\n").Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            string trim = line.TrimStart();
            if (trim.StartsWith("```", StringComparison.Ordinal) || trim.StartsWith("~~~", StringComparison.Ordinal))
            {
                string fence = trim[..3];
                string lang = trim[3..].Trim();
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal))
                {
                    if (code.Length > 0) code.Append('\n');
                    code.Append(lines[i]);
                    i++;
                }
                i++; // closing fence (or the end while streaming)
                into.Add(new BlockUIContainer(CodeBlock(code.ToString(), lang)) { Margin = new Thickness(0, 2, 0, 10) });
                continue;
            }
            if (trim.Length == 0)
            {
                i++;
                continue;
            }
            var h = Heading.Match(line);
            if (h.Success)
            {
                int level = h.Groups[1].Value.Length;
                var p = ReadText.Paragraph(size + (level <= 1 ? 3 : level == 2 ? 1.5 : 0.5), Theme.Semi);
                Inline(h.Groups[2].Value, p.Inlines, openLink);
                p.Margin = new Thickness(0, into.Count == 0 ? 0 : 6, 0, 6);
                into.Add(p);
                i++;
                continue;
            }
            if (RuleLine.IsMatch(line))
            {
                into.Add(new BlockUIContainer(new SmoothBorder { Height = 1, Background = Theme.P.Line }) { Margin = new Thickness(0, 6, 0, 10) });
                i++;
                continue;
            }
            if (trim.StartsWith('|') && i + 1 < lines.Length && TableSep.IsMatch(lines[i + 1]))
            {
                var rows = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('|'))
                {
                    if (!TableSep.IsMatch(lines[i])) rows.Add(lines[i]);
                    i++;
                }
                into.Add(Table(rows, openLink, size));
                continue;
            }
            if (trim.StartsWith('>'))
            {
                var quote = new StringBuilder();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    string q = lines[i].TrimStart()[1..];
                    if (q.StartsWith(' ')) q = q[1..];
                    quote.Append(q).Append('\n');
                    i++;
                }
                var section = new Section
                {
                    BorderBrush = Theme.P.Line,
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    Padding = new Thickness(12, 2, 0, 2),
                    Margin = new Thickness(0, 0, 0, 8),
                };
                Blocks(quote.ToString().TrimEnd(), openLink, size, section.Blocks);
                if (section.Blocks.LastBlock is { } lastQ) lastQ.Margin = new Thickness(lastQ.Margin.Left, lastQ.Margin.Top, lastQ.Margin.Right, 0);
                into.Add(section);
                continue;
            }
            if (ListItem.IsMatch(line))
            {
                int number = 0;
                Paragraph? prev = null;
                while (i < lines.Length)
                {
                    var m = ListItem.Match(lines[i]);
                    if (!m.Success)
                    {
                        // a wrapped continuation line belongs to the previous item
                        if (lines[i].StartsWith("  ", StringComparison.Ordinal) && lines[i].Trim().Length > 0 && prev is not null)
                        {
                            prev.Inlines.Add(new LineBreak());
                            Inline(lines[i].Trim(), prev.Inlines, openLink);
                            i++;
                            continue;
                        }
                        break;
                    }
                    int indent = m.Groups[1].Value.Replace("\t", "    ").Length / 2;
                    string marker = m.Groups[2].Value;
                    bool ordered = char.IsDigit(marker[0]);
                    number = ordered ? int.TryParse(marker.TrimEnd('.', ')'), out int n) ? n : number + 1 : number;
                    prev = Item(ordered ? number + "." : indent > 0 ? "◦" : "•", ordered ? 22 : 16, Math.Min(indent, 4) * 18, size);
                    Inline(m.Groups[3].Value, prev.Inlines, openLink);
                    into.Add(prev);
                    i++;
                }
                if (prev is not null) prev.Margin = new Thickness(prev.Margin.Left, 0, 0, 11); // the item's 3 + the list's own 8 after it
                continue;
            }
            // paragraph: until a blank line or another block
            var para = ReadText.Paragraph(size, Theme.Regular);
            bool firstLine = true;
            while (i < lines.Length)
            {
                string l = lines[i];
                string lt = l.TrimStart();
                if (lt.Length == 0 || lt.StartsWith("```", StringComparison.Ordinal) || lt.StartsWith("~~~", StringComparison.Ordinal) || Heading.IsMatch(l) || ListItem.IsMatch(l) || lt.StartsWith('>') || (lt.StartsWith('|') && i + 1 < lines.Length && TableSep.IsMatch(lines[i + 1])) || RuleLine.IsMatch(l)) break;
                if (!firstLine) para.Inlines.Add(new LineBreak());
                Inline(l.TrimEnd(), para.Inlines, openLink);
                firstLine = false;
                i++;
            }
            into.Add(para);
        }
    }

    /// One list item: the marker in a column of its own (`width`), the text hanging beside it when it wraps.
    private static Paragraph Item(string marker, double width, double indent, double size)
    {
        var p = ReadText.Paragraph(size, Theme.Regular);
        p.Margin = new Thickness(indent + width, 0, 0, 3);
        p.TextIndent = -width;
        var mark = Theme.T(marker, size, Theme.P.TextFaint, Theme.Regular, false);
        mark.Width = width;
        // an inline element sits on the baseline by its bottom edge: the part under the marker's own baseline hangs below
        mark.Margin = new Thickness(0, 0, 0, -(Theme.Sans.LineSpacing - Theme.Sans.Baseline) * size);
        p.Inlines.Add(new InlineUIContainer(mark) { BaselineAlignment = BaselineAlignment.Baseline });
        return p;
    }

    public static TextBlock Para(double size, FontWeight weight) => new()
    {
        FontFamily = Theme.Sans,
        FontSize = size,
        FontWeight = weight,
        Foreground = Theme.P.Text,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = Math.Round(size * 1.6),
        Margin = new Thickness(0, 0, 0, 8),
    };

    /// Fenced code: monospace on its own tint, with 복사 for all of it. It is drawn as a plain TextBlock and becomes
    /// selectable (a ReadText, laid out the same) when the mouse first comes over it: a text box of its own inside the
    /// answer's costs about 20 KB of memory, which most code blocks are never dragged over to need.
    private static SmoothBorder CodeBlock(string code, string lang)
    {
        var grid = new Grid();
        var holder = new Border
        {
            Margin = new Thickness(0, lang.Length > 0 ? 18 : 0, 0, 0),
            Background = Brushes.Transparent, // the mouse finds it between the lines too
            Child = new TextBlock { Text = code, FontFamily = Theme.Mono, FontSize = 12.5, Foreground = Theme.P.Text, TextWrapping = TextWrapping.Wrap, LineHeight = 19 },
        };
        holder.MouseEnter += Selectable;
        void Selectable(object sender, MouseEventArgs e)
        {
            holder.MouseEnter -= Selectable;
            var p = new Paragraph(new Run(code)) { FontFamily = Theme.Mono, FontSize = 12.5, LineHeight = 19, Margin = new Thickness(0) };
            holder.Child = new ReadText(ReadText.Doc(12.5, p));
        }
        grid.Children.Add(holder);
        if (lang.Length > 0)
        {
            var l = Theme.T(lang, 11, Theme.P.TextFaint, Theme.Medium);
            l.VerticalAlignment = VerticalAlignment.Top;
            l.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(l);
        }
        var copyText = Theme.T(Tr("복사", "Copy"), 11, Theme.P.TextDim, Theme.Medium, false);
        var copy = new SmoothBorder
        {
            Child = copyText,
            Padding = new Thickness(7, 1, 7, 2),
            CornerRadius = new CornerRadius(5),
            Background = Theme.P.Key,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, -2, -4, 0),
        };
        copy.MouseLeftButtonUp += (_, e) =>
        {
            Search.Launch.Copy(code);
            copyText.Text = Tr("복사 완료", "Copied");
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                copyText.Text = Tr("복사", "Copy");
            };
            t.Start();
            e.Handled = true;
        };
        grid.Children.Add(copy);
        return new SmoothBorder
        {
            Background = Theme.P.Code,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 10),
            Child = grid,
        };
    }

    private static Table Table(List<string> rows, Action<string> openLink, double size)
    {
        var cells = new List<string[]>();
        int cols = 0;
        foreach (string r in rows)
        {
            string t = r.Trim();
            if (t.StartsWith('|')) t = t[1..];
            if (t.EndsWith('|')) t = t[..^1];
            var parts = t.Split('|');
            for (int k = 0; k < parts.Length; k++) parts[k] = parts[k].Trim();
            cols = Math.Max(cols, parts.Length);
            cells.Add(parts);
        }
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 10) };
        for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        for (int r = 0; r < cells.Count; r++)
        {
            var row = new TableRow();
            for (int c = 0; c < cols; c++)
            {
                var p = ReadText.Paragraph(size - 1, r == 0 ? Theme.Semi : Theme.Regular);
                p.Margin = new Thickness(0);
                p.LineHeight = Math.Round((size - 1) * 1.5);
                if (c < cells[r].Length) Inline(cells[r][c], p.Inlines, openLink);
                row.Cells.Add(new TableCell(p) { Padding = new Thickness(0, 6, 10, 6), BorderBrush = Theme.P.Line, BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            group.Rows.Add(row);
        }
        return table;
    }

    // ── inline ──────────────────────────────────────────────────────────────────────────────────
    private static readonly Regex Token = new(
        @"(\*\*(?<b>.+?)\*\*)|(__(?<b2>.+?)__)|(~~(?<s>.+?)~~)|(`(?<c>[^`]+)`)|(\[(?<lt>[^\]]+)\]\((?<lu>[^)\s]+)\))|(?<u>https?://[^\s<>()\]]+[^\s<>()\].,;:!?'""])|((?<![\w*])\*(?<i>[^*\s][^*]*?)\*(?![\w*]))|((?<![\w_])_(?<i2>[^_\s][^_]*?)_(?![\w_]))",
        RegexOptions.Compiled);

    public static void Inline(string s, InlineCollection into, Action<string> openLink)
    {
        int at = 0;
        foreach (Match m in Token.Matches(s))
        {
            if (m.Index > at) into.Add(new Run(Hangul.KeepAll(s[at..m.Index])));
            if (m.Groups["b"].Success || m.Groups["b2"].Success)
            {
                var b = new Bold();
                Inline(m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["b2"].Value, b.Inlines, openLink);
                into.Add(b);
            }
            else if (m.Groups["s"].Success)
            {
                var sp = new Span { TextDecorations = TextDecorations.Strikethrough };
                Inline(m.Groups["s"].Value, sp.Inlines, openLink);
                into.Add(sp);
            }
            else if (m.Groups["c"].Success)
            {
                into.Add(new Run(" " + m.Groups["c"].Value + " ") { FontFamily = Theme.Mono, FontSize = 12.5, Background = Theme.P.Code });
            }
            else if (m.Groups["lt"].Success)
            {
                into.Add(Link(m.Groups["lt"].Value, m.Groups["lu"].Value, openLink));
            }
            else if (m.Groups["u"].Success)
            {
                string u = m.Groups["u"].Value;
                into.Add(Link(u.Length > 60 ? u[..57] + "…" : u, u, openLink));
            }
            else if (m.Groups["i"].Success || m.Groups["i2"].Success)
            {
                var it = new Italic();
                Inline(m.Groups["i"].Success ? m.Groups["i"].Value : m.Groups["i2"].Value, it.Inlines, openLink);
                into.Add(it);
            }
            at = m.Index + m.Length;
        }
        if (at < s.Length) into.Add(new Run(Hangul.KeepAll(s[at..])));
    }

    private static Hyperlink Link(string text, string url, Action<string> openLink)
    {
        var h = new Hyperlink(new Run(Hangul.KeepAll(text))) { Foreground = Theme.P.AccentText, TextDecorations = null, Cursor = Cursors.Hand };
        h.Click += (_, _) => openLink(url);
        h.MouseEnter += (_, _) => h.TextDecorations = TextDecorations.Underline;
        h.MouseLeave += (_, _) => h.TextDecorations = null;
        return h;
    }
}
