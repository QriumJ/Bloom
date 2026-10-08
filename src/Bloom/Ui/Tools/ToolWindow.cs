using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.Ui;

/// A Bloom tool in a small window of its own. Dragged by its head to an edge of the launcher it snaps on (ToolDock):
/// then it moves, shows and hides with the launcher. Dragged away, it floats on its own and stays when something
/// else takes the focus, on top of other windows or not (its 항상 위 button).
internal sealed class ToolWindow : Window
{
    public readonly ITool Tool;
    private readonly ToolDock dock;
    private readonly SmoothBorder frame;
    private readonly SmoothBorder attachButton, topButton;
    private readonly WindowChrome chrome;

    /// right | left | bottom while attached to the launcher; null floating.
    public string? Edge { get; private set; }

    /// Floating: above other windows.
    public bool OnTop { get; private set; } = true;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// QA: what a group picture draws of it.
    public FrameworkElement FrameForQa => frame;

    public ToolWindow(ToolDock dock, ITool tool, string? edge, bool onTop)
    {
        this.dock = dock;
        Tool = tool;
        OnTop = onTop;
        Title = "Bloom " + tool.Name;
        Width = 300;
        Height = 360;
        MinWidth = 160;
        MinHeight = 120;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        chrome = new WindowChrome { CaptionHeight = 40, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false };
        WindowChrome.SetWindowChrome(this, chrome);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // head: the tool's glyph and name (the handle to drag it by), its own buttons, attach, on top, close
        var head = new Grid { Margin = new Thickness(14, 0, 6, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var glyph = Theme.Glyph(tool.Glyph, 13, Theme.P.Accent);
        glyph.Margin = new Thickness(0, 0, 8, 0);
        name.Children.Add(glyph);
        name.Children.Add(Theme.T(tool.Name, 13, Theme.P.Text, Theme.Semi));
        head.Children.Add(name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (tool.Head is { } own)
        {
            WindowChrome.SetIsHitTestVisibleInChrome(own, true);
            buttons.Children.Add(own);
        }
        attachButton = HeadButton('', Tr("런처에서 떼기", "Detach from the launcher"), () => dock.ToggleAttach(this));
        topButton = HeadButton('', Tr("항상 위", "Always on top"), () => SetOnTop(!OnTop));
        buttons.Children.Add(attachButton);
        buttons.Children.Add(topButton);
        buttons.Children.Add(HeadButton(G.Close, Tr("닫기", "Close"), () => dock.Close(this)));
        Grid.SetColumn(buttons, 1);
        head.Children.Add(buttons);
        grid.Children.Add(head);

        var line = new SmoothBorder { Background = Theme.P.Line };
        Grid.SetRow(line, 1);
        grid.Children.Add(line);
        Grid.SetRow(tool.View, 2);
        grid.Children.Add(tool.View);

        frame = new SmoothBorder { Child = grid };
        Content = frame;
        frame.LayoutTransform = Theme.Zoom; // its size follows the scale through ToolDock (attached ones from the launcher)
        SetEdge(edge);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = dock.Escape(this);
        };
        Activated += (_, _) => dock.Activated(this);
        Deactivated += (_, _) => dock.Deactivated(this);
        SourceInitialized += (_, _) =>
        {
            Glass.ToolWindow(this);
            Paint();
            HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
        };
        Theme.Changed += Paint;
        Closed += (_, _) =>
        {
            Theme.Changed -= Paint;
            Tool.Closed();
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_MOVING)
        {
            // while dragged: the edges pull to the edges nearby (other tools, the launcher, the screen)
            var r = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.RECT>(lParam);
            if (dock.Magnet(this, ref r))
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(r, lParam, false);
                handled = true;
                return new IntPtr(1);
            }
        }
        if (msg == Win32.WM_EXITSIZEMOVE) Dispatcher.BeginInvoke(() => dock.Moved(this)); // dropped: snap on or float
        return IntPtr.Zero;
    }

    /// Attached (an edge) or floating (null): the buttons and the frame follow.
    public void SetEdge(string? edge)
    {
        Edge = edge;
        // attached, its size comes from the launcher; floating, its edges resize it
        chrome.ResizeBorderThickness = new Thickness(edge is null ? 6 : 0);
        ((TextBlock)attachButton.Child).Text = edge is null ? "" : "";
        attachButton.ToolTip = edge is null ? Tr("런처에 붙이기", "Attach to the launcher") : Tr("런처에서 떼기", "Detach from the launcher");
        topButton.Visibility = edge is null ? Visibility.Visible : Visibility.Collapsed;
        Paint();
    }

    public void SetOnTop(bool on)
    {
        OnTop = on;
        if (Edge is null) Topmost = on;
        Paint();
        dock.Save();
    }

    private void Paint()
    {
        frame.Background = Theme.Acrylic ? Theme.P.Tint : Theme.P.Solid;
        ((TextBlock)topButton.Child).Foreground = OnTop ? Theme.P.Accent : Theme.P.TextDim;
        if (Handle != IntPtr.Zero)
            Glass.Apply(this, Theme.P.Dark, Theme.Acrylic, Theme.P.Dark ? Color.FromRgb(0x35, 0x2E, 0x31) : Color.FromRgb(0xD9, 0xD2, 0xD5));
    }

    /// A tools' glyph button that still takes clicks inside the draggable head.
    private static SmoothBorder HeadButton(char glyph, string tip, Action click)
    {
        var b = ToolUi.Button(glyph, tip, click);
        b.Margin = new Thickness(2, 0, 0, 0);
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        return b;
    }

    /// The window's place on the screen, in physical pixels.
    public Win32.RECT Rect
    {
        get
        {
            Win32.GetWindowRect(Handle, out var r);
            return r;
        }
    }

    /// Puts the window at a place in physical pixels (attached: sized by the launcher).
    public void PlaceAt(int x, int y, int w, int h) =>
        Win32.SetWindowPos(Handle, Topmost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE);
}
