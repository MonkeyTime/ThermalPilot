# Regenerate the vector sources and Windows multi-resolution icons without external design tools.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/ThermalPilot/Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$variants = [ordered]@{
    'ThermalPilot' = $null
    'TrayIdle' = '#91A0B6'
    'TrayActive' = '#6DE5C0'
    'TrayCooling' = '#F0B472'
    'TrayAlert' = '#F27983'
}

foreach ($variant in $variants.GetEnumerator()) {
    $badge = if ($variant.Value) { '<circle cx="77" cy="77" r="16" fill="' + $variant.Value + '" stroke="#0D1420" stroke-width="5"/>' } else { '' }
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96">
  <rect x="2" y="2" width="92" height="92" rx="20" fill="#162131" stroke="#6DE5C0" stroke-width="3"/>
  <path d="M43 23V62" fill="none" stroke="#EDF3FA" stroke-width="17" stroke-linecap="round"/>
  <circle cx="43" cy="70" r="19" fill="#EDF3FA"/>
  <path d="M43 38V67" fill="none" stroke="#6DE5C0" stroke-width="7" stroke-linecap="round"/>
  <circle cx="43" cy="70" r="11" fill="#6DE5C0"/>
  <path d="M63 28H74M63 40H70M63 52H74" fill="none" stroke="#6DE5C0" stroke-width="4" stroke-linecap="round"/>
$badge
</svg>
"@
    Set-Content -LiteralPath (Join-Path $assetDirectory ($variant.Key + '.svg')) -Value $svg -Encoding utf8
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size * 4, $size * 4)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.ScaleTransform(($size * 4 / 96.0), ($size * 4 / 96.0))
        $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#162131'))
        $teal = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#6DE5C0'), 3)
        $white = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#EDF3FA'))
        $green = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#6DE5C0'))
        $round = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $round.AddArc(2, 2, 40, 40, 180, 90)
        $round.AddArc(54, 2, 40, 40, 270, 90)
        $round.AddArc(54, 54, 40, 40, 0, 90)
        $round.AddArc(2, 54, 40, 40, 90, 90)
        $round.CloseFigure()
        $graphics.FillPath($background, $round)
        $graphics.DrawPath($teal, $round)
        $stem = [System.Drawing.Pen]::new($white, 17)
        $stem.StartCap = $stem.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($stem, 43, 23, 43, 62)
        $graphics.FillEllipse($white, 24, 51, 38, 38)
        $inner = [System.Drawing.Pen]::new($green, 7)
        $inner.StartCap = $inner.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($inner, 43, 38, 43, 67)
        $graphics.FillEllipse($green, 32, 59, 22, 22)
        $teal.Width = 4
        $teal.StartCap = $teal.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($teal, 63, 28, 74, 28)
        $graphics.DrawLine($teal, 63, 40, 70, 40)
        $graphics.DrawLine($teal, 63, 52, 74, 52)
        if ($variant.Value) {
            $statusBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($variant.Value))
            $statusPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#0D1420'), 5)
            $graphics.FillEllipse($statusBrush, 61, 61, 32, 32)
            $graphics.DrawEllipse($statusPen, 61, 61, 32, 32)
            $statusBrush.Dispose()
            $statusPen.Dispose()
        }
        $outputBitmap = [System.Drawing.Bitmap]::new($size, $size)
        $outputGraphics = [System.Drawing.Graphics]::FromImage($outputBitmap)
        $outputGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $outputGraphics.DrawImage($bitmap, 0, 0, $size, $size)
        $memory = [System.IO.MemoryStream]::new()
        $outputBitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($memory.ToArray())
        if ($size -eq 256) { $outputBitmap.Save((Join-Path $assetDirectory ($variant.Key + '.png')), [System.Drawing.Imaging.ImageFormat]::Png) }
        foreach ($resource in @($memory, $outputGraphics, $outputBitmap, $round, $inner, $stem, $green, $white, $teal, $background, $graphics, $bitmap)) { $resource.Dispose() }
    }
    $file = [System.IO.File]::Create((Join-Path $assetDirectory ($variant.Key + '.ico')))
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write($frame) }
    }
    finally { $writer.Dispose(); $file.Dispose() }
}
