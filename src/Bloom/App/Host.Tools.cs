using System;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// Timers and reminders ringing, and the colour picker.
internal sealed partial class Host
{
    // ── timers, colours ─────────────────────────────────────────────────────────────────────────
    /// A timer or reminder came due: the card at the bottom right (5분 뒤 다시 puts it back).
    public static void Ring(TimerEntry e)
    {
        Log.Write($"timer due: {e.Kind}");
        new AlarmWindow(e, x => Timers.Add(x.Kind, x.Label, DateTime.Now.AddMinutes(5)), Settings.Current.TimerSound).Show();
    }

    /// The colour picker: the frozen screen with a magnifier; the colour picked goes to the recent ones (and its HEX
    /// to the clipboard), then the colour view opens.
    public async void StartColor()
    {
        if (colorBusy) return;
        colorBusy = true;
        try
        {
            Window.Dismiss();
            await Task.Delay(150);
            if (await Ocr.ColorPick.Pick() is not { } c) return;
            if (Settings.Current.ColorCopy) Launch.Copy(ColorBook.Hex(c));
            ShowColor(c);
            if (Settings.Current.ColorCopy) Window.ShowToast(Tr($"{ColorBook.Hex(c)} 복사 완료", $"Copied {ColorBook.Hex(c)}"));
        }
        catch (Exception e)
        {
            Log.Error("color", e);
        }
        finally
        {
            colorBusy = false;
        }
    }

    /// A colour becomes the current one (newest in the recent list) and the colour view shows it.
    public void ShowColor(System.Windows.Media.Color c)
    {
        ColorBook.Add(c);
        OpenView(Mode.Color);
    }
}
