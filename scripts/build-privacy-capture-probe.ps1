param([string]$OutputPath)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $root 'tools\PrivacyCaptureProbe\bin\privacy-capture-probe.exe' }
$source = Join-Path $root 'tools\PrivacyCaptureProbe\PrivacyCaptureProbe.cpp'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw "Visual Studio locator not found: $vswhere" }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual C++ build tools are required for the Desktop Duplication privacy probe.' }
$devCmd = Join-Path $vs 'Common7\Tools\VsDevCmd.bat'
if (-not (Test-Path $devCmd)) { throw "Visual Studio developer command script not found: $devCmd" }
$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

# cmd owns the Visual C++ environment; quote each path rather than relying on the current shell.
$objectPath = Join-Path $outputDirectory 'PrivacyCaptureProbe.obj'
$command = 'call "' + $devCmd + '" -arch=x64 -host_arch=x64 >nul && cl.exe /nologo /std:c++17 /EHsc /O2 /W4 /DUNICODE /D_UNICODE "' + $source + '" /Fo:"' + $objectPath + '" /Fe:"' + $OutputPath + '" /link d3d11.lib dxgi.lib user32.lib'
cmd.exe /d /c $command
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $OutputPath)) { throw 'Desktop Duplication privacy probe compilation failed.' }
Write-Host "Privacy capture probe built: $OutputPath"
