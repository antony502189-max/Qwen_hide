[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'ChatGPTDesktopController.sln'
$dist = Join-Path $root 'dist'
$single = Join-Path $root 'dist-single'
dotnet --info
dotnet restore $solution
dotnet build $solution -c Release --no-restore
dotnet test $solution -c Release --no-build
Remove-Item -LiteralPath $dist -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $single -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src\ChatGPTDesktopController\ChatGPTDesktopController.csproj') -c Release -r win-x64 --self-contained true -o $dist /p:PublishSingleFile=false
dotnet publish (Join-Path $root 'src\ChatGPTDesktopController\ChatGPTDesktopController.csproj') -c Release -r win-x64 --self-contained true -o $single /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
Get-ChildItem -LiteralPath $single -File | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant()) *$(Split-Path -Leaf $_.Path)" } | Set-Content -Encoding ascii (Join-Path $single 'SHA256SUMS.txt')
Write-Host "Built $single\ChatGPTDesktopController.exe"
