[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipWindowsGraphicsCapture,
    [string]$CppWinRtInclude
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'tools\ChatGPTPrivacyCaptureProbe\bin' }
$dxgiSource = Join-Path $root 'tools\ChatGPTPrivacyCaptureProbe\DesktopDuplicationProbe.cpp'
$wgcSource = Join-Path $root 'tools\ChatGPTPrivacyCaptureProbe\WindowsGraphicsCaptureProbe.cpp'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw "Visual Studio locator not found: $vswhere" }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual C++ x64 build tools are required for the advanced capture probes.' }
$devCmd = Join-Path $vs 'Common7\Tools\VsDevCmd.bat'
if (-not (Test-Path -LiteralPath $devCmd)) { throw "Visual Studio developer command script not found: $devCmd" }

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$dxgiExe = Join-Path $OutputDirectory 'chatgpt-dxgi-capture-probe.exe'
$dxgiObj = Join-Path $OutputDirectory 'DesktopDuplicationProbe.obj'
$command = 'call "' + $devCmd + '" -arch=x64 -host_arch=x64 >nul && cl.exe /nologo /std:c++17 /EHsc /O2 /W4 /DUNICODE /D_UNICODE "' + $dxgiSource + '" /Fo:"' + $dxgiObj + '" /Fe:"' + $dxgiExe + '" /link d3d11.lib dxgi.lib user32.lib'

$wgcExe = Join-Path $OutputDirectory 'chatgpt-wgc-capture-probe.exe'
if (-not $SkipWindowsGraphicsCapture) {
    if ($CppWinRtInclude) {
        $cppWinRtIncludePath = (Resolve-Path -LiteralPath $CppWinRtInclude).Path
    }
    else {
        $sdkIncludeRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Include'
        $sdkInclude = Get-ChildItem -Path $sdkIncludeRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
        if ($sdkInclude) { $cppWinRtIncludePath = Join-Path $sdkInclude.FullName 'cppwinrt' }
    }
    if (-not $cppWinRtIncludePath -or -not (Test-Path -LiteralPath (Join-Path $cppWinRtIncludePath 'winrt\base.h'))) {
        throw 'Windows SDK C++/WinRT headers are required for the Windows Graphics Capture probe. Use -SkipWindowsGraphicsCapture to build DXGI only.'
    }
    $wgcObj = Join-Path $OutputDirectory 'WindowsGraphicsCaptureProbe.obj'
    $command += ' && cl.exe /nologo /std:c++17 /EHsc /O2 /W4 /DUNICODE /D_UNICODE /D_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS /I"' + $cppWinRtIncludePath + '" "' + $wgcSource + '" /Fo:"' + $wgcObj + '" /Fe:"' + $wgcExe + '" /link d3d11.lib dxgi.lib user32.lib windowsapp.lib'
}

cmd.exe /d /c $command
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dxgiExe)) { throw 'DXGI privacy probe compilation failed.' }
if (-not $SkipWindowsGraphicsCapture -and -not (Test-Path -LiteralPath $wgcExe)) { throw 'Windows Graphics Capture privacy probe compilation failed.' }

Write-Host "DXGI probe: $dxgiExe"
if (-not $SkipWindowsGraphicsCapture) { Write-Host "WGC probe:  $wgcExe" }
