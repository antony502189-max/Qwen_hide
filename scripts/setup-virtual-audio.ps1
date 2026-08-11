param([string]$InstallerPath)
$ErrorActionPreference = 'Stop'
Write-Host 'Qwen Desktop Controller virtual-audio setup helper'
Write-Host 'The controller never installs drivers or changes Windows default audio devices automatically.'
Write-Host 'Install a trusted signed virtual cable only from its vendor, then restart Windows if the vendor installer requires it.'
Write-Host 'Example vendor documentation: https://vb-audio.com/Cable/'

if ($InstallerPath) {
    $resolved = Resolve-Path -LiteralPath $InstallerPath
    Write-Host "Opening the installer with UAC: $resolved"
    Start-Process -FilePath $resolved -Verb RunAs
    Write-Host 'After installation/restart: choose the cable render endpoint in Controller Settings.'
    Write-Host 'Then configure ONLY Qwen to use the paired capture endpoint through Qwen input settings or Windows per-app audio routing where supported.'
    Write-Host 'Do not set the cable as the global Windows default microphone.'
} else {
    Start-Process 'https://vb-audio.com/Cable/'
    Write-Host 'Download the correct signed installer from the vendor. You can rerun this script with -InstallerPath <path-to-setup.exe> to open it with UAC.'
}
