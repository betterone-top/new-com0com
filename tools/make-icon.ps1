<#
.SYNOPSIS
  Generates the application icons: a high-resolution vector redraw of the original com0com icon
  (two serial plugs joined by a looped cable), rendered at 16-256 px and packed as PNG entries.
    com0com.ico  setupg  (no badge; Windows adds the UAC shield itself)
    setupc.ico   setupc  (dark ">_" console badge: original command line)
    setupcn.ico  setupcn (blue ">_" console badge: new command line)

.PARAMETER OutDir
  Output directory (default: ..\src).

.PARAMETER Preview
  Optional directory for PNG previews of every size.
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src'),
    [string]$Preview = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256

function C([string]$hex, [int]$a = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    [System.Drawing.Color]::FromArgb($a, $c)
}

# Rounded rectangle path in design units.
function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# One plug (side view): connector face on the left, hood narrowing to the cable on the right.
# cy = vertical centre, accent = outline colour. Design space is 32x32.
function Draw-Plug($g, [float]$cy, $accent, [float]$line, [bool]$small) {
    # hood (trapezoid with rounded joins)
    $hood = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hood.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(8.0, $cy - 6.2),
        [System.Drawing.PointF]::new(15.6, $cy - 1.9),
        [System.Drawing.PointF]::new(15.6, $cy + 1.9),
        [System.Drawing.PointF]::new(8.0, $cy + 6.2)))
    $hb = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        [System.Drawing.PointF]::new(0, $cy - 6.5), [System.Drawing.PointF]::new(0, $cy + 6.5),
        (C $(if ($small) { '#E3E7EB' } else { '#F4F6F8' })), (C $(if ($small) { '#7D8792' } else { '#9AA3AD' })))
    $g.FillPath($hb, $hood)
    # subtle highlight along the upper edge
    $hl = New-Object System.Drawing.Pen((C '#FFFFFF' 170), [single]($line * 0.6))
    $g.DrawLine($hl, 8.9, $cy - 5.2, 15.0, $cy - 1.6)
    $pen = New-Object System.Drawing.Pen($accent, $line)
    $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $hood)

    # connector face (D-sub shell seen from the side)
    $face = RoundRect 3.6 ($cy - 7.0) 4.6 14.0 1.3
    $fb = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        [System.Drawing.PointF]::new(3.6, 0), [System.Drawing.PointF]::new(8.2, 0),
        (C '#FFFFFF'), (C '#C9CFD6'))
    $g.FillPath($fb, $face)
    $g.DrawPath($pen, $face)
    # mating edge (the pins side) as a darker strip
    $slot = RoundRect 4.5 ($cy - 5.6) 1.5 11.2 0.7
    $g.FillPath((New-Object System.Drawing.SolidBrush((C '#5B636C' 200))), $slot)
}

# Console badge (">_") in the lower right corner; drawn in device pixels so it stays crisp.
function Draw-Badge($g, [int]$size, $fill) {
    $g.ResetTransform()
    # relatively larger on small icons so it remains recognisable
    $frac = if ($size -le 24) { 0.62 } elseif ($size -le 48) { 0.55 } else { 0.5 }
    $w = [Math]::Round($size * $frac); $h = [Math]::Round($w * 0.78)
    $x = $size - $w; $y = $size - $h
    $r = [Math]::Max(1.0, $w * 0.14)
    $rect = RoundRect $x $y ($w - 0.5) ($h - 0.5) $r
    $g.FillPath((New-Object System.Drawing.SolidBrush($fill)), $rect)
    $edge = New-Object System.Drawing.Pen((C '#FFFFFF'), [single][Math]::Max(1.0, $size / 64))
    $g.DrawPath($edge, $rect)

    # ">_" glyph
    $t = [single][Math]::Max(1.0, $w * 0.11)
    $pen = New-Object System.Drawing.Pen((C '#FFFFFF'), $t)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    if ($size -le 20) { $pen.StartCap = 'Flat'; $pen.EndCap = 'Flat'; $pen.LineJoin = 'Miter' }
    $cx = $x + $w * 0.24; $cy = $y + $h * 0.30; $ch = $h * 0.40
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($cx, $cy),
        [System.Drawing.PointF]::new($cx + $ch * 0.55, $cy + $ch / 2),
        [System.Drawing.PointF]::new($cx, $cy + $ch)))
    $uy = $y + $h * 0.70
    $g.DrawLine($pen, [single]($x + $w * 0.52), [single]$uy, [single]($x + $w * 0.78), [single]$uy)
}

function Render([int]$size, $badge) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0
    $g.ScaleTransform($s, $s)

    # outline width: thin at large sizes, at least ~1.2 device pixels at small sizes
    $small = $size -le 24
    $line = if ($small) { 1.25 / $s } else { [Math]::Max(0.9, 1.0 / $s) }

    $red = C '#9B1C1C'
    $dark = C '#1F2328'

    # cable: dark-red jacket with a light core, looping from the upper plug to the lower one
    $cable = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cable.AddLine(15.0, 8.0, 19.5, 8.0)
    $cable.AddBezier(19.5, 8.0, 30.2, 8.0, 30.2, 24.0, 19.5, 24.0)
    $cable.AddLine(19.5, 24.0, 15.0, 24.0)
    $jacket = New-Object System.Drawing.Pen($red, [single](3.6 + $line * 0.4))
    $jacket.StartCap = 'Flat'; $jacket.EndCap = 'Flat'; $jacket.LineJoin = 'Round'
    $g.DrawPath($jacket, $cable)
    if ($size -gt 20) {
        # light core; at the smallest sizes the cable stays solid so it doesn't blur
        $core = New-Object System.Drawing.Pen((C '#D9DDE2'), [single]([Math]::Max(1.2, 3.6 - 2 * $line)))
        $core.LineJoin = 'Round'
        $g.DrawPath($core, $cable)
    }
    if ($size -ge 48) {
        $shine = New-Object System.Drawing.Pen((C '#FFFFFF' 150), [single]0.5)
        $shine.LineJoin = 'Round'
        $g.TranslateTransform(-0.35, -0.35)
        $g.DrawPath($shine, $cable)
        $g.TranslateTransform(0.35, 0.35)
    }

    Draw-Plug $g 8.0 $red $line $small
    Draw-Plug $g 24.0 $dark $line $small

    if ($badge) { Draw-Badge $g $size $badge }
    $g.Dispose()
    $bmp
}

# ---------------------------------------------------------------- pack the .ico (PNG entries)
function Write-Ico([string]$name, $badge) {
    $out = Join-Path (Resolve-Path $OutDir) $name
    $images = foreach ($size in $Sizes) {
        $bmp = Render $size $badge
        if ($Preview) {
            New-Item -ItemType Directory -Force $Preview | Out-Null
            $bmp.Save((Join-Path $Preview ("{0}-{1}.png" -f [IO.Path]::GetFileNameWithoutExtension($name), $size)), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        [pscustomobject]@{ Size = $size; Data = $ms.ToArray() }
    }

    $fs = [System.IO.File]::Create($out)
    $w = New-Object System.IO.BinaryWriter($fs)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($img in $images) {
        $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$img.Data.Length); $w.Write([uint32]$offset)
        $offset += $img.Data.Length
    }
    foreach ($img in $images) { $w.Write($img.Data) }
    $w.Close()
    "wrote $out ($((Get-Item $out).Length) bytes)"
}

Write-Ico 'com0com.ico' $null            # setupg
Write-Ico 'setupc.ico' (C '#3A3F45')     # original command line: dark console
Write-Ico 'setupcn.ico' (C '#0067C0')    # new command line: blue console
