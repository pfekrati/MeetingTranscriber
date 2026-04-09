Add-Type -AssemblyName System.Drawing

$srcPath = Join-Path $PSScriptRoot "..\MeetingTranscriber\Resources\transcript.png"
$icoPath = Join-Path $PSScriptRoot "..\MeetingTranscriber\Resources\transcript.ico"
$srcImg = [System.Drawing.Image]::FromFile($srcPath)

$sizes = @(16, 32, 48, 256)
$imageDataList = New-Object System.Collections.Generic.List[byte[]]

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($srcImg, 0, 0, $s, $s)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageDataList.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}
$srcImg.Dispose()

# Build ICO file
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICO header
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

# Calculate initial data offset
$dataOffset = 6 + $sizes.Count * 16

# Write directory entries
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = $imageDataList[$i]
    if ($s -ge 256) { $bw.Write([byte]0) } else { $bw.Write([byte]$s) }
    if ($s -ge 256) { $bw.Write([byte]0) } else { $bw.Write([byte]$s) }
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$dataOffset)
    $dataOffset += $data.Length
}

# Write image data
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bw.Write($imageDataList[$i])
}

$bw.Dispose()
$fs.Dispose()

# Verify
$newBytes = [System.IO.File]::ReadAllBytes($icoPath)
$count = [BitConverter]::ToUInt16($newBytes, 4)
Write-Host "Generated ICO with $count images:"
for ($j = 0; $j -lt $count; $j++) {
    $off = 6 + $j * 16
    $w = $newBytes[$off]; $h = $newBytes[$off+1]
    $bpp = [BitConverter]::ToUInt16($newBytes, $off+6)
    $sz = [BitConverter]::ToUInt32($newBytes, $off+8)
    $wLabel = if ($w -eq 0) { "256" } else { "$w" }
    $hLabel = if ($h -eq 0) { "256" } else { "$h" }
    Write-Host "  ${wLabel}x${hLabel} ${bpp}bpp ($sz bytes)"
}
Write-Host "Saved to: $icoPath"
