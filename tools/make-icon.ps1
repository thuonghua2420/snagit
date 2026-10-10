# Regenerates src/SnapGit.App/Assets/SnapGit.ico from the same design as Ui/AppIcon.cs
# (blue rounded square, four capture brackets, centre dot). Run from the repo root:  pwsh tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.ScaleTransform($size / 32.0, $size / 32.0)

    $r = 7.0; $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(1, 1, $d, $d, 180, 90); $path.AddArc(31 - $d, 1, $d, $d, 270, 90)
    $path.AddArc(31 - $d, 31 - $d, $d, $d, 0, 90); $path.AddArc(1, 31 - $d, $d, $d, 90, 90); $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 120, 215))), $path)

    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 2.6
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $a = 8.0; $b = 24.0; $l = 6.0
    $g.DrawLines($pen, @([System.Drawing.PointF]::new($a, $a + $l), [System.Drawing.PointF]::new($a, $a), [System.Drawing.PointF]::new($a + $l, $a)))
    $g.DrawLines($pen, @([System.Drawing.PointF]::new($b - $l, $a), [System.Drawing.PointF]::new($b, $a), [System.Drawing.PointF]::new($b, $a + $l)))
    $g.DrawLines($pen, @([System.Drawing.PointF]::new($a, $b - $l), [System.Drawing.PointF]::new($a, $b), [System.Drawing.PointF]::new($a + $l, $b)))
    $g.DrawLines($pen, @([System.Drawing.PointF]::new($b - $l, $b), [System.Drawing.PointF]::new($b, $b), [System.Drawing.PointF]::new($b, $b - $l)))
    $g.FillEllipse([System.Drawing.Brushes]::White, 13, 13, 6, 6)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = $sizes | ForEach-Object { , (New-IconPng $_) }

$out = Join-Path $PSScriptRoot '..\src\SnapGit.App\Assets\SnapGit.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$stream = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $stream
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $bytes = $images[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $images) { $w.Write($bytes) }
$w.Close()
"wrote $out ($((Get-Item $out).Length) bytes, sizes: $($sizes -join ', '))"
