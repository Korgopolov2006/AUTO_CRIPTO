#requires -Version 7
<#
.SYNOPSIS
    Рисует значок приложения (щит с галочкой на синем скруглённом квадрате) и сохраняет его как многоразмерный .ico.

.DESCRIPTION
    Фигуры заданы в сетке 256 x 256 и рисуются с четырёхкратным запасом, затем уменьшаются до каждого размера: так мелкие размеры
    (16 и 24 пикселя в заголовке окна и трее) остаются чёткими. На мелких размерах галочка толще. В .ico лежат PNG-кадры
    16, 20, 24, 32, 40, 48, 64, 96, 128 и 256 пикселей. Запускается только при изменении рисунка; готовый файл лежит в
    src\SetupAssistant\Resources\AppIcon.ico и используется как значок exe, окна и области уведомлений.

.PARAMETER OutputPath
    Куда записать .ico (по умолчанию Resources\AppIcon.ico приложения).

.PARAMETER PreviewPath
    Необязательно: куда сохранить картинку с кадрами на светлом и тёмном фоне для проверки глазами.
#>
[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\SetupAssistant\Resources\AppIcon.ico'),
    [string]$PreviewPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Shield {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddBezier(128, 42, 158, 58, 182, 66, 204, 68)
    $path.AddBezier(204, 68, 207, 140, 178, 190, 128, 216)
    $path.AddBezier(128, 216, 78, 190, 49, 140, 52, 68)
    $path.AddBezier(52, 68, 74, 66, 98, 58, 128, 42)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$px) {
    # Рисуем в 4 раза крупнее (не меньше 256) и уменьшаем.
    $work = [Math]::Max(256, $px * 4)
    $big = [System.Drawing.Bitmap]::new($work, $work, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($work / 256.0, $work / 256.0)

    # Подложка: скруглённый квадрат с вертикальным градиентом (в тон акцента приложения #0F6CBD)
    $tile = New-RoundedRect 8 8 240 240 56
    $bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new(0, 8), [System.Drawing.PointF]::new(0, 248),
        [System.Drawing.ColorTranslator]::FromHtml('#2B94EC'), [System.Drawing.ColorTranslator]::FromHtml('#0A4C93'))
    $g.FillPath($bg, $tile)

    # Щит: мягкая тень, белая заливка, правая половина чуть темнее — объём без лишних деталей
    $shield = New-Shield
    $shadow = $shield.Clone()
    $shadow.Transform([System.Drawing.Drawing2D.Matrix]::new(1, 0, 0, 1, 0, 7))
    $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(70, 0, 28, 70)), $shadow)
    $g.FillPath([System.Drawing.Brushes]::White, $shield)
    $state = $g.Save()
    $g.SetClip($shield)
    $g.IntersectClip([System.Drawing.RectangleF]::new(128, 0, 128, 256))
    $g.FillRectangle([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(34, 10, 76, 147)), 0, 0, 256, 256)
    $g.Restore($state)

    # Галочка — «подпись проверена»; на мелких размерах толще, иначе пропадает
    $width = if ($px -le 24) { 30 } elseif ($px -le 48) { 24 } else { 20 }
    $pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#0F6CBD'), $width)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(93, 128), [System.Drawing.PointF]::new(118, 153), [System.Drawing.PointF]::new(166, 98)))
    $g.Dispose()

    $small = [System.Drawing.Bitmap]::new($px, $px, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $s = [System.Drawing.Graphics]::FromImage($small)
    $s.InterpolationMode = 'HighQualityBicubic'
    $s.PixelOffsetMode = 'HighQuality'
    $s.SmoothingMode = 'HighQuality'
    $s.DrawImage($big, 0, 0, $px, $px)
    $s.Dispose(); $big.Dispose()
    return $small
}

function ConvertTo-PngBytes($bitmap) {
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $stream.ToArray()
}

$frames = foreach ($px in $sizes) {
    $bmp = New-IconBitmap $px
    [pscustomobject]@{ Size = $px; Bitmap = $bmp; Png = (ConvertTo-PngBytes $bmp) }
}

# Контейнер .ico: заголовок, записи каталога, затем PNG-кадры один за другим
$out = [System.IO.MemoryStream]::new()
$w = [System.IO.BinaryWriter]::new($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $w.Write([byte]($f.Size % 256)); $w.Write([byte]($f.Size % 256))   # 256 записывается как 0
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$f.Png.Length); $w.Write([uint32]$offset)
    $offset += $f.Png.Length
}
foreach ($f in $frames) { $w.Write($f.Png) }
$w.Flush()

$target = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Host "Значок записан: $target ($([Math]::Round($out.Length / 1KB, 1)) КБ, кадров: $($frames.Count))"

if ($PreviewPath) {
    # Кадры подряд в натуральную величину на светлом и тёмном фоне, плюс 256 слева
    $pad = 16
    $rowWidth = [int](($frames | Where-Object Size -lt 256 | Measure-Object Size -Sum).Sum + $pad * ($frames.Count + 1) + 256)
    $sheet = [System.Drawing.Bitmap]::new($rowWidth, 2 * (256 + 2 * $pad))
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    foreach ($row in 0, 1) {
        $top = $row * (256 + 2 * $pad)
        $color = if ($row -eq 0) { [System.Drawing.Color]::FromArgb(243, 243, 243) } else { [System.Drawing.Color]::FromArgb(32, 32, 32) }
        $g.FillRectangle([System.Drawing.SolidBrush]::new($color), 0, $top, $rowWidth, 256 + 2 * $pad)
        $x = $pad
        foreach ($f in ($frames | Sort-Object Size -Descending)) {
            $g.DrawImage($f.Bitmap, $x, $top + $pad + 256 - $f.Size, $f.Size, $f.Size)
            $x += $f.Size + $pad
        }
    }
    $g.Dispose()
    $sheet.Save([System.IO.Path]::GetFullPath($PreviewPath), [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Предпросмотр: $PreviewPath"
}
