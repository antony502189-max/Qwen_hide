$ErrorActionPreference='Continue'
Write-Host 'Qwen Work Overlay diagnostics'
Write-Host "Windows: $([Environment]::OSVersion.VersionString)"
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\*' | Where-Object { $_.name -match 'WebView2' } | Select-Object name,pv
Add-Type -AssemblyName System.Windows.Forms
Write-Host 'Screens:'; [System.Windows.Forms.Screen]::AllScreens | ForEach-Object { "  $($_.DeviceName) $($_.Bounds)" }
Write-Host 'Audio endpoint visibility is available in the application's Settings and Diagnostics windows.'
