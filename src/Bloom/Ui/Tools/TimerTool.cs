using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 타이머 · 알림: a line to start one in words ("10분", "오후 3시 회의"), and the ones waiting with the time left.
internal sealed class TimerTool : ITool
{
    private readonly Grid view = new();
    private readonly StackPanel list = new() { Margin = new Thickness(8, 4, 8, 8) };
    private readonly TextBox input;
    private readonly TextBlock note;
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool unread; // the note says the last line had no time it could read

    public string Key => "timers";
    public string Name => Tr("타이머 · 알림", "Timers");
    public char Glyph => G.Stopwatch;
    public FrameworkElement View => view;
    public FrameworkElement? Head => null;
    public string? State => null;

    public TimerTool()
    {
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var (field, box) = ToolUi.Field(Tr("10분 · 오후 3시 회의 · 25분 집중", "10 min · 3pm meeting · 25 min focus"), enter: () => Start());
        input = box;
        field.Margin = new Thickness(12, 10, 12, 2);
        view.Children.Add(field);
        note = Theme.T(Tr("Enter로 시작", "Enter starts it"), 11.5, Theme.P.TextFaint);
        note.Margin = new Thickness(16, 4, 16, 2);
        Grid.SetRow(note, 1);
        view.Children.Add(note);
        var scroll = ToolUi.Scroll(list);
        Grid.SetRow(scroll, 2);
        view.Children.Add(scroll);
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            Start();
        };
        // typing again after a line it couldn't read: back to the plain hint
        input.TextChanged += (_, _) =>
        {
            if (!unread) return;
            unread = false;
            note.Text = Tr("Enter로 시작", "Enter starts it");
        };
        tick.Tick += (_, _) => Tick();
        view.IsVisibleChanged += (_, _) =>
        {
            if (view.IsVisible) tick.Start();
            else tick.Stop();
        };
        Timers.Changed += OnChanged;
        Fill();
    }

    private void OnChanged() => view.Dispatcher.BeginInvoke(Fill);

    private void Start()
    {
        string text = input.Text.Trim();
        if (text.Length == 0) return;
        // "10" alone reads as minutes here: this field only ever starts timers
        if (int.TryParse(text, out int minutes) && minutes is > 0 and <= 1440) text += Tr("분", " min");
        // this field only starts timers and reminders, so a time needs no 타이머 / 알림 word ("10분 회의", "오후 3시 회의")
        if (TimeParse.Read(text, timerField: true) is not { } ask)
        {
            note.Text = Tr("시간을 못 읽었어요 — 예: 10분, 1시간 30분, 오후 3시 회의", "Couldn't read a time — e.g. 10 min, 1h 30m, 3pm meeting");
            unread = true;
            return;
        }
        var due = ask.Length is { } l ? DateTime.Now + l : ask.Due;
        Timers.Add(ask.Kind, ask.Label, due);
        note.Text = ask.Kind == "timer" ? Tr($"시작했어요 · {Timers.WhenText(due)}에 끝나요", $"Started · ends {Timers.WhenText(due)}") : Tr($"{Timers.WhenText(due)}에 알려 드릴게요", $"I'll remind you {Timers.WhenText(due)}");
        input.Clear();
    }

    /// Every second: only the time left changes (the rows stay, so a button under the mouse stays lit).
    private void Tick()
    {
        foreach (var (t, left) in shown) left.Text = Timers.LeftText(t.Left);
    }

    private readonly System.Collections.Generic.List<(TimerEntry t, TextBlock left)> shown = new();

    private void Fill()
    {
        list.Children.Clear();
        shown.Clear();
        var waiting = Timers.Waiting.ToList();
        if (waiting.Count == 0)
        {
            list.Children.Add(ToolUi.Empty(Tr("진행 중인 타이머 · 알림이 없어요", "No timers or reminders running")));
            return;
        }
        foreach (var t in waiting)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            var left = Theme.T(Timers.LeftText(t.Left), 15, Theme.P.Text, Theme.Semi);
            shown.Add((t, left));
            text.Children.Add(left);
            var sub = Theme.T($"{t.Label} · {Timers.WhenText(t.DueAt)}", 11.5, Theme.P.TextFaint);
            sub.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(sub);
            g.Children.Add(text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string id = t.Id;
            buttons.Children.Add(ToolUi.Button(G.Add, Tr("5분 더", "5 more min"), () => Timers.Extend(id, TimeSpan.FromMinutes(5))));
            buttons.Children.Add(ToolUi.Button(G.Close, Tr("취소", "Cancel"), () => Timers.Remove(id)));
            Grid.SetColumn(buttons, 1);
            g.Children.Add(buttons);
            list.Children.Add(ToolUi.Row(g, null));
        }
    }

    public void FocusFirst() => input.Dispatcher.BeginInvoke(() =>
    {
        input.Focus();
        Keyboard.Focus(input);
    }, DispatcherPriority.Input);

    public void Closed()
    {
        tick.Stop();
        Timers.Changed -= OnChanged;
    }
}
