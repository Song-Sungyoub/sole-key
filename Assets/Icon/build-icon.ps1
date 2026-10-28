$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
$iconDir = $PSScriptRoot
$markup = [IO.File]::ReadAllText((Join-Path $iconDir 'Icon.xaml'))
$frames = @()
foreach ($size in @(16, 20, 24, 32, 48, 64, 128, 256, 512)) {
    $visual = [Windows.Markup.XamlReader]::Parse($markup)
    $visual.Width = $size
    $visual.Height = $size
    $visual.Measure([Windows.Size]::new($size, $size))
    $visual.Arrange([Windows.Rect]::new(0, 0, $size, $size))
    $visual.UpdateLayout()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    [IO.File]::WriteAllBytes((Join-Path $iconDir "SoleKey-$size.png"), $bytes)
    if ($size -le 256) { $frames += @{ Size = $size; Bytes = $bytes } }
}
$file = [IO.File]::Create((Join-Path $iconDir 'SoleKey.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
$checkFile = [IO.File]::OpenRead((Join-Path $iconDir 'SoleKey.ico'))
try {
    $decoded = [Windows.Media.Imaging.IconBitmapDecoder]::new($checkFile, [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    if ($decoded.Frames.Count -ne $frames.Count) { throw 'ICO frame count mismatch' }
    for ($index = 0; $index -lt $frames.Count; $index++) {
        if ($decoded.Frames[$index].PixelWidth -ne $frames[$index].Size -or $decoded.Frames[$index].PixelHeight -ne $frames[$index].Size) { throw 'ICO dimensions mismatch' }
    }
} finally { $checkFile.Dispose() }
Write-Output 'SoleKey icon: PNG 16-512px, ICO 8 frames validated'
