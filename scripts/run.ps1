$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
& $dotnet run --project (Join-Path $root 'src\QwenWorkOverlay\QwenWorkOverlay.csproj')
