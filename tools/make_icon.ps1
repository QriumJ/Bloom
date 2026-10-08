# Bloom's icon (src\Bloom\Assets\icon.ico): five white rounded petals around a rose centre on a
# dark rose-tinted tile — "한 줄이 피어난다", the old line mark opened into a
# flower — drawn with WPF at every icon size and packed as PNG entries.
# Same drawing as Ui\Theme.MarkVisual.   pwsh tools\make_icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\Bloom\Assets\icon.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $v = New-Object System.Windows.Media.DrawingVisual
    $dc = $v.RenderOpen()
    $bg = New-Object System.Windows.Media.LinearGradientBrush ([System.Windows.Media.Color]::FromRgb(0x2E, 0x25, 0x29)), ([System.Windows.Media.Color]::FromRgb(0x12, 0x0E, 0x10)), 90
    $dc.DrawRoundedRectangle($bg, $null, (New-Object System.Windows.Rect 0, 0, $s, $s), $s * 0.23, $s * 0.23)
    $white = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xF1, 0xEC, 0xEE))
    $rose = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xFF, 0x7A, 0x9E))
    $cx = $s / 2
    $cy = $s * 0.515
    $hw = [Math]::Max(1.1, $s * 0.085)
    $len = $s * 0.28
    $gap = $s * 0.06
    $yb = $cy - $gap
    $yt = $cy - $gap - $len
    # a petal: pointed base at the centre, round head outside (invariant numbers for the path syntax)
    $n = { param($x) ([double]$x).ToString('0.###', [Globalization.CultureInfo]::InvariantCulture) }
    $path = "M $(& $n $cx),$(& $n $yb) C $(& $n ($cx - $hw * 0.3)),$(& $n ($yb - $len * 0.3)) $(& $n ($cx - $hw)),$(& $n ($yt + $hw * 1.6)) $(& $n ($cx - $hw)),$(& $n ($yt + $hw)) " +
        "A $(& $n $hw),$(& $n $hw) 0 0 1 $(& $n ($cx + $hw)),$(& $n ($yt + $hw)) " +
        "C $(& $n ($cx + $hw)),$(& $n ($yt + $hw * 1.6)) $(& $n ($cx + $hw * 0.3)),$(& $n ($yb - $len * 0.3)) $(& $n $cx),$(& $n $yb) Z"
    $petal = [System.Windows.Media.Geometry]::Parse($path)
    for ($i = 0; $i -lt 5; $i++) {
        $dc.PushTransform((New-Object System.Windows.Media.RotateTransform ($i * 72), $cx, $cy))
        $dc.DrawGeometry($white, $null, $petal)
        $dc.Pop()
    }
    $r = [Math]::Max(1.5, $s * 0.062)
    $dc.DrawEllipse($rose, $null, (New-Object System.Windows.Point $cx, $cy), $r, $r)
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $s, $s, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($v)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    $pngs += , $ms.ToArray()
}
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
"icon: $out ($((Get-Item $out).Length) bytes, $($sizes.Count) sizes)"
