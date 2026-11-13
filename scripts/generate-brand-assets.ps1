param(
    [string]$SourceIcon = "F:\Programacion\ProyectosPruebas\Microsoft\Plataforma .NET y .NET Core\Proyectos\WinUI\WinUI + Visual C#\CSILPD\src\CSILPD\Assets\Appicon.ico"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$uiAssets = Join-Path $root "src/CSIViewer/Assets"
$serverAssets = Join-Path $root "src/CSIViewer.Server/Assets"
$hostAssets = Join-Path $root "src/CSIViewer.Host/Assets"
foreach ($dir in @($uiAssets, $serverAssets, $hostAssets)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item $SourceIcon (Join-Path $dir "Appicon.ico") -Force
}

# Extract the 256x256 PNG frame from the ICO so the big tiles stay sharp.
$bytes = [System.IO.File]::ReadAllBytes($SourceIcon)
$count = [BitConverter]::ToUInt16($bytes, 4)
$pngBytes = $null
for ($i = 0; $i -lt $count; $i++) {
    $offset = 6 + ($i * 16)
    $width = if ($bytes[$offset] -eq 0) { 256 } else { [int]$bytes[$offset] }
    $size = [BitConverter]::ToUInt32($bytes, $offset + 8)
    $dataOffset = [BitConverter]::ToUInt32($bytes, $offset + 12)
    if ($width -ge 256) {
        $pngBytes = $bytes[$dataOffset..($dataOffset + $size - 1)]
        break
    }
}
if (-not $pngBytes) {
    throw "No se encontró un frame de 256x256 en el icono."
}

$tempPng = Join-Path $env:TEMP ("csi-viewer-icon-" + [Guid]::NewGuid().ToString("N") + ".png")
[System.IO.File]::WriteAllBytes($tempPng, $pngBytes)
$source = [System.Drawing.Bitmap]::FromFile($tempPng)

function New-Canvas([int]$width, [int]$height, [System.Drawing.Color]$background) {
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear($background)
    return @{ Bitmap = $bitmap; Graphics = $graphics }
}

function Draw-Icon($graphics, [int]$x, [int]$y, [int]$width, [int]$height) {
    $graphics.DrawImage($source, $x, $y, $width, $height)
}

function Draw-FittedText($graphics, [string]$text, [double]$fontSize, $style, $brush, [int]$x, [int]$y, [int]$maxWidth) {
    $size = $fontSize
    while ($true) {
        $font = New-Object System.Drawing.Font("Segoe UI", $size, $style)
        $measure = $graphics.MeasureString($text, $font)
        if ($measure.Width -le $maxWidth -or $size -le 8) {
            break
        }
        $font.Dispose()
        $size -= 1
    }
    $graphics.DrawString($text, $font, $brush, $x, $y)
    $font.Dispose()
}

function Save-Square([int]$size, [string]$path) {
    $canvas = New-Canvas $size $size ([System.Drawing.Color]::White)
    $padding = [Math]::Max(2, [int]($size * 0.09))
    $inner = $size - ($padding * 2)
    Draw-Icon $canvas.Graphics $padding $padding $inner $inner
    $canvas.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Graphics.Dispose()
    $canvas.Bitmap.Dispose()
}

foreach ($item in @(
    @(44, "Square44x44Logo.png"),
    @(50, "StoreLogo.png"),
    @(71, "Square71x71Logo.png"),
    @(150, "Square150x150Logo.png"),
    @(310, "Square310x310Logo.png"),
    @(310, "LargeTile.png")
)) {
    Save-Square $item[0] (Join-Path $uiAssets $item[1])
}

$brand = [System.Drawing.Color]::FromArgb(255, 245, 247, 250)
$dark = [System.Drawing.Color]::FromArgb(255, 15, 59, 102)
$muted = [System.Drawing.Color]::FromArgb(255, 70, 90, 115)

$wide = New-Canvas 310 150 $brand
Draw-Icon $wide.Graphics 18 30 86 86
Draw-FittedText $wide.Graphics "CSIViewer" 22 ([System.Drawing.FontStyle]::Bold) (New-Object System.Drawing.SolidBrush($dark)) 112 44 186
Draw-FittedText $wide.Graphics "Cliente" 14 ([System.Drawing.FontStyle]::Regular) (New-Object System.Drawing.SolidBrush($muted)) 114 83 184
$wide.Bitmap.Save((Join-Path $uiAssets "Wide310x150Logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$wide.Graphics.Dispose()
$wide.Bitmap.Dispose()

$splash = New-Canvas 620 300 $brand
Draw-Icon $splash.Graphics 68 68 164 164
Draw-FittedText $splash.Graphics "CSIViewer" 42 ([System.Drawing.FontStyle]::Bold) (New-Object System.Drawing.SolidBrush($dark)) 258 92 336
Draw-FittedText $splash.Graphics "Cliente" 22 ([System.Drawing.FontStyle]::Regular) (New-Object System.Drawing.SolidBrush($muted)) 262 162 330
$splash.Bitmap.Save((Join-Path $uiAssets "SplashScreen.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$splash.Graphics.Dispose()
$splash.Bitmap.Dispose()

$source.Dispose()
Remove-Item $tempPng -Force -ErrorAction SilentlyContinue
Write-Host "Iconos de CSIViewer generados en $uiAssets"
