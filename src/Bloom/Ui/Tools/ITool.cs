using System.Windows;

namespace Bloom.Ui;

/// What one Bloom tool (메모 · 타이머 · 계산기 · 클립보드) puts in its ToolWindow.
internal interface ITool
{
    string Key { get; }
    string Name { get; }
    char Glyph { get; }

    /// The body under the window's head.
    FrameworkElement View { get; }

    /// The tool's own buttons in the head (left of the window's), or null.
    FrameworkElement? Head { get; }

    /// What the next ToolPlace keeps of it (메모: the note it shows), or null.
    string? State { get; }

    /// The keyboard goes to its first field.
    void FocusFirst();

    /// The window closed: stop timers, let go of events.
    void Closed();
}
