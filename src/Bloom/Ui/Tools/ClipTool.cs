using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Clip;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 클립보드: what was copied lately (search in it); a click puts one back on the clipboard.
internal sealed class ClipTool : ITool
{
    private readonly Host host;
    private readonly Grid view = new();
    private readonly StackPanel list = new() { Margin = new Thickness(8, 4, 8, 8) };
    private readonly TextBox search;
    private readonly TextBlock note;

    public string Key => "clipboard";
    public string Name => Tr("클립보드", "Clipboard");
    public char Glyph => G.Paste;
    public FrameworkElement View => view;
    public FrameworkElement? Head => null;
    public string? State => null;

    public ClipTool(Host host)
    {
        this.host = host;
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var (field, box) = ToolUi.Field(Tr("복사한 것 찾기", "Find in what you copied"));
        search = box;
        field.Margin = new Thickness(12, 10, 12, 2);
        view.Children.Add(field);
        note = Theme.T(Tr("누르면 다시 복사돼요", "Click one to copy it again"), 11.5, Theme.P.TextFaint);
        note.Margin = new Thickness(16, 4, 16, 2);
        Grid.SetRow(note, 1);
        view.Children.Add(note);
        var scroll = ToolUi.Scroll(list);
        Grid.SetRow(scroll, 2);
        view.Children.Add(scroll);
        search.TextChanged += (_, _) => Fill();
        host.Clipboard.Changed += OnChanged;
        Fill();
    }

    private void OnChanged() => view.Dispatcher.BeginInvoke(Fill);

    private void Fill()
    {
        list.Children.Clear();
        if (!Settings.Current.ClipHistory)
        {
            list.Children.Add(ToolUi.Empty(Tr("클립보드 기록이 꺼져 있어요", "Clipboard history is off")));
            var on = ToolUi.TextButton(Tr("켜기", "Turn on"), () =>
            {
                Settings.Current.ClipHistory = true;
                Settings.Save();
                host.ApplyHotkeys();
                Fill();
            });
            on.HorizontalAlignment = HorizontalAlignment.Center;
            on.Margin = new Thickness(0, 10, 0, 0);
            list.Children.Add(on);
            return;
        }
        string q = search.Text.Trim();
        var items = (q.Length == 0 ? host.Clipboard.Items.AsEnumerable() : host.Clipboard.Find(q)).Take(40).ToList();
        if (items.Count == 0)
        {
            list.Children.Add(ToolUi.Empty(q.Length == 0 ? Tr("아직 복사한 게 없어요", "Nothing copied yet") : Tr("맞는 게 없어요", "Nothing matches")));
            return;
        }
        foreach (var e in items)
        {
            var g = new StackPanel();
            var main = Theme.T(Text(e), 13, Theme.P.Text, Theme.Medium);
            g.Children.Add(main);
            var sub = Theme.T(ToolUi.Ago(e.CopiedAt) + (e.Source is { Length: > 0 } s ? " · " + s : ""), 11.5, Theme.P.TextFaint);
            sub.Margin = new Thickness(0, 2, 0, 0);
            g.Children.Add(sub);
            var entry = e;
            list.Children.Add(ToolUi.Row(g, () =>
            {
                note.Text = host.Clipboard.Put(entry) ? Tr("복사했어요 — 붙여 넣으면 돼요", "Copied — paste it anywhere") : Tr("복사하지 못했어요", "Couldn't copy it");
            }));
        }
    }

    /// One line for an entry: its text, the picture's size, or the files' names.
    private static string Text(ClipEntry e) => e.Kind switch
    {
        "image" => Tr($"그림 {e.Width}×{e.Height}", $"Picture {e.Width}×{e.Height}"),
        "files" => Tr("파일: ", "Files: ") + string.Join(", ", (e.Files ?? new()).Select(Path.GetFileName)),
        _ => string.Join(" ", (e.Text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)),
    };

    public void FocusFirst() => search.Dispatcher.BeginInvoke(() =>
    {
        search.Focus();
        Keyboard.Focus(search);
    }, DispatcherPriority.Input);

    public void Closed() => host.Clipboard.Changed -= OnChanged;
}
