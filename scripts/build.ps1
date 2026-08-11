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

Copy-Item (Join-Path $root 'scripts\runtime-probe.ps1') (Join-Path $dist 'runtime-probe.ps1') -Force
Copy-Item (Join-Path $root 'scripts\diagnose.ps1') (Join-Path $dist 'diagnose.ps1') -Force
Copy-Item (Join-Path $root 'scripts\setup-virtual-audio.ps1') (Join-Path $dist 'setup-virtual-audio.ps1') -Force
Copy-Item (Join-Path $root 'README.md') (Join-Path $dist 'README.md') -Force
Copy-Item (Join-Path $root 'GUIDE_EN.md') (Join-Path $dist 'GUIDE_EN.md') -Force
Copy-Item (Join-Path $root 'GUIDE_RU.md') (Join-Path $dist 'GUIDE_RU.md') -Force
Copy-Item (Join-Path $root 'MANUAL_TEST_CHECKLIST_EN.md') (Join-Path $dist 'MANUAL_TEST_CHECKLIST_EN.md') -Force
Copy-Item (Join-Path $root 'RUN_ME_FIRST_RU.txt') (Join-Path $dist 'RUN_ME_FIRST_RU.txt') -Force

$exe = Join-Path $dist 'QwenDesktopController.exe'
if (-not (Test-Path $exe)) { throw "Publish completed but executable was not found: $exe" }
$hash = Get-FileHash $exe -Algorithm SHA256
Write-Host "Release executable: $exe"
Write-Host "SHA256: $($hash.Hash)"
Write-Host "Runtime probe: $(Join-Path $dist 'runtime-probe.ps1')"
Write-Host "Diagnostics helper: $(Join-Path $dist 'diagnose.ps1')"
Write-Host "Virtual-audio helper: $(Join-Path $dist 'setup-virtual-audio.ps1')"
Write-Host "Russian guide: $(Join-Path $dist 'GUIDE_RU.md')"
