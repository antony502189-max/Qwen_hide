$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'

function Test-DotNet8Sdk([string]$Exe) {
    if (-not $Exe -or -not (Test-Path $Exe)) { return $false }
    try {
        $sdks = & $Exe --list-sdks 2>$null
        return [bool]($sdks | Where-Object { $_ -match '^8\.0\.' })
    } catch { return $false }
}

$systemDotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$systemDotnet = if ($systemDotnetCommand) { $systemDotnetCommand.Source } else { $null }

if (Test-DotNet8Sdk $localDotnet) {
    $dotnet = $localDotnet
} elseif (Test-DotNet8Sdk $systemDotnet) {
    $dotnet = $systemDotnet
} else {
    Write-Host '.NET 8 SDK not found; installing a local project-scoped SDK...'
    & (Join-Path $PSScriptRoot 'install-dotnet-sdk.ps1')
    if (-not (Test-DotNet8Sdk $localDotnet)) { throw 'Local .NET 8 SDK installation did not produce a usable SDK.' }
    $dotnet = $localDotnet
}

Write-Host "Using dotnet: $dotnet"
& $dotnet --version
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
Write-Host 'Setup complete. Native-controller mode uses the already-installed Qwen Desktop app and does not require WebView2.'
