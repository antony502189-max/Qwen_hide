$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$solution = Join-Path $root 'QwenWorkOverlay.sln'
$project = Join-Path $root 'src\QwenWorkOverlay\QwenWorkOverlay.csproj'
$dist = Join-Path $root 'dist'

& $dotnet restore $solution
& $dotnet build $solution -c Release --no-restore
& $dotnet test $solution -c Release --no-build

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
& $dotnet restore $project -r win-x64
& $dotnet publish $project -c Release -r win-x64 --self-contained true -o $dist

$exe = Join-Path $dist 'QwenDesktopController.exe'
if (-not (Test-Path $exe)) { throw "Publish completed but executable was not found: $exe" }
Write-Host "Release executable: $exe"
