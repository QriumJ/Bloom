using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.Ui;

/// The notices (App\Notices) as cards in the screen corner or edge picked in 설정 > 알림, newest nearest the edge, at the
/// size picked there. Drawn like the timer card (AlarmWindow). Never takes the focus; a click closes a card, the mouse
/// over one holds it, and a card with a key (승인 대기) stays until Notices takes it away.
internal sealed class NoticeWindow : Window
{
    private const int MaxCards = 4;
    private readonly StackPanel stack = new() { Margin = new Thickness(0, 4, 0, 4) };
    private readonly ScaleTransform scale = new(1, 1);
    private readonly Dictionary<string, FrameworkElement> keyed = new();

    /// QA: laid out off-screen, never shown on the user's screens.
    public bool Offscreen;

    public NoticeWindow()
    {
        Title = "Bloom";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        Content = new Border { Child = stack, LayoutTransform = scale };
        SourceInitialized += (_, _) =>
        {
            Glass.ToolWindow(this);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex | Win32.WS_EX_NOACTIVATE)); // a click never takes the focus
        };
        SizeChanged += (_, _) => Place();
    }

    private static bool AtTop => Settings.Current.NoticeSpot.StartsWith("top", StringComparison.Ordinal);

    /// A new card (one with the same key replaces the old one).
    public void Add(Notice n)
    {
        double s = Settings.Current.NoticeSize switch { "s" => 0.85, "l" => 1.2, "xl" => 1.4, _ => 1 };
        scale.ScaleX = scale.ScaleY = s;
        if (n.Key is { } k && keyed.Remove(k, out var old)) stack.Children.Remove(old);
        var card = Card(n);
        if (AtTop) stack.Children.Insert(0, card);
        else stack.Children.Add(card);
        if (n.Key is { } key) keyed[key] = card;
        while (stack.Children.Count > MaxCards) Dismiss((FrameworkElement)stack.Children[AtTop ? stack.Children.Count - 1 : 0], false);
        if (!IsVisible) Show();
        Place();
        Enter(card);
    }

    /// The card with this key goes (its 승인 대기 ended).
    public void Remove(string key)
    {
        if (keyed.TryGetValue(key, out var card)) Dismiss(card, true);
    }

    public void Clear()
    {
        stack.Children.Clear();
        keyed.Clear();
        Hide();
    }

    public void Capture(string file) => Shots.Save((FrameworkElement)Content, file);

    private void Place()
    {
        if (Offscreen)
        {
            Left = Top = -32000;
            return;
        }
        var wa = SystemParameters.WorkArea;
        string spot = Settings.Current.NoticeSpot;
        Left = spot.EndsWith("left", StringComparison.Ordinal) ? wa.Left + 4
            : spot.EndsWith("right", StringComparison.Ordinal) ? wa.Right - ActualWidth - 4
            : wa.Left + ((wa.Width - ActualWidth) / 2);
        Top = AtTop ? wa.Top + 4 : wa.Bottom - ActualHeight - 4;
    }

    private FrameworkElement Card(Notice n)
    {
        var tint = Tint(n.Kind);
        var icon = new SmoothBorder
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0x2E, tint.R, tint.G, tint.B)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 12, 0),
            Child = Theme.Glyph(Glyph(n.Kind), 17, new SolidColorBrush(tint)),
        };
        var text = new StackPanel();
        text.Children.Add(Theme.T(n.Head, 12, new SolidColorBrush(tint), Theme.Semi));
        var title = Theme.T(n.Title, 14, Theme.P.Text, Theme.Medium); // one line each, then … (the tooltip has it all)
        title.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(title);
        if (n.Detail is { Length: > 0 } d)
        {
            var detail = Theme.T(d, 12, Theme.P.TextFaint);
            detail.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(detail);
        }
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(icon);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var card = new SmoothBorder
        {
            Background = Theme.P.Solid,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 16, 12),
            Width = 330,
            Margin = new Thickness(10, 4, 10, 4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Direction = 270, Opacity = 0.3 },
            Cursor = Cursors.Hand,
            Child = row,
            ToolTip = n.Detail is { Length: > 0 } ? $"{n.Title}\n{n.Detail}" : n.Title,
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Dismiss(card, true);
        };
        if (n.Key is null) // a keyed card stays until its wait ends
        {
            var life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.Current.NoticeSeconds, 2, 60) + (n.Kind is "limit" or "recover" ? 4 : 0)) };
            life.Tick += (_, _) =>
            {
                life.Stop();
                Dismiss(card, true);
            };
            card.MouseEnter += (_, _) => life.Stop();
            card.MouseLeave += (_, _) =>
            {
                life.Interval = TimeSpan.FromSeconds(2);
                life.Start();
            };
            card.Unloaded += (_, _) => life.Stop();
            life.Start();
        }
        return card;
    }

    /// 진행 중 · 작업 완료 in the accent, 승인 대기 in tangerine, 한도 in red, 복구 in mint.
    public static Color Tint(string kind) => kind switch
    {
        "waiting" => Accents.Swatch(Accents.Find("tangerine")!, Theme.P.Dark),
        "limit" => Theme.P.Danger.Color,
        "recover" => Accents.Swatch(Accents.Find("mint")!, Theme.P.Dark),
        _ => Theme.P.Accent.Color,
    };

    private static char Glyph(string kind) => kind switch
    {
        "start" => G.Gears,
        "waiting" => G.Clock,
        "limit" => G.Warning,
        "recover" => G.Sync,
        _ => G.Done,
    };

    private void Enter(FrameworkElement card)
    {
        if (Motion.Moves)
        {
            var move = new TranslateTransform(0, AtTop ? -10 : 10);
            card.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.YProperty, Motion.Paced(new DoubleAnimation(AtTop ? -10 : 10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } }));
        }
        if (Motion.Fades) card.BeginAnimation(OpacityProperty, Motion.Paced(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))));
    }

    /// Out (faded when fades are on); the window hides with the last card.
    private void Dismiss(FrameworkElement card, bool fade)
    {
        foreach (var (k, c) in keyed)
            if (c == card)
            {
                keyed.Remove(k);
                break;
            }
        void Gone()
        {
            stack.Children.Remove(card);
            if (stack.Children.Count == 0) Hide();
        }
        if (!fade || !Motion.Fades || !stack.Children.Contains(card))
        {
            Gone();
            return;
        }
        card.IsHitTestVisible = false;
        var a = Motion.Paced(new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
        a.Completed += (_, _) => Gone();
        card.BeginAnimation(OpacityProperty, a);
    }
}
