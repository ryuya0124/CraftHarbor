# Rasterize our code-native SVG paths with WPF; no external image tooling required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/CraftHarbor.Desktop/Assets'
[xml]$source = Get-Content -Raw -LiteralPath (Join-Path $assetRoot 'CraftHarbor.svg')
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($size / 64.0, $size / 64.0))
    foreach ($path in $source.svg.path) {
        $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromString($path.fill)
        $geometry = [System.Windows.Media.Geometry]::Parse($path.d)
        $drawing.DrawGeometry($brush, $null, $geometry)
    }
    $drawing.Pop()
    $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [System.IO.MemoryStream]::new()
    $encoder.Save($memory)
    $frames.Add($memory.ToArray())
    if ($size -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $assetRoot 'CraftHarbor.png'), $memory.ToArray()) }
    $memory.Dispose()
}
$stream = [System.IO.File]::Create((Join-Path $assetRoot 'CraftHarbor.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Output 'Generated 7-resolution ICO and 256px PNG from SVG.'
