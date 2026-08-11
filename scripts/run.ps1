$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $root '.tools\dotnet\dotnet.exe') run --project (Join-Path $root 'src\QwenWorkOverlay\QwenWorkOverlay.csproj')
