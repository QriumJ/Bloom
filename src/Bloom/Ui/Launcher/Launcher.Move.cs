using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Native;

namespace Bloom.Ui;

/// Moving the launcher: the search line and the footer are its handles. Dragged, it moves (its attached tools with it)
/// and stays there; a double click puts it back in the middle. In the search box itself only the part past the text
/// drags (on the text, a click places the caret and a drag selects), and only once the mouse really moves.
internal sealed partial class Launcher
{
    private Point? boxPress; // a press on the empty part of the search box, until it moves (drag) or lets go (click)

    private void MakeHandle(FrameworkElement el)
    {
        el.MouseLeftButtonDown += (_, e) =>
        {
            if (OnButton(e.OriginalSource as DependencyObject, el)) return;
            e.Handled = true;
            if (e.ClickCount == 2) Recenter();
            else DragLauncher();
        };
    }

    private void WireBoxDrag()
    {
        box.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(box);
            if (box.GetCharacterIndexFromPoint(p, false) >= 0) return; // on the text: the box's own
            boxPress = p;
            e.Handled = true;
        };
        box.PreviewMouseMove += (_, e) =>
        {
            if (boxPress is not { } start) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                boxPress = null;
                return;
            }
            var d = e.GetPosition(box) - start;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            boxPress = null;
            DragLauncher();
        };
        box.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (boxPress is null) return;
            boxPress = null; // a click past the text: the caret goes to the end, as the box would put it
            FocusBox();
            box.CaretIndex = box.Text.Length;
            e.Handled = true;
        };
    }

    private void DragLauncher()
    {
        DragMove();
        Win32.GetWindowRect(Handle, out var r);
        movedTo = new Win32.POINT { X = r.Left, Y = r.Top };
        host.Tools.Layout();
        FocusBox();
    }

    private void Recenter()
    {
        movedTo = null;
        Place();
        host.Tools.Layout();
    }

    /// The search line's own row (its side margins included) as a handle under the bar.
    private static Border HandleUnder() => new() { Background = Brushes.Transparent };
}
