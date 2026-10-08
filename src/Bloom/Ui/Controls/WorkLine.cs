using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Bloom.Ui;

/// The working line under the search field: one accent segment sweeping while something runs.
internal sealed class WorkLine : Canvas
{
    private readonly Rectangle seg;
    private readonly TranslateTransform move = new();
    private bool on;

    public WorkLine()
    {
        Height = 2;
        ClipToBounds = true;
        IsHitTestVisible = false;
        seg = new Rectangle { Width = 180, Height = 2, RenderTransform = move, Visibility = Visibility.Collapsed };
        Children.Add(seg);
        SizeChanged += (_, _) =>
        {
            if (on) Run();
        };
    }

    public bool Working
    {
        get => on;
        set
        {
            if (on == value) return;
            on = value;
            if (on) Run();
            else
            {
                move.BeginAnimation(TranslateTransform.XProperty, null);
                seg.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Run()
    {
        // the accent of the current theme (rose differs between dark and light)
        var c = Theme.P.Accent.Color;
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0));
        brush.GradientStops.Add(new GradientStop(c, 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        seg.Fill = brush;
        seg.Visibility = Visibility.Visible;
        double w = Math.Max(ActualWidth, 400);
        if (!Motion.Moves)
        {
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.X = (w - seg.Width) / 2; // a still line in the middle
            return;
        }
        var a = Motion.Paced(new DoubleAnimation(-seg.Width, w, TimeSpan.FromMilliseconds(1300)) { RepeatBehavior = RepeatBehavior.Forever });
        move.BeginAnimation(TranslateTransform.XProperty, a);
    }
}
