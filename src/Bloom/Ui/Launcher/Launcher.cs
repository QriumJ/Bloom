using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.Ui;

/// The launcher: one search line, a list of sections, a footer with the primary action and the
/// action panel (Ctrl+K). Root, file search, browser, AI threads, chat and settings are views of
/// the same window. Opening and list navigation are never animated (100+ times a day).
internal sealed partial class Launcher : Window, IChatHost
{
    private readonly Host host;
    private Engine Engine => host.Engine;

    // chrome
    private readonly Border frame;
    private readonly Grid layout;
    private readonly ContentControl lead = new() { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
    private readonly SmoothBorder chip = new();
    private readonly TextBlock chipText;
    private readonly TextBox box;
    private readonly TextBlock placeholder;
    private readonly StackPanel hint = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly WorkLine work = new();

    // body
    private readonly Grid body = new();
    private readonly ScrollViewer scroll;
    private readonly StackPanel list = new() { Margin = new Thickness(0, 4, 0, 8) };
    private readonly ChatView chat;
    private readonly PreviewPane preview = new() { Visibility = Visibility.Collapsed };
    private readonly MonthView month = new() { Visibility = Visibility.Collapsed, BorderBrush = Theme.P.Line, BorderThickness = new Thickness(0, 0, 1, 0) };
    private DateTime calDay = DateTime.Today;
    private string fileKind = "all"; // file search: Engine.FileKinds
    private readonly Border previewFrame;
    private string? previewId;
    private FormSpec? form;
    private string? groupId; // Mode.Group: which favorites folder
    private FavStrip? strip; // the favorites row in the empty view
    private bool inStrip;
    private int stripSel;
    private readonly ActionPanel actions;
    private readonly Border toast;
    private readonly TextBlock toastText;

    // footer
    private readonly TextBlock footContext;
    private readonly StackPanel footRight = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

    // state
    public Mode Mode { get; private set; } = Mode.Root;
    private List<Section> sections = new();
    private readonly List<Row> rows = new();
    private int sel = -1;
    private int version;
    private bool suppressText;
    private ChatThread? thread;
    private (string title, string url, string text)? attachment;
    private string? attachedImage; // Bloom Lens: the picture the next message takes along

    public string? AttachedImage => attachedImage;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly DispatcherTimer chatTick = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool chatDirty;
    public bool Capturing;
    public bool AllowClose;

    public Launcher(Host host)
    {
        this.host = host;
        Title = "Bloom";
        Width = Settings.Current.Width;
        Height = Settings.Current.Height;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });

        layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        frame = new SmoothBorder { Child = layout };
        Content = frame;
        Theme.Scaled(this, frame);
        Theme.ScaleChanged += (_, s) =>
        {
            if (!IsVisible) return;
            Place();
            host.Tools.Layout();
            ShowToast(Tr($"크기 {s * 100:0}%", $"Size {s * 100:0}%"));
        };

        // ── search line ──
        var barHandle = HandleUnder(); // the row's side margins drag too
        layout.Children.Add(barHandle);
        MakeHandle(barHandle);
        var bar = new Grid { Margin = new Thickness(20, 0, 16, 0), Background = Brushes.Transparent };
        MakeHandle(bar);
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lead.Margin = new Thickness(0, 0, 12, 0);
        bar.Children.Add(lead);
        chipText = Theme.T("", 12.5, Theme.P.TextDim, Theme.Medium, false);
        chip.Child = chipText;
        chip.Padding = new Thickness(8, 2, 8, 3);
        chip.CornerRadius = new CornerRadius(6);
        chip.Background = Theme.P.Tile;
        chip.Margin = new Thickness(0, 0, 10, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        chip.Visibility = Visibility.Collapsed;
        Grid.SetColumn(chip, 1);
        bar.Children.Add(chip);
        box = new TextBox
        {
            FontFamily = Theme.Sans,
            FontSize = 19,
            Foreground = Theme.P.Text,
            CaretBrush = Theme.P.Accent,
            SelectionBrush = Theme.P.Selection,
            SelectionOpacity = 1,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(-2, 0, 0, 1),
        };
        box.Resources[SystemParameters.VerticalScrollBarWidthKey] = 0.0;
        box.Template = BareTextBox();
        InputMethod.SetPreferredImeState(box, InputMethodState.DoNotCare);
        placeholder = Theme.T("", 19, Theme.P.TextFaint);
        placeholder.IsHitTestVisible = false;
        placeholder.Margin = new Thickness(0, 0, 0, 1);
        var boxHost = new Grid();
        boxHost.Children.Add(placeholder);
        boxHost.Children.Add(box);
        Grid.SetColumn(boxHost, 2);
        bar.Children.Add(boxHost);
        Grid.SetColumn(hint, 3);
        bar.Children.Add(hint);
        var corner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        // the bar's right end: 도구 붙이기 outside the quick AI (Ctrl+J's menu), 📎 in it, ⚙ 설정 always; 📌 only while the
        // launcher is pinned (Ctrl+P), a click lets it go
        toolsButton = BarButton(G.NewWindow, Tr("도구 붙이기 (Ctrl+J)", "Attach tools (Ctrl+J)"), OpenTools);
        corner.Children.Add(toolsButton);
        attachButton = BarButton(G.Attach, Tr("파일 · 사진 첨부 — Ctrl+O, 붙여넣기나 끌어 놓기도 돼요", "Attach files or pictures — Ctrl+O, pasting or dropping works too"), PickFiles);
        attachButton.Visibility = Visibility.Collapsed;
        corner.Children.Add(attachButton);
        corner.Children.Add(BarButton(G.Settings, Tr("설정 (Ctrl+,)", "Settings (Ctrl+,)"), host.OpenSettings));
        pinButton = BarButton('', Tr("화면에 고정 (Ctrl+P) — 다른 곳을 눌러도 남아요 · 검색줄이나 아래 바를 끌면 옮겨져요", "Keep on screen (Ctrl+P) — stays when you click elsewhere · drag the search line or the bottom bar to move it"), () => SetPinned(!Pinned));
        pinButton.Visibility = Visibility.Collapsed;
        corner.Children.Add(pinButton);
        Grid.SetColumn(corner, 4);
        bar.Children.Add(corner);
        layout.Children.Add(bar);

        var line = new Grid();
        line.Children.Add(new SmoothBorder { Background = Theme.P.Line });
        line.Children.Add(work);
        Grid.SetRow(line, 1);
        layout.Children.Add(line);

        // ── body ──
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list, Focusable = false, CanContentScroll = false };
        scroll.Template = ThinScroll();
        // file search and clipboard history split: the list on the left, the preview on the right
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        body.Children.Add(scroll);
        // the calendar: the month on the left, the picked day's list on the right
        body.Children.Add(month);
        month.DayClicked = d => CalendarGo(d);
        month.MonthStepped = n => CalendarGo(calDay.AddMonths(n));
        month.TodayClicked = () => CalendarGo(DateTime.Today);
        previewFrame = new SmoothBorder { Child = preview, BorderBrush = Theme.P.Line, BorderThickness = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed };
        Grid.SetColumn(previewFrame, 1);
        body.Children.Add(previewFrame);
        chat = new ChatView(host, this) { Visibility = Visibility.Collapsed };
        Grid.SetColumnSpan(chat, 2);
        body.Children.Add(chat);
        actions = new ActionPanel(this) { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 12, 10) };
        Grid.SetColumnSpan(actions, 2);
        body.Children.Add(actions);
        toastText = Theme.T("", 12.5, Theme.P.Text, Theme.Medium, false);
        toast = new SmoothBorder
        {
            Child = toastText,
            Background = Theme.P.Popup,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        Grid.SetColumnSpan(toast, 2);
        body.Children.Add(toast);
        Grid.SetRow(body, 2);
        layout.Children.Add(body);

        var footLine = new SmoothBorder { Background = Theme.P.Line };
        Grid.SetRow(footLine, 3);
        layout.Children.Add(footLine);
        var foot = new Grid { Margin = new Thickness(16, 0, 14, 0) };
        var leftSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        leftSp.Children.Add(MarkImage(15));
        footContext = Theme.T("Bloom", 12, Theme.P.TextFaint, Theme.Medium);
        footContext.Margin = new Thickness(8, 0, 0, 0);
        leftSp.Children.Add(footContext);
        foot.Children.Add(leftSp);
        foot.Children.Add(footRight);
        Grid.SetRow(foot, 4);
        layout.Children.Add(foot);
        // the footer and the search line are the handles (Launcher.Move.cs)
        foot.Background = Brushes.Transparent;
        MakeHandle(foot);
        WireBoxDrag();
        LocationChanged += (_, _) =>
        {
            if (IsVisible) host.Tools.Layout(); // the attached tools follow while it is dragged
        };

        WireAttachments();
        box.TextChanged += (_, _) => OnText();
        box.PreviewKeyDown += OnBoxKey;
        PreviewKeyDown += OnKey;
        PreviewKeyUp += (_, e) =>
        {
            if (Mode == Mode.Form && form?.Current.Keys == true) FormKeys(e, up: true); // PrintScreen arrives going up
        };
        Deactivated += (_, _) => OnDeactivated();
        Activated += (_, _) =>
        {
            Topmost = true;
            host.Tools.SetTopmost(true); // the group comes forward together
        };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            _ = Slow();
        };
        chatTick.Tick += (_, _) =>
        {
            if (!chatDirty || thread is null) return;
            chatDirty = false;
            chat.Update(thread);
            UpdateChrome();
        };
        trim.Tick += (_, _) => Trim();
        Theme.Changed += ApplyTheme;
        // Alt+F4 hides the launcher; only Quit really closes it
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            Dismiss();
        };
        SourceInitialized += (_, _) =>
        {
            Glass.ToolWindow(this);
            ApplyTheme();
        };
        SetMode(Mode.Root);
    }

    // ── helpers for building the chrome ─────────────────────────────────────────────────────────
    public static ControlTemplate BareTextBox()
    {
        var t = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(FrameworkElement.FocusVisualStyleProperty, null);
        t.VisualTree = host;
        return t;
    }

    /// A scroll viewer with a thin thumb over the content (no track, no arrows).
    private static ControlTemplate? thinScroll;

    public static ControlTemplate ThinScroll() => thinScroll ??= (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollViewer">
          <Grid>
            <ScrollContentPresenter x:Name="PART_ScrollContentPresenter" CanContentScroll="{TemplateBinding CanContentScroll}"/>
            <ScrollBar x:Name="PART_VerticalScrollBar" HorizontalAlignment="Right" Width="4" MinWidth="0" Margin="0,6,2,6" Cursor="Arrow"
                       Maximum="{TemplateBinding ScrollableHeight}" ViewportSize="{TemplateBinding ViewportHeight}"
                       Value="{Binding VerticalOffset, Mode=OneWay, RelativeSource={RelativeSource TemplatedParent}}"
                       Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}">
              <ScrollBar.Template>
                <ControlTemplate TargetType="ScrollBar">
                  <Track x:Name="PART_Track" IsDirectionReversed="True">
                    <Track.Thumb>
                      <Thumb>
                        <Thumb.Template>
                          <ControlTemplate TargetType="Thumb">
                            <Border CornerRadius="2" Background="{DynamicResource AL.Thumb}"/>
                          </ControlTemplate>
                        </Thumb.Template>
                      </Thumb>
                    </Track.Thumb>
                  </Track>
                </ControlTemplate>
              </ScrollBar.Template>
            </ScrollBar>
          </Grid>
        </ControlTemplate>
        """);

    public static Image MarkImage(int size)
    {
        int px = size * 3;
        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(Theme.MarkVisual(px));
        rtb.Freeze();
        var img = new Image { Source = rtb, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    private void ApplyTheme()
    {
        frame.Background = Theme.Acrylic && !Capturing ? Theme.P.Tint : Theme.P.Solid;
        if (IsLoaded || new WindowInteropHelper(this).Handle != IntPtr.Zero)
            Glass.Apply(this, Theme.P.Dark, Theme.Acrylic && !Capturing, Theme.P.Dark ? Color.FromRgb(0x35, 0x2E, 0x31) : Color.FromRgb(0xD9, 0xD2, 0xD5));
        if (Mode == Mode.Chat && thread is not null) chat.Show(thread);
        else Render(sections, Selected?.Id);
        UpdateChrome();
    }

    // ── show / hide ─────────────────────────────────────────────────────────────────────────────
    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public void Summon()
    {
        if (IsVisible && IsActive)
        {
            Dismiss();
            return;
        }
        bool wasVisible = IsVisible;
        if (!wasVisible)
        {
            // opened again: a fresh search line and home, except the quick AI, which carries on where it was
            if (Mode is not Mode.Chat) Go(Mode.Root, "");
            Place();
            new WindowInteropHelper(this).EnsureHandle();
            Cloak(true);
            Show();
            host.Tools.LauncherShown(); // the attached tools come with it
        }
        Activate();
        Win32.ForceForeground(Handle);
        FocusBox();
        if (!wasVisible) UncloakAfterFrame();
    }

    /// The window opens under a DWM cloak and appears once WPF has drawn the new frame. Uncloaked,
    /// the first frame on screen was the one from before the last hide, over the plain backdrop of a
    /// not-yet-active window (the acrylic only draws while active): a blink on every open.
    private void Cloak(bool on)
    {
        if (Handle == IntPtr.Zero) return;
        int v = on ? 1 : 0;
        int hr = Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_CLOAK, ref v, 4);
        if (hr != 0 && !cloakLogged)
        {
            cloakLogged = true;
            Log.Write($"cloak {(on ? "on" : "off")} failed 0x{hr:X8}");
        }
    }

    private bool cloakLogged;

    private void UncloakAfterFrame()
    {
        int frames = 0;
        bool done = false;
        var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; // never stays hidden
        EventHandler? onFrame = null;
        void Done()
        {
            if (done) return;
            done = true;
            CompositionTarget.Rendering -= onFrame;
            guard.Stop();
            Cloak(false);
        }
        onFrame = (_, _) =>
        {
            if (++frames >= 2) Done(); // the frame after the first one: the new content is presented
        };
        guard.Tick += (_, _) => Done();
        CompositionTarget.Rendering += onFrame;
        guard.Start();
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        actions.Close();
        SetPinned(false);
        Hide();
        KeysField();
        host.Tools.LauncherHidden();
        ShowPreview(null); // a playing preview stops with the window
        trim.Stop();
        trim.Start();
    }

    /// A minute after hiding, give memory back (the launcher mostly waits in the tray): the working set drops from
    /// ~150 MB to ~10 MB (measured); the next open pages back what it touches.
    private readonly DispatcherTimer trim = new(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMinutes(1) };

    private void Trim()
    {
        trim.Stop();
        if (IsVisible) return;
        Core.MemoryTrim.Now();
    }

    /// A minute from now, while the launcher stays hidden (another window of Bloom's was closed).
    public void TrimSoon()
    {
        if (IsVisible) return;
        trim.Stop();
        trim.Start();
    }

    private void OnDeactivated()
    {
        if (Capturing || !IsVisible) return;
        // after the focus has landed: in an attached tool the group stays, elsewhere it lets go
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible || IsActive) return;
            if (host.Tools.Owns(Win32.GetForegroundWindow())) return;
            GroupLeft();
        }, DispatcherPriority.Input);
    }

    /// The focus went outside the launcher and its attached tools: a conversation or a pinned launcher stays (behind
    /// other windows, the Win key brings it forward); otherwise everything hides.
    public void GroupLeft()
    {
        if (Mode == Mode.Chat || Pinned)
        {
            Topmost = false;
            host.Tools.SetTopmost(false);
            return;
        }
        Dismiss();
    }

    /// 📌: stays on screen when something else takes the focus (Ctrl+P, or by itself while you work in an attached
    /// tool); Esc still hides it, and hiding lets the pin go.
    public bool Pinned { get; private set; }

    private readonly SmoothBorder pinButton, attachButton, toolsButton;

    public void SetPinned(bool on)
    {
        Pinned = on;
        var g = (TextBlock)pinButton.Child;
        g.Text = on ? "" : "";
        g.Foreground = on ? Theme.P.Accent : Theme.P.TextFaint;
        pinButton.Background = on ? Theme.P.AccentSoft : Brushes.Transparent;
        pinButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    /// The 도구 menu (Ctrl+J, the bar button): each tool opens beside the launcher, or closes; asked again while it is
    /// open, the menu closes.
    public void OpenTools()
    {
        if (!Features.On("notes")) return; // switched off in 설정 > 기능
        if (actions.IsOpen && actions.Heading == Tr("도구", "Tools"))
        {
            actions.Close();
            return;
        }
        var items = ToolDock.Kinds.Select(k => new ActionItem
        {
            Title = host.Tools.IsOpen(k.key) ? Tr($"{k.name} 닫기", $"Close {k.name}") : Tr($"{k.name} 붙이기", $"Attach {k.name}"),
            Glyph = k.glyph,
            StayOpen = true,
            Run = () => host.Tools.Toggle(k.key),
        }).ToList();
        actions.Open(Tr("도구", "Tools"), items);
    }

    private SmoothBorder BarButton(char glyph, string tip, Action click)
    {
        var b = new SmoothBorder { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, Child = Theme.Glyph(glyph, 13, Theme.P.TextFaint), Margin = new Thickness(2, 0, 0, 0) };
        b.MouseEnter += (_, _) =>
        {
            if (b != pinButton || !Pinned) b.Background = Theme.P.Hover;
        };
        b.MouseLeave += (_, _) =>
        {
            if (b != pinButton || !Pinned) b.Background = Brushes.Transparent;
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    public void FocusBox()
    {
        box.Focus();
        Keyboard.Focus(box);
    }

    /// Where the launcher was dragged to (physical pixels), kept until Bloom restarts or a handle is double-clicked.
    private Win32.POINT? movedTo;

    /// A click on something in `root` that takes clicks itself (a hand cursor on it or above it).
    private static bool OnButton(DependencyObject? d, DependencyObject root)
    {
        for (; d is not null && d != root; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { Cursor: { } c } && c == Cursors.Hand) return true;
        return false;
    }

    /// Where it was dragged to, while that is on a screen; else centred on the monitor under the mouse, a little above
    /// the middle.
    public void Place(int? displayIndex = null)
    {
        if (displayIndex is null && movedTo is { } at && Handle != IntPtr.Zero
            && Win32.MonitorFromPoint(new Win32.POINT { X = at.X + 40, Y = at.Y + 20 }, 0 /* MONITOR_DEFAULTTONULL */) != IntPtr.Zero)
        {
            uint dpiHere = Win32.GetDpiForWindow(Handle) is > 0 and var dw ? dw : 96;
            Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, at.X, at.Y, (int)Math.Round(Width * dpiHere / 96.0), (int)Math.Round(Height * dpiHere / 96.0), Win32.SWP_NOACTIVATE);
            return;
        }
        IntPtr mon;
        if (displayIndex is { } d)
        {
            var mons = Monitors();
            mon = d - 1 < mons.Count && d >= 1 ? mons[d - 1] : mons.FirstOrDefault();
        }
        else
        {
            Win32.GetCursorPos(out var p);
            mon = Win32.MonitorFromPoint(p, Win32.MONITOR_DEFAULTTONEAREST);
        }
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(mon, ref info)) return;
        if (Win32.GetDpiForMonitor(mon, 0, out uint dpi, out _) != 0 || dpi == 0) dpi = 96; // failed: 100 %, never a zero-size window
        double scale = dpi / 96.0;
        int w = (int)Math.Round(Width * scale), h = (int)Math.Round(Height * scale);
        var wa = info.rcWork;
        int x = wa.Left + (wa.Right - wa.Left - w) / 2;
        int y = wa.Top + (int)((wa.Bottom - wa.Top - h) * 0.3);
        if (Handle == IntPtr.Zero)
        {
            Left = x / scale;
            Top = y / scale;
            return;
        }
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE);
    }

    /// Monitors with the primary first (so --display=2 is the other one, as in the docks' QA flags).
    public static List<IntPtr> Monitors()
    {
        var mons = new List<IntPtr>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr hdc, ref Win32.RECT r, IntPtr data) =>
        {
            mons.Add(m);
            return true;
        }, IntPtr.Zero);
        return mons.OrderByDescending(m =>
        {
            var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
            return Win32.GetMonitorInfo(m, ref info) && (info.dwFlags & 1) != 0;
        }).ToList();
    }

    /// Builds the window once off-screen so the first Win key press shows it instantly.
    public void Prewarm()
    {
        Left = -32000;
        Top = -32000;
        ShowActivated = false;
        Show();
        UpdateLayout();
        Hide();
        ShowActivated = true;
        trim.Start(); // the start-up allocations are given back too, not only after the first use
    }

    public void ShowToast(string text)
    {
        toastText.Text = text;
        toast.Visibility = Visibility.Visible;
        toast.Opacity = 1;
        // without fades it simply goes after the same time
        var fade = Motion.Paced(new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(Motion.Fades ? 220 : 0)) { BeginTime = TimeSpan.FromMilliseconds(Motion.Fades ? 1300 : 1500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        fade.Completed += (_, _) =>
        {
            if (toast.Opacity == 0) toast.Visibility = Visibility.Collapsed;
        };
        toast.BeginAnimation(OpacityProperty, fade);
    }

    // ── QA hooks (Captures) ─────────────────────────────────────────────────────────────────────
    public void MoveForQa(int delta) => Move(delta);

    public FavStrip? StripForQa => strip;

    public void FormNextForQa() => FormNext();

    public void SetTextForQa(string text)
    {
        box.Text = text;
        box.CaretIndex = text.Length;
    }

    public void Capture(string file) => Shots.Save(frame, file);

    /// QA: what a group picture draws of the launcher.
    public FrameworkElement FrameForQa => frame;
}
