$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$logoPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../assets/branding/SandinFinance.png'))
$assetDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../assets/branding'))
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$imageStream = [System.IO.MemoryStream]::new([System.IO.File]::ReadAllBytes($logoPath))
$decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create($imageStream, [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
$image = $decoder.Frames[0]
$imageStream.Dispose()
$sizes = @(16, 32, 48, 256)
$frames = @()
foreach ($size in $sizes) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.DrawImage($image, [System.Windows.Rect]::new(0, 0, $size, $size))
    $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $frameStream = [System.IO.MemoryStream]::new()
    $encoder.Save($frameStream)
    $frames += ,$frameStream.ToArray()
    $frameStream.Dispose()
}
$outputPath = Join-Path $assetDirectory 'SandinFinance.ico'
$outputStream = [System.IO.File]::Create($outputPath)
$writer = [System.IO.BinaryWriter]::new($outputStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { [byte]0 } else { [byte]$sizes[$index] }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
Write-Host 'Windows application icon regenerated from SandinFinance.png.'
