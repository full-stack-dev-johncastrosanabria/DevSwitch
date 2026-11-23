# Draws the DevSwitch icon (gradient tile + power symbol + "on" dot) at every Windows icon size
# and packs the PNGs into Assets\DevSwitch.ico. Re-run after changing the design.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-IconPng([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([Drawing.Color]::Transparent)

    # Tile: diagonal blue -> violet gradient with a soft top highlight.
    $m = [Math]::Max(1, $s * 0.04)
    $tile = New-RoundedPath $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.22)
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.PointF]::new(0, 0)), ([Drawing.PointF]::new($s, $s)),
        ([Drawing.Color]::FromArgb(255, 37, 99, 235)), ([Drawing.Color]::FromArgb(255, 124, 58, 237))
    $g.FillPath($grad, $tile)
    # The brush repeats outside its rectangle, so paint only that rectangle, clipped to the tile.
    $shineRect = [Drawing.RectangleF]::new(0, 0, $s, $s * 0.6)
    $shine = New-Object Drawing.Drawing2D.LinearGradientBrush $shineRect,
        ([Drawing.Color]::FromArgb(60, 255, 255, 255)), ([Drawing.Color]::FromArgb(0, 255, 255, 255)), 90
    $g.SetClip($tile)
    $g.FillRectangle($shine, 0, 0, $s, [float]($s * 0.6) - 1)
    $g.ResetClip()

    # Power symbol: open ring + vertical bar, white with round caps.
    $stroke = [Math]::Max(1.6, $s * 0.085)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), $stroke
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $c = $s * 0.5; $r = $s * 0.24; $cy = $s * 0.53
    $g.DrawArc($pen, $c - $r, $cy - $r, 2 * $r, 2 * $r, -55, 290)
    $g.DrawLine($pen, $c, $cy - $r - $stroke * 0.35, $c, $cy - $r * 0.05)

    # Green "on" dot, bottom-right, from 32 px up (too small to read below that).
    if ($s -ge 32) {
        $dr = $s * 0.11; $dx = $s * 0.78; $dy = $s * 0.78
        $ring = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
        $g.FillEllipse($ring, $dx - $dr * 1.35, $dy - $dr * 1.35, $dr * 2.7, $dr * 2.7)
        $dot = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 34, 197, 94))
        $g.FillEllipse($dot, $dx - $dr, $dy - $dr, $dr * 2, $dr * 2)
    }

    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save((Join-Path $dir 'DevSwitch-256.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $g.Dispose(); $bmp.Dispose()
    , $ms.ToArray()
}

$images = foreach ($s in $sizes) { , (New-IconPng $s) }

# ICO container: 6-byte header, 16-byte directory entry per image, then the PNG payloads.
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256))   # 256 is stored as 0
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $dir 'DevSwitch.ico'), $out.ToArray())
"DevSwitch.ico: $($sizes.Count) sizes, $($out.Length) bytes"
