$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $localDotnet)) { & (Join-Path $PSScriptRoot 'install-dotnet-sdk.ps1') }
& $localDotnet --info
New-Item -ItemType Directory -Force -Path (Join-Path $env:LOCALAPPDATA 'QwenWorkOverlay\WebViewProfile'),(Join-Path $env:LOCALAPPDATA 'QwenWorkOverlay\logs') | Out-Null
$wv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\*' -ErrorAction SilentlyContinue | Where-Object { $_.name -match 'WebView2' }
if (-not $wv) { Write-Warning 'WebView2 Runtime was not detected. Install Microsoft Edge WebView2 Evergreen Runtime before launching.' }
& $localDotnet restore (Join-Path $root 'QwenWorkOverlay.sln')
