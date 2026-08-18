[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'ChatGPTDesktopController.sln'
$project = Join-Path $root 'src\ChatGPTDesktopController\ChatGPTDesktopController.csproj'
$dist = Join-Path $root 'dist'
$single = Join-Path $root 'dist-single'
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }

& $dotnet --info
if ($LASTEXITCODE -ne 0) {
    throw "dotnet --info failed with exit code $LASTEXITCODE"
}

& $dotnet restore $solution
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

& $dotnet build $solution -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

& $dotnet test $solution -c Release --no-build --logger 'console;verbosity=normal'
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE"
}

Remove-Item -LiteralPath $dist -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $single -Recurse -Force -ErrorAction SilentlyContinue

& $dotnet publish $project -c Release -r win-x64 --self-contained true -o $dist /p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (framework-dependent layout) failed with exit code $LASTEXITCODE"
}

& $dotnet publish $project -c Release -r win-x64 --self-contained true -o $single /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (single-file) failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $single 'ChatGPTDesktopController.exe'

if (-not (Test-Path -LiteralPath $exe)) {
    throw "Single-file publish did not create $exe"
}

Get-ChildItem -LiteralPath $single -File |
    Get-FileHash -Algorithm SHA256 |
    ForEach-Object {
        "$($_.Hash.ToLowerInvariant()) *$(Split-Path -Leaf $_.Path)"
    } |
    Set-Content -Encoding ascii (Join-Path $single 'SHA256SUMS.txt')

if (-not (Test-Path -LiteralPath (Join-Path $single 'SHA256SUMS.txt'))) {
    throw 'SHA256SUMS.txt was not created'
}

Write-Host "Built $exe"
