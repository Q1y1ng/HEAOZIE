# 生成 assets\hz.ico（多尺寸 PNG 压缩 ICO）。
#
# 为什么用脚本生成而不是塞一个二进制进库：图标要跟 Theme.AppIcon 的观感一致
# （深色圆角方块 + 强调色描边 + 中央「H」），改配色时重新跑一遍即可，diff 也看得见。
#
# 用法： pwsh -NoProfile -File tools\make-icon.ps1
# 输出： assets\hz.ico（16/32/48/256 四档）

param(
    [string]$Out = "assets\hz.ico"
)

Add-Type -AssemblyName System.Drawing

$accent = [System.Drawing.Color]::FromArgb(0xE9, 0x45, 0x60)
$surface = [System.Drawing.Color]::FromArgb(0x11, 0x17, 0x1F)

function New-HzBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = $size * 0.06
    $w = $size - $pad * 2
    $radius = $size * 0.24

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($pad, $pad, $d, $d, 180, 90)
    $path.AddArc($pad + $w - $d, $pad, $d, $d, 270, 90)
    $path.AddArc($pad + $w - $d, $pad + $w - $d, $d, $d, 0, 90)
    $path.AddArc($pad, $pad + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.SolidBrush $surface
    $g.FillPath($bg, $path)
    $pen = New-Object System.Drawing.Pen $accent, ([Math]::Max(1.0, $size * 0.075))
    $g.DrawPath($pen, $path)

    # 中央「H」
    $bw = $size * 0.085
    $left = $size * 0.305
    $right = $size * 0.61
    $top = $size * 0.30
    $bot = $size * 0.70
    $fg = New-Object System.Drawing.SolidBrush $accent
    $g.FillRectangle($fg, [single]$left, [single]$top, [single]$bw, [single]($bot - $top))
    $g.FillRectangle($fg, [single]$right, [single]$top, [single]$bw, [single]($bot - $top))
    $g.FillRectangle($fg, [single]$left, [single]($size * 0.4575), [single](($right - $left) + $bw), [single]$bw)

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 32, 48, 256)
$blobs = @()
foreach ($s in $sizes) {
    $bmp = New-HzBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $blobs += , @{ Size = $s; Bytes = $ms.ToArray() }
    $ms.Dispose()
    $bmp.Dispose()
}

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

$fs = [System.IO.File]::Create((Join-Path (Get-Location) $Out))
$bw = New-Object System.IO.BinaryWriter $fs

# ICONDIR
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$blobs.Count)

$offset = 6 + 16 * $blobs.Count
foreach ($b in $blobs) {
    $dim = if ($b.Size -ge 256) { 0 } else { $b.Size }
    $bw.Write([byte]$dim)          # width
    $bw.Write([byte]$dim)          # height
    $bw.Write([byte]0)             # palette
    $bw.Write([byte]0)             # reserved
    $bw.Write([uint16]1)           # planes
    $bw.Write([uint16]32)          # bpp
    $bw.Write([uint32]$b.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $b.Bytes.Length
}
foreach ($b in $blobs) { $bw.Write($b.Bytes) }

$bw.Flush()
$bw.Dispose()
$fs.Dispose()

$full = Join-Path (Get-Location) $Out
Write-Output ("OK: {0}  ({1} bytes, sizes {2})" -f $full, (Get-Item $full).Length, ($sizes -join '/'))
