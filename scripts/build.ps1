$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$dotnet=Join-Path $root '.tools\dotnet\dotnet.exe'
& $dotnet restore (Join-Path $root 'QwenWorkOverlay.sln')
& $dotnet build (Join-Path $root 'QwenWorkOverlay.sln') -c Release --no-restore
& $dotnet test (Join-Path $root 'QwenWorkOverlay.sln') -c Release --no-build
$dist=Join-Path $root 'dist'
& $dotnet restore (Join-Path $root 'src\QwenWorkOverlay\QwenWorkOverlay.csproj') -r win-x64
& $dotnet publish (Join-Path $root 'src\QwenWorkOverlay\QwenWorkOverlay.csproj') -c Release -r win-x64 --self-contained true -o $dist
Write-Host "Release executable: $(Join-Path $dist 'QwenWorkOverlay.exe')"
