using System.Windows;
using System.Windows.Media.Animation;
using Bloom.Core;

namespace Bloom.Ui;

/// How much Bloom moves (설정 > 성능): `system` follows Windows' animation effects switch, `on`, `reduced` (fades
/// only, nothing slides or grows), `off` (nothing animates); and how many frames a second an animation draws. Every
/// animation is made through Paced, and checks Moves / Fades first, so the settings reach all of them.
internal static class Motion
{
    /// Things may slide, grow and sweep.
    public static bool Moves => Settings.Current.Animation switch
    {
        "on" => true,
        "reduced" or "off" => false,
        _ => SystemParameters.ClientAreaAnimation,
    };

    /// Things may fade in and out (everything but `off`).
    public static bool Fades => Settings.Current.Animation != "off";

    /// The frame rate picked in 성능 on this animation (0 = the screen's own).
    public static T Paced<T>(T animation) where T : Timeline
    {
        if (Settings.Current.AnimationFps > 0) Timeline.SetDesiredFrameRate(animation, Settings.Current.AnimationFps);
        return animation;
    }
}
