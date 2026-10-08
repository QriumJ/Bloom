using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 메모: the list of notes (search, pinned first) and one note open to write in; every keystroke is kept.
internal sealed class NotesTool : ITool
{
    private readonly Grid view = new();
    private readonly Grid listPage = new();
    private readonly StackPanel list = new() { Margin = new Thickness(8, 4, 8, 8) };
    private readonly TextBox search, editor;
    private readonly StackPanel head = new() { Orientation = Orientation.Horizontal };
    private readonly SmoothBorder backButton, deleteButton;
    private readonly TextBlock saved;
    private Note? note; // the open one; null: the list

    public string Key => "notes";
    public string Name => Tr("메모", "Notes");
    public char Glyph => G.Document;
    public FrameworkElement View => view;
    public FrameworkElement? Head => head;
    public string? State => note?.Id;

    public NotesTool(string? noteId)
    {
        // the list: a search field, a new note, the notes
        listPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var (field, box) = ToolUi.Field(Tr("메모 찾기 · Enter로 새 메모", "Find a note · Enter for a new one"));
        search = box;
        field.Margin = new Thickness(12, 10, 12, 4);
        listPage.Children.Add(field);
        var scroll = ToolUi.Scroll(list);
        Grid.SetRow(scroll, 1);
        listPage.Children.Add(scroll);
        search.TextChanged += (_, _) => Fill();
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            string q = search.Text.Trim();
            var hit = q.Length == 0 ? null : Matching(q).FirstOrDefault();
            Open(hit ?? Notes.New(q));
        };

        // one note: the text, and when it was kept
        var page = new Grid();
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor = new TextBox
        {
            FontFamily = Theme.Sans,
            FontSize = 14,
            Foreground = Theme.P.Text,
            CaretBrush = Theme.P.Accent,
            SelectionBrush = Theme.P.Selection,
            SelectionOpacity = 1,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14, 12, 12, 8),
        };
        editor.TextChanged += (_, _) =>
        {
            if (note is null) return;
            Notes.SetText(note, editor.Text);
            saved!.Text = Tr("저장됨", "Saved");
        };
        page.Children.Add(editor);
        saved = Theme.T("", 11.5, Theme.P.TextFaint);
        saved.Margin = new Thickness(14, 2, 14, 8);
        Grid.SetRow(saved, 1);
        page.Children.Add(saved);
        page.Visibility = Visibility.Collapsed;
        view.Children.Add(listPage);
        view.Children.Add(page);

        backButton = ToolUi.Button('', Tr("메모 목록", "All notes"), () => Open(null));
        deleteButton = ToolUi.Button(G.Delete, Tr("이 메모 지우기 (휴지통으로)", "Delete this note (to the Recycle Bin)"), () =>
        {
            if (note is { } n) Notes.Delete(n);
            note = null;
            Open(null);
        });
        head.Children.Add(backButton);
        head.Children.Add(deleteButton);
        head.Children.Add(ToolUi.Button(G.Add, Tr("새 메모", "New note"), () => Open(Notes.New())));

        Notes.Changed += OnChanged;
        Open(noteId is null ? null : Notes.Find(noteId));
    }

    /// Shows a note by its id (the search line's results open their note here).
    public void Show(string id)
    {
        if (Notes.Find(id) is { } n) Open(n);
    }

    /// QA: a note being written, as typed so far (a new one on the first call).
    public void TypeForQa(string text)
    {
        if (note is null) Open(Notes.New());
        editor.Text = text;
        editor.CaretIndex = text.Length;
    }

    private void Open(Note? n)
    {
        // an empty note left behind just goes
        if (note is { } was && !ReferenceEquals(was, n) && was.Text.Trim().Length == 0 && Notes.Find(was.Id) is not null) Notes.Delete(was);
        note = n;
        var page = (FrameworkElement)view.Children[1];
        listPage.Visibility = n is null ? Visibility.Visible : Visibility.Collapsed;
        page.Visibility = n is null ? Visibility.Collapsed : Visibility.Visible;
        backButton.Visibility = deleteButton.Visibility = n is null ? Visibility.Collapsed : Visibility.Visible;
        if (n is null)
        {
            search.Clear();
            Fill();
        }
        else
        {
            editor.Text = n.Text;
            editor.CaretIndex = editor.Text.Length;
            saved.Text = Tr($"자동 저장 · {ToolUi.Ago(n.UpdatedAt)}", $"Saved as you type · {ToolUi.Ago(n.UpdatedAt)}");
        }
        FocusFirst();
    }

    private static System.Collections.Generic.IEnumerable<Note> Matching(string q)
    {
        var query = new Query(q);
        return Notes.Ordered.Where(n => Matcher.Score(query, n.Match) > 0);
    }

    private void OnChanged(Note? changed)
    {
        // the open note's own keystrokes don't redraw anything; the list follows every other change
        if (note is null) Fill();
    }

    private void Fill()
    {
        list.Children.Clear();
        string q = search.Text.Trim();
        var notes = (q.Length == 0 ? Notes.Ordered : Matching(q)).ToList();
        if (notes.Count == 0)
        {
            list.Children.Add(ToolUi.Empty(q.Length == 0 ? Tr("아직 메모가 없어요 — 위에 적고 Enter", "No notes yet — type above and press Enter") : Tr("맞는 메모가 없어요 — Enter로 새 메모", "No note matches — Enter makes a new one")));
            return;
        }
        foreach (var n in notes)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(Theme.T(n.Title, 13, Theme.P.Text, Theme.Semi));
            string rest = string.Join(" ", n.Text.Split('\n').Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0));
            var sub = Theme.T(ToolUi.Ago(n.UpdatedAt) + (rest.Length > 0 ? " · " + rest : ""), 11.5, Theme.P.TextFaint);
            sub.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(sub);
            g.Children.Add(text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pin = ToolUi.Button(n.Pinned ? '' : '', n.Pinned ? Tr("고정 풀기", "Unpin") : Tr("맨 위에 고정", "Pin to the top"), () => Notes.SetPinned(n, !n.Pinned), n.Pinned ? Theme.P.Accent : null);
            var delete = ToolUi.Button(G.Delete, Tr("지우기 (휴지통으로)", "Delete (to the Recycle Bin)"), () => Notes.Delete(n));
            // a pinned note shows its pin; the rest of the buttons come under the mouse
            delete.Visibility = Visibility.Hidden;
            if (!n.Pinned) pin.Visibility = Visibility.Hidden;
            buttons.Children.Add(pin);
            buttons.Children.Add(delete);
            Grid.SetColumn(buttons, 1);
            g.Children.Add(buttons);
            var row = ToolUi.Row(g, () => Open(n));
            row.MouseEnter += (_, _) => pin.Visibility = delete.Visibility = Visibility.Visible;
            row.MouseLeave += (_, _) =>
            {
                delete.Visibility = Visibility.Hidden;
                if (!n.Pinned) pin.Visibility = Visibility.Hidden;
            };
            list.Children.Add(row);
        }
    }

    public void FocusFirst()
    {
        var target = note is null ? search : editor;
        target.Dispatcher.BeginInvoke(() =>
        {
            target.Focus();
            Keyboard.Focus(target);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    public void Closed()
    {
        Notes.Changed -= OnChanged;
        if (note is { } n && n.Text.Trim().Length == 0 && Notes.Find(n.Id) is not null) Notes.Delete(n);
    }
}
