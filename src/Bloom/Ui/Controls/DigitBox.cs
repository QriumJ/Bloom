using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloom.Ui;

/// A bare field that takes digits only, typed or pasted, with the IME off (a Korean IME would hold them in
/// composition): 타이머 · 알림's units and the number fields in 설정.
internal static class DigitBox
{
    public static TextBox Make(string text, int maxLength, double size, FontWeight weight, double minWidth)
    {
        var box = new TextBox
        {
            Text = text,
            MaxLength = maxLength,
            MinWidth = minWidth,
            FontFamily = Theme.Sans,
            FontSize = size,
            FontWeight = weight,
            Foreground = Theme.P.Text,
            CaretBrush = Theme.P.Accent,
            SelectionBrush = Theme.P.Selection,
            SelectionOpacity = 1,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Template = Launcher.BareTextBox(),
        };
        InputMethod.SetIsInputMethodEnabled(box, false);
        box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);
        DataObject.AddPastingHandler(box, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.UnicodeText) is not string t || !t.Trim().All(char.IsAsciiDigit)) e.CancelCommand();
        });
        return box;
    }
}
