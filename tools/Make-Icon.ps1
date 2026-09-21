#requires -Version 7
<#
.SYNOPSIS
    Renders the EventBlitz icon (a cyan bolt on the dark console background) as icon.png in the repository root
    and Assets/icon.ico for the executable. Run after changing the design; both files are committed.
#>
param(
    [string] $RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Render([int] $size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded dark tile with a hairline border, like the app's own surfaces.
    $radius = [Math]::Max(2, $size * 0.22)
    $rect = [System.Drawing.RectangleF]::new(0.5, 0.5, $size - 1, $size - 1)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 11, 12, 16)), $path)
    if ($size -ge 32) {
        $g.DrawPath([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(40, 255, 255, 255), [Math]::Max(1, $size / 128)), $path)
    }

    # The bolt: a slanted zig-zag filling the middle of the tile.
    $s = $size / 100.0
    $bolt = @(
        [System.Drawing.PointF]::new(57 * $s, 14 * $s),
        [System.Drawing.PointF]::new(30 * $s, 56 * $s),
        [System.Drawing.PointF]::new(48 * $s, 56 * $s),
        [System.Drawing.PointF]::new(41 * $s, 86 * $s),
        [System.Drawing.PointF]::new(70 * $s, 42 * $s),
        [System.Drawing.PointF]::new(52 * $s, 42 * $s)
    )
    $g.FillPolygon([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 34, 211, 238)), $bolt)

    $g.Dispose()
    return $bitmap
}

function ToPng([System.Drawing.Bitmap] $bitmap) {
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return $stream.ToArray()
}

$png256 = ToPng (Render 256)
[System.IO.File]::WriteAllBytes((Join-Path $RepoRoot 'icon.png'), $png256)

# ICO container with PNG-compressed entries (supported since Windows Vista).
$sizes = 256, 64, 48, 32, 16
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) { $images.Add((ToPng (Render $size))) }
$ico = [System.IO.MemoryStream]::new()
$w = [System.IO.BinaryWriter]::new($ico)
$w.Write([uint16] 0); $w.Write([uint16] 1); $w.Write([uint16] $sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $w.Write([byte] ($(if ($size -ge 256) { 0 } else { $size })))
    $w.Write([byte] ($(if ($size -ge 256) { 0 } else { $size })))
    $w.Write([byte] 0); $w.Write([byte] 0)
    $w.Write([uint16] 1); $w.Write([uint16] 32)
    $w.Write([uint32] $images[$i].Length)
    $w.Write([uint32] $offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $w.Write($image) }
$w.Flush()
$assets = Join-Path $RepoRoot 'src/EventBlitz.App/Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $assets 'icon.ico'), $ico.ToArray())
Write-Host "icon.png and Assets/icon.ico written"
