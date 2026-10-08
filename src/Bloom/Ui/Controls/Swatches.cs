using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// Colour dots (설정 > 색): click or ←→ picks, Enter goes round; the picked one wears a ring.
internal sealed class Swatches : StackPanel
{
    private readonly List<(string value, Border ring)> items = new();
    public string Value { get; private set; }
    public event Action<string>? Picked;

    public Swatches(IEnumerable<(string value, string name, Color color)> options, string value)
    {
        Value = value;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        foreach (var (v, name, color) in options)
        {
            var dot = new SmoothBorder { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(color) };
            var ring = new SmoothBorder { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(2), Child = dot, Cursor = Cursors.Hand, ToolTip = name, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.Transparent };
            string vv = v;
            ring.MouseLeftButtonUp += (_, e) =>
            {
                Set(vv);
                Picked?.Invoke(vv);
                e.Handled = true;
            };
            items.Add((v, ring));
            Children.Add(ring);
        }
        Set(value);
    }

    public void Set(string v)
    {
        Value = v;
        foreach (var (value, ring) in items) ring.BorderBrush = value == v ? Theme.P.Text : Brushes.Transparent;
    }

    public string Step(int delta)
    {
        int i = items.FindIndex(x => x.value == Value);
        Set(items[Math.Clamp(i + delta, 0, items.Count - 1)].value);
        return Value;
    }

    public string Cycle()
    {
        int i = items.FindIndex(x => x.value == Value);
        Set(items[(i + 1) % items.Count].value);
        return Value;
    }
}
