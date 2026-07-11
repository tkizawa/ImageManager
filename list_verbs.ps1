$path = 'C:\Users\tomok\OneDrive\2024-12-07 07_37_13 PM スキャン.pdf'
if (-Not (Test-Path $path)) {
    Write-Host "File not found."
    exit
}
$shell = New-Object -ComObject Shell.Application
$folder = $shell.Namespace((Split-Path $path))
$item = $folder.ParseName((Split-Path $path -Leaf))
Write-Host "Verbs:"
$item.Verbs() | ForEach-Object { Write-Host $_.Name }
