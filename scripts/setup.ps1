$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'

if (-not (Test-Path $localDotnet) -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    & (Join-Path $PSScriptRoot 'install-dotnet-sdk.ps1')
}

$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
& $dotnet --info

$localData = Join-Path $env:LOCALAPPDATA 'QwenDesktopController'
New-Item -ItemType Directory -Force -Path $localData,(Join-Path $localData 'logs') | Out-Null

Write-Host 'Checking for a running Qwen Desktop process...'
$qwen = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'qwen' } | Select-Object -First 1
if ($qwen) {
    Write-Host "Found Qwen process: PID=$($qwen.Id) Name=$($qwen.ProcessName)"
    try { Write-Host "Executable: $($qwen.Path)" } catch {}
} else {
    Write-Warning 'Qwen Desktop is not currently running. This is OK; the controller can attach after you open it.'
}

& $dotnet restore (Join-Path $root 'QwenWorkOverlay.sln')
Write-Host 'Setup complete. This project no longer requires WebView2 because it controls the already-installed Qwen Desktop app.'
