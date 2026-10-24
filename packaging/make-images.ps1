param([string]$Source, [string]$Ico, [string]$PngDir)
# Builds a multi-size .ico (PNG-compressed entries) with rounded corners from a square picture,
# plus the PNG logo sizes MSIX packages need.
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile($Source)

function Render([int]$size, [int]$width = 0, [int]$height = 0, [double]$scale = 1.0) {
    if ($width -eq 0) { $width = $size; $height = $size }
    $bmp = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [int]($size * $scale)
    $x = [int](($width - $s) / 2); $y = [int](($height - $s) / 2)
    $r = [Math]::Max(2, [int]($s * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $r, $r, 180, 90); $path.AddArc($x + $s - $r - 1, $y, $r, $r, 270, 90)
    $path.AddArc($x + $s - $r - 1, $y + $s - $r - 1, $r, $r, 0, 90); $path.AddArc($x, $y + $s - $r - 1, $r, $r, 90, 90); $path.CloseFigure()
    $g.SetClip($path)
    $g.DrawImage($src, $x, $y, $s, $s)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($size in $sizes) {
    $bmp = Render $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $b = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([Byte]$b); $w.Write([Byte]$b); $w.Write([Byte]0); $w.Write([Byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes($Ico, $out.ToArray())

if ($PngDir) {
    New-Item -ItemType Directory -Force $PngDir | Out-Null
    $specs = @(
        @('Square44x44Logo.png', 44, 44, 44, 1.0), @('Square44x44Logo.targetsize-48_altform-unplated.png', 48, 48, 48, 1.0),
        @('Square150x150Logo.png', 150, 150, 150, 0.66), @('Wide310x150Logo.png', 150, 310, 150, 0.66),
        @('StoreLogo.png', 50, 50, 50, 1.0), @('SplashScreen.png', 300, 620, 300, 0.5)
    )
    foreach ($spec in $specs) {
        $bmp = Render $spec[1] $spec[2] $spec[3] $spec[4]
        $bmp.Save((Join-Path $PngDir $spec[0]), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
}
$src.Dispose()
"icon written"
