namespace Bloom.Core;

/// An open tool window (메모 · 타이머 · 계산기 · 클립보드) as it was left: attached to the launcher (laid out along an
/// edge, or where it was put beside it), or floating where it was put (physical pixels), on top or not.
internal sealed class ToolPlace
{
    public string Key { get; set; } = "";
    public string? Edge { get; set; } // right | left | bottom: laid out along that edge; free: X, Y, W, H in dip at 100 % from the launcher's top-left; null = floating
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public bool OnTop { get; set; } // floating: above other windows (a sticky note); off until switched on
    public string? Note { get; set; } // 메모: the note it shows
}
