Add-Type -AssemblyName System.Drawing
$pngPath = 'C:\Users\tomok\.gemini\antigravity-ide\brain\6230c313-e039-497a-9d7b-5a549c7b6345\image_manager_icon_1783744768139.png'
$icoPath = 'd:\Dev\ImageManager\Assets\AppIcon.ico'

if (-Not (Test-Path 'd:\Dev\ImageManager\Assets')) {
    New-Item -ItemType Directory -Path 'd:\Dev\ImageManager\Assets' | Out-Null
}

$img = [System.Drawing.Image]::FromFile($pngPath)
$bitmap = New-Object System.Drawing.Bitmap($img, 256, 256)

$fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICO header
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]1)

# Image directory
$bw.Write([byte]0) # width 256
$bw.Write([byte]0) # height 256
$bw.Write([byte]0) # color count
$bw.Write([byte]0) # reserved
$bw.Write([uint16]1) # planes
$bw.Write([uint16]32) # bpp

# Save bitmap to memory stream to get PNG data
$ms = New-Object System.IO.MemoryStream
$bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $ms.ToArray()

$bw.Write([uint32]$pngBytes.Length)
$bw.Write([uint32]22) # offset

$bw.Write($pngBytes)

$bw.Close()
$fs.Close()
$bitmap.Dispose()
$img.Dispose()

Write-Host "Converted successfully."
