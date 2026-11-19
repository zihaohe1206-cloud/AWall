$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Regenerates AWall.png and AWall.ico (same mark as draw_icon.py).
# Mark: rounded tile with an indigo -> cyan gradient, a white "A" peak with a
# wave running through it as the crossbar, and a small moon.
# Frames <= 32px use a simplified, thicker mark so the tray icon stays crisp.

function New-Mark([bool]$simple) {
    $bitmap = New-Object System.Drawing.Bitmap 1024,1024,([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $inset = if ($simple) { 40 } else { 64 }; $radius = if ($simple) { 250 } else { 232 }
    $d = 2 * $radius; $far = 1024 - $inset - $d
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc($inset,$inset,$d,$d,180,90); $tile.AddArc($far,$inset,$d,$d,270,90)
    $tile.AddArc($far,$far,$d,$d,0,90); $tile.AddArc($inset,$far,$d,$d,90,90); $tile.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0,0)),([System.Drawing.PointF]::new(614,1229)),([System.Drawing.Color]::FromArgb(124,97,255)),([System.Drawing.Color]::FromArgb(31,182,232))
    $g.FillPath($grad,$tile)
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0,0)),([System.Drawing.PointF]::new(0,512)),([System.Drawing.Color]::FromArgb(34,255,255,255)),([System.Drawing.Color]::FromArgb(0,255,255,255))
    $g.SetClip($tile); $g.FillRectangle($hl,0,0,1024,512); $g.ResetClip()

    $white = [System.Drawing.Color]::White
    $peakWidth = if ($simple) { 150 } else { 104 }
    $peakPen = New-Object System.Drawing.Pen $white,$peakWidth
    $peakPen.StartCap = 'Round'; $peakPen.EndCap = 'Round'; $peakPen.LineJoin = 'Round'
    if ($simple) { $peak = @(@(270,790),@(512,250),@(754,790)); $wave = @(300,724,610,36,104) }
    else { $peak = @(@(292,768),@(512,290),@(732,768)); $wave = @(232,792,600,44,70) }
    $g.DrawLines($peakPen,[System.Drawing.PointF[]]($peak | ForEach-Object { [System.Drawing.PointF]::new($_[0],$_[1]) }))
    $points = for ($i = 0; $i -le 64; $i++) {
        [System.Drawing.PointF]::new($wave[0] + ($wave[1] - $wave[0]) * $i / 64, $wave[2] - $wave[3] * [Math]::Sin(2 * [Math]::PI * $i / 64))
    }
    $wavePen = New-Object System.Drawing.Pen $white,$wave[4]
    $wavePen.StartCap = 'Round'; $wavePen.EndCap = 'Round'; $wavePen.LineJoin = 'Round'
    $g.DrawLines($wavePen,[System.Drawing.PointF[]]$points)
    if (-not $simple) {
        $moon = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235,255,255,255))
        $g.FillEllipse($moon,706,246,104,104); $moon.Dispose()
    }
    foreach ($r in @($g,$tile,$grad,$hl,$peakPen,$wavePen)) { $r.Dispose() }
    return $bitmap
}

function Resize-Mark($source,[int]$size) {
    $small = New-Object System.Drawing.Bitmap $size,$size,([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($small)
    $sg.InterpolationMode = 'HighQualityBicubic'; $sg.PixelOffsetMode = 'HighQuality'; $sg.CompositingQuality = 'HighQuality'
    $sg.DrawImage($source,0,0,$size,$size); $sg.Dispose()
    return $small
}

# 32bpp BGRA DIB + AND mask: the most compatible ICO frame format (tray, Explorer, csc).
function Get-DibBytes($bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0,0,$w,$h),'ReadOnly',([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $pixels = New-Object byte[] ($data.Stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0,$pixels,0,$pixels.Length); $bmp.UnlockBits($data)
    $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32]($h * 2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]0); $bw.Write([uint32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($pixels,$y * $data.Stride,$w * 4) }
    $bw.Write((New-Object byte[] ([Math]::Floor(($w + 31) / 32) * 4 * $h)))
    $bw.Flush(); $bytes = $ms.ToArray(); $bw.Dispose()
    return ,$bytes
}

$full = New-Mark $false; $simple = New-Mark $true
$full.Save((Join-Path $PSScriptRoot 'AWall.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$sizes = @(16,20,24,32,40,48,64,96,128,256)
$frames = @()
foreach ($size in $sizes) {
    $small = Resize-Mark $(if ($size -le 32) { $simple } else { $full }) $size
    if ($size -eq 256) { $ms = New-Object System.IO.MemoryStream; $small.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png); $frames += ,($ms.ToArray()); $ms.Dispose() }
    else { $frames += ,(Get-DibBytes $small) }
    $small.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $PSScriptRoot 'AWall.ico'))
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
$writer.Dispose(); $full.Dispose(); $simple.Dispose()
Write-Host "Wrote AWall.png and AWall.ico"
