#requires -Version 7
<#
Generates the icons under src/NodLight.App:
  app.ico              the exe icon (Task Manager, Explorer, the taskbar menu header)
  icons/{dark,light}/new.ico, install.ico, remove.ico, hide.ico    the taskbar menu entries
app.ico draws the same design as AppIcon.cs (three status-coloured bars on the strip's dark background). The menu
icons are monochrome glyphs on a transparent background. All designs use a
64-unit grid scaled to each size and are stored as PNG entries. Re-run after changing a design.
#>
param([string]$AppDir = (Join-Path $PSScriptRoot '..\src\NodLight.App'))

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = 2 * $r
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-Pen($color, [float]$s) {
    $pen = [System.Drawing.Pen]::new($color, 6 * $s)
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen
}

function Get-Points([float]$s, [float[]]$xy) {
    for ($i = 0; $i -lt $xy.Count; $i += 2) { [System.Drawing.PointF]::new($xy[$i] * $s, $xy[$i + 1] * $s) }
}

$drawings = @{
    app = {
        param($g, $s, $size, $color)
        $bg = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(0x11, 0x11, 0x1B))
        $path = New-RoundedPath 0 0 $size $size (12 * $s)
        $g.FillPath($bg, $path)
        $bars = @(
            @{ Top = 8;  Color = [System.Drawing.Color]::FromArgb(0xFA, 0xB3, 0x87) },
            @{ Top = 24; Color = [System.Drawing.Color]::FromArgb(0xA6, 0xE3, 0xA1) },
            @{ Top = 40; Color = [System.Drawing.Color]::FromArgb(0x6C, 0x70, 0x86) }
        )
        foreach ($bar in $bars) {
            $brush = [System.Drawing.SolidBrush]::new($bar.Color)
            $barPath = New-RoundedPath (8 * $s) ($bar.Top * $s) (48 * $s) (14 * $s) (4 * $s)
            $g.FillPath($brush, $barPath)
            $brush.Dispose(); $barPath.Dispose()
        }
        $bg.Dispose(); $path.Dispose()
    }
    new = {       # plus
        param($g, $s, $size, $color)
        $pen = New-Pen $color $s
        $g.DrawLine($pen, 32 * $s, 10 * $s, 32 * $s, 54 * $s)
        $g.DrawLine($pen, 10 * $s, 32 * $s, 54 * $s, 32 * $s)
        $pen.Dispose()
    }
    install = {   # arrow dropping into a tray
        param($g, $s, $size, $color)
        $pen = New-Pen $color $s
        $g.DrawLine($pen, 32 * $s, 8 * $s, 32 * $s, 38 * $s)
        $g.DrawLines($pen, [System.Drawing.PointF[]](Get-Points $s 19, 26, 32, 39, 45, 26))
        $g.DrawLines($pen, [System.Drawing.PointF[]](Get-Points $s 10, 40, 10, 55, 54, 55, 54, 40))
        $pen.Dispose()
    }
    remove = {    # trash can
        param($g, $s, $size, $color)
        $pen = New-Pen $color $s
        $g.DrawLine($pen, 10 * $s, 16 * $s, 54 * $s, 16 * $s)
        $g.DrawLines($pen, [System.Drawing.PointF[]](Get-Points $s 24, 16, 24, 8, 40, 8, 40, 16))
        $g.DrawLines($pen, [System.Drawing.PointF[]](Get-Points $s 16, 16, 19, 55, 45, 55, 48, 16))
        $g.DrawLine($pen, 28 * $s, 28 * $s, 28 * $s, 44 * $s)
        $g.DrawLine($pen, 36 * $s, 28 * $s, 36 * $s, 44 * $s)
        $pen.Dispose()
    }
    hide = {      # eye with a slash
        param($g, $s, $size, $color)
        $pen = New-Pen $color $s
        $g.DrawEllipse($pen, 6 * $s, 20 * $s, 52 * $s, 24 * $s)
        $g.DrawEllipse($pen, 26 * $s, 26 * $s, 12 * $s, 12 * $s)
        $g.DrawLine($pen, 12 * $s, 56 * $s, 52 * $s, 8 * $s)
        $pen.Dispose()
    }
}

function New-IconPng([int]$size, [scriptblock]$draw, $color) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    & $draw $g ($size / 64.0) $size $color
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    , $ms.ToArray()
}

function Write-Ico([string]$path, [scriptblock]$draw, [int[]]$sizes, $color = $null) {
    $images = foreach ($size in $sizes) { , (New-IconPng $size $draw $color) }
    $stream = [System.IO.MemoryStream]::new()
    $w = [System.IO.BinaryWriter]::new($stream)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)   # ICONDIR: reserved, type = icon, count
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }                 # 0 means 256
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)                                # planes, bits per pixel
        $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $w.Write($image) }
    $w.Flush()
    $full = [System.IO.Path]::GetFullPath($path)
    New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
    [System.IO.File]::WriteAllBytes($full, $stream.ToArray())
    Write-Output "Wrote $full ($($stream.Length) bytes, sizes: $($sizes -join ', '))"
}

Write-Ico (Join-Path $AppDir 'app.ico') $drawings.app 16, 24, 32, 48, 64, 256

# Menu icons are monochrome in the menu's text colour: white glyphs for the dark theme, near-black for the light one.
# The app picks the folder from the Windows theme (see App.SetUpJumpList).
$themes = @{
    dark  = [System.Drawing.Color]::FromArgb(0xF2, 0xF2, 0xF2)
    light = [System.Drawing.Color]::FromArgb(0x1F, 0x1F, 0x1F)
}
foreach ($theme in $themes.Keys) {
    foreach ($name in 'new', 'install', 'remove', 'hide') {
        Write-Ico (Join-Path $AppDir "icons\$theme\$name.ico") $drawings[$name] 16, 24, 32, 48 $themes[$theme]
    }
}
