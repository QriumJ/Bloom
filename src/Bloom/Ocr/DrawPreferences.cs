using System;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Ocr;

/// Values copied on the caller thread; the drawing dispatcher never accesses mutable application settings.
internal sealed record DrawPreferences(string Tool, int PenColor, int MarkColor, int PenSize, int MarkSize)
{
    public static DrawPreferences Capture()
    {
        var s = Settings.Current;
        return new(s.DrawTool, s.DrawPenColor, s.DrawMarkColor, s.DrawPenSize, s.DrawMarkSize);
    }

    public void Save()
    {
        var s = Settings.Current;
        s.DrawTool = Tool;
        s.DrawPenColor = PenColor;
        s.DrawMarkColor = MarkColor;
        s.DrawPenSize = PenSize;
        s.DrawMarkSize = MarkSize;
        Settings.Save();
    }

    public static Action<DrawPreferences> PersistOn(Dispatcher owner, Action<DrawPreferences> save) => changed =>
    {
        if (!owner.HasShutdownStarted)
            owner.BeginInvoke(() => save(changed), DispatcherPriority.Normal);
    };
}

/// Color values, rather than the live palette's dispatcher-owned brushes, cross the thread boundary.
internal sealed record DrawTheme(Color Accent, Color AccentInk, Color AccentSoft)
{
    public static DrawTheme Capture() => new(Theme.P.Accent.Color, Theme.P.AccentInk.Color, Theme.P.AccentSoft.Color);

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
