$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Draw at high resolution so the desktop icon stays crisp at every Windows scale.
$bitmap = New-Object System.Drawing.Bitmap 1024,1024
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
$tile = New-Object System.Drawing.Drawing2D.GraphicsPath
$tile.AddArc(64,64,256,256,180,90)
$tile.AddArc(704,64,256,256,270,90)
$tile.AddArc(704,704,256,256,0,90)
$tile.AddArc(64,704,256,256,90,90)
$tile.CloseFigure()
$background = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(128,64)),([System.Drawing.Point]::new(896,960)),([System.Drawing.ColorTranslator]::FromHtml('#15274E')),([System.Drawing.ColorTranslator]::FromHtml('#5741B9'))
$g.FillPath($background,$tile)
$g.SetClip($tile)

# An aurora ribbon, a small moon, and two bold peaks suggest a live landscape.
$aurora = New-Object System.Drawing.Drawing2D.GraphicsPath
$aurora.AddBezier(64,490,310,120,570,470,960,140)
$aurora.AddLine(960,140,960,320)
$aurora.AddBezier(960,320,600,610,350,280,64,660)
$aurora.CloseFigure()
$ribbon = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(150,200)),([System.Drawing.Point]::new(850,530)),([System.Drawing.ColorTranslator]::FromHtml('#50E6D4')),([System.Drawing.ColorTranslator]::FromHtml('#8277F4'))
$g.FillPath($ribbon,$aurora)
$moon = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#E2FAFF'))
$g.FillEllipse($moon,700,240,96,96)
$rear = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#8EABF8'))
$g.FillPolygon($rear,[System.Drawing.Point[]]@([System.Drawing.Point]::new(400,838),[System.Drawing.Point]::new(704,480),[System.Drawing.Point]::new(1000,838)))
$front = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#E4F5FF'))
$g.FillPolygon($front,[System.Drawing.Point[]]@([System.Drawing.Point]::new(74,838),[System.Drawing.Point]::new(420,378),[System.Drawing.Point]::new(768,838)))
$facet = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#8CDCEB'))
$g.FillPolygon($facet,[System.Drawing.Point[]]@([System.Drawing.Point]::new(420,378),[System.Drawing.Point]::new(450,668),[System.Drawing.Point]::new(768,838)))
$base = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#15274E'))
$g.FillRectangle($base,64,838,896,122)
$g.ResetClip()
$bitmap.Save((Join-Path $PSScriptRoot 'AWall.png'),[System.Drawing.Imaging.ImageFormat]::Png)

# ICO directory with PNG frames; Windows chooses the right size for each display.
$sizes = @(16,24,32,48,64,128,256)
$frames = @()
foreach($size in $sizes) {
    $small = New-Object System.Drawing.Bitmap $size,$size
    $sg = [System.Drawing.Graphics]::FromImage($small)
    $sg.InterpolationMode = 'HighQualityBicubic'
    $sg.PixelOffsetMode = 'HighQuality'
    $sg.DrawImage($bitmap,0,0,$size,$size)
    $stream = New-Object System.IO.MemoryStream
    $small.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,($stream.ToArray())
    $stream.Dispose(); $sg.Dispose(); $small.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $PSScriptRoot 'AWall.ico'))
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for($i=0; $i -lt $sizes.Count; $i++) {
    $dimension = if($sizes[$i] -eq 256){0}else{$sizes[$i]}
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach($frame in $frames){ $writer.Write([byte[]]$frame) }
$writer.Dispose()
$g.Dispose(); $bitmap.Dispose()
foreach($resource in @($tile,$background,$aurora,$ribbon,$moon,$rear,$front,$facet,$base)){ $resource.Dispose() }
