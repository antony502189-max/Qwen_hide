$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $root '.tools\dotnet-sdk.zip'
$install = Join-Path $root '.tools\dotnet'
$url = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.424/dotnet-sdk-8.0.424-win-x64.zip'
New-Item -ItemType Directory -Force -Path (Join-Path $root '.tools') | Out-Null
if (-not (Test-Path (Join-Path $install 'dotnet.exe'))) {
  Write-Host 'Downloading .NET SDK 8.0.424 locally (no administrator permission required)...'
  Invoke-WebRequest -Uri $url -OutFile $destination
  Expand-Archive -LiteralPath $destination -DestinationPath $install -Force
}
& (Join-Path $install 'dotnet.exe') --version
