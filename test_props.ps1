$path = 'C:\Users\tomok\OneDrive\2024-12-07 07_37_13 PM スキャン.pdf'
$shell = New-Object -ComObject Shell.Application
$folder = $shell.Namespace((Split-Path $path))
$item = $folder.ParseName((Split-Path $path -Leaf))
for($i=0; $i -le 320; $i++){
    $prop = $folder.GetDetailsOf($item, $i)
    if($prop){ Write-Host "$i : $prop" }
}
