$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$solution = Join-Path $root 'QwenWorkOverlay.sln'
$project = Join-Path $root 'src\QwenWorkOverlay\QwenWorkOverlay.csproj'
$dist = Join-Path $root 'dist'
$distSingle = Join-Path $root 'dist-single'
$privacyProbe = Join-Path $root 'tools\PrivacyCaptureProbe\bin\privacy-capture-probe.exe'
$wgcPrivacyProbe = Join-Path $root 'tools\PrivacyCaptureProbe\bin\privacy-wgc-capture-probe.exe'

function Invoke-Dotnet([string[]]$arguments, [string]$operation) {
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$operation failed with exit code $LASTEXITCODE" }
}

Invoke-Dotnet -arguments @('restore', $solution) -operation 'Restore solution'
Invoke-Dotnet -arguments @('build', $solution, '-c', 'Release', '--no-restore') -operation 'Build solution'
Invoke-Dotnet -arguments @('test', $solution, '-c', 'Release', '--no-build') -operation 'Test solution'

& (Join-Path $PSScriptRoot 'build-privacy-capture-probe.ps1') -OutputPath $privacyProbe
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $privacyProbe) -or -not (Test-Path $wgcPrivacyProbe)) { throw 'Build privacy capture probes failed.' }

foreach ($directory in @($dist, $distSingle)) {
    if (Test-Path $directory) { Remove-Item $directory -Recurse -Force }
}

Invoke-Dotnet -arguments @('restore', $project, '-r', 'win-x64') -operation 'Restore win-x64 project'
Invoke-Dotnet -arguments @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $dist) -operation 'Publish multi-file win-x64'

# Endpoint protection can hold a newly-created single-file EXE for a moment. Retry only this
# deterministic publish, and never continue to packaging/hashing on an unsuccessful attempt.
$singlePublished = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
    & $dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $distSingle
    if ($LASTEXITCODE -eq 0) { $singlePublished = $true; break }
    if ($attempt -lt 3) { Start-Sleep -Seconds $attempt }
}
if (-not $singlePublished) { throw 'Publish single-file win-x64 failed after 3 attempts.' }

function Add-ReleaseExtras([string]$destination) {
    foreach ($name in @('runtime-probe.ps1','qwen-uia-probe.ps1','qwen-msaa-probe.ps1','calibrate-qwen-voice-click.ps1','diagnose.ps1','setup-virtual-audio.ps1')) {
        Copy-Item (Join-Path $root "scripts\$name") (Join-Path $destination $name) -Force
    }
    foreach ($name in @('README.md','GUIDE_EN.md','GUIDE_RU.md','MANUAL_TEST_CHECKLIST_EN.md','RUN_ME_FIRST_RU.txt')) {
        Copy-Item (Join-Path $root $name) (Join-Path $destination $name) -Force
    }
    Copy-Item $privacyProbe (Join-Path $destination 'privacy-capture-probe.exe') -Force
    Copy-Item $wgcPrivacyProbe (Join-Path $destination 'privacy-wgc-capture-probe.exe') -Force

    $exe = Join-Path $destination 'QwenDesktopController.exe'
    if (-not (Test-Path $exe)) { throw "Publish completed but executable was not found: $exe" }
    $hash = Get-FileHash $exe -Algorithm SHA256
    "QwenDesktopController.exe  SHA256  $($hash.Hash)" | Set-Content -Encoding ASCII (Join-Path $destination 'SHA256SUMS.txt')
    Write-Host "Release executable: $exe"
    Write-Host "SHA256: $($hash.Hash)"
}

Add-ReleaseExtras $dist
Add-ReleaseExtras $distSingle

if (Test-Path (Join-Path $distSingle 'QwenDesktopController.dll')) {
    throw 'Single-file publish unexpectedly contains QwenDesktopController.dll.'
}

Write-Host "Reliable multi-file release: $dist"
Write-Host "Single-file release: $distSingle"
Write-Host "Runtime probe: $(Join-Path $distSingle 'runtime-probe.ps1')"
Write-Host "Russian guide: $(Join-Path $distSingle 'GUIDE_RU.md')"
