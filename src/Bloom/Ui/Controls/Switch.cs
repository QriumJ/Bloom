using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Bloom.Ui;

/// On/off switch (settings).
internal sealed class Switch : SmoothBorder
{
    private readonly Ellipse knob;
    private bool on;

    public Switch(bool value)
    {
        Width = 32;
        Height = 18;
        CornerRadius = new CornerRadius(9); // half the height: a true pill in WPF
        knob = new Ellipse { Width = 12, Height = 12, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
        Child = knob;
        VerticalAlignment = VerticalAlignment.Center;
        On = value;
    }

    public bool On
    {
        get => on;
        set
        {
            on = value;
            // a rose fill with the rose ink on it
            Background = on ? Theme.P.Accent : Theme.P.Track;
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            knob.Fill = on ? Theme.P.AccentInk : Theme.P.TextDim;
        }
    }
}
