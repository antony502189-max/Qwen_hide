param([string]$InstallerPath)
$ErrorActionPreference = 'Stop'
Write-Host 'Qwen Work Overlay virtual-audio setup'
Write-Host 'The application never installs or changes audio drivers automatically.'
Write-Host 'Install a signed virtual cable only from its vendor, then restart Windows if the installer requires it.'
Write-Host 'Example vendor documentation: https://vb-audio.com/Cable/'
if ($InstallerPath) {
    $resolved = Resolve-Path -LiteralPath $InstallerPath
    Write-Host "Opening the installer with UAC: $resolved"
    Start-Process -FilePath $resolved -Verb RunAs
    Write-Host 'After installation/restart: choose the cable render endpoint in Qwen Work Overlay Settings, then its paired capture endpoint in Qwen voice settings.'
} else {
    Start-Process 'https://vb-audio.com/Cable/'
    Write-Host 'After installing the driver, rerun this script with -InstallerPath <path-to-setup.exe> to open the installer through UAC.'
}
