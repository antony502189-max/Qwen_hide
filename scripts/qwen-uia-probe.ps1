param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$scriptDirectory = $PSScriptRoot
$root = if ((Split-Path $scriptDirectory -Leaf) -ieq 'scripts') {
    Split-Path -Parent $scriptDirectory
} else {
    $scriptDirectory
}

if (-not $OutputPath) {
    $artifacts = Join-Path $root 'artifacts'
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    $OutputPath = Join-Path $artifacts 'qwen-uia-probe.json'
}

$qwenProcesses = [System.Diagnostics.Process]::GetProcessesByName('Qwen')
$target = $null
foreach ($process in $qwenProcesses) {
    try {
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and -not [string]::IsNullOrWhiteSpace($process.MainWindowTitle)) {
            if ($null -eq $target -or $process.MainWindowTitle -match 'Qwen') {
                $target = $process
            }
        }
    } catch {}
}

if ($null -eq $target) {
    throw 'No visible Qwen main window was found. Open Qwen Desktop and run the probe again.'
}

$rootElement = [System.Windows.Automation.AutomationElement]::FromHandle($target.MainWindowHandle)
if ($null -eq $rootElement) {
    throw 'UI Automation could not open the Qwen root element.'
}

function Get-SafeProperty($element, $property) {
    try {
        $value = $element.GetCurrentPropertyValue($property, $true)
        if ($value -eq [System.Windows.Automation.AutomationElement]::NotSupported) { return $null }
        return $value
    } catch {
        return $null
    }
}

function Test-Pattern($element, $pattern) {
    try {
        $patternObject = $null
        return $element.TryGetCurrentPattern($pattern, [ref]$patternObject)
    } catch {
        return $false
    }
}

# Deliberately inspect only interactive/button-like controls. Do not enumerate Text/Edit/Document
# elements so chat contents and prompt text are not collected by this diagnostic.
$allowedControlTypes = @(
    [System.Windows.Automation.ControlType]::Button,
    [System.Windows.Automation.ControlType]::CheckBox,
    [System.Windows.Automation.ControlType]::RadioButton,
    [System.Windows.Automation.ControlType]::MenuItem,
    [System.Windows.Automation.ControlType]::Hyperlink,
    [System.Windows.Automation.ControlType]::ComboBox
)

$all = $rootElement.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)

$items = New-Object System.Collections.ArrayList
foreach ($element in $all) {
    $controlType = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::ControlTypeProperty)
    if ($null -eq $controlType) { continue }

    $isAllowedType = $false
    foreach ($allowed in $allowedControlTypes) {
        if ($controlType -eq $allowed) { $isAllowedType = $true; break }
    }

    $hasInvoke = Test-Pattern $element ([System.Windows.Automation.InvokePattern]::Pattern)
    $hasToggle = Test-Pattern $element ([System.Windows.Automation.TogglePattern]::Pattern)
    $hasSelectionItem = Test-Pattern $element ([System.Windows.Automation.SelectionItemPattern]::Pattern)

    if (-not $isAllowedType -and -not $hasInvoke -and -not $hasToggle -and -not $hasSelectionItem) { continue }

    $name = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::NameProperty)
    $automationId = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::AutomationIdProperty)
    $className = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::ClassNameProperty)
    $frameworkId = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::FrameworkIdProperty)
    $enabled = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::IsEnabledProperty)
    $offscreen = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::IsOffscreenProperty)
    $rect = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::BoundingRectangleProperty)

    $controlTypeName = $null
    try { $controlTypeName = $controlType.ProgrammaticName } catch { $controlTypeName = $controlType.ToString() }

    $entry = [ordered]@{
        controlType = $controlTypeName
        name = if ($name) { [string]$name } else { $null }
        automationId = if ($automationId) { [string]$automationId } else { $null }
        className = if ($className) { [string]$className } else { $null }
        frameworkId = if ($frameworkId) { [string]$frameworkId } else { $null }
        isEnabled = $enabled
        isOffscreen = $offscreen
        invokePattern = $hasInvoke
        togglePattern = $hasToggle
        selectionItemPattern = $hasSelectionItem
        bounds = if ($rect) {
            [ordered]@{
                x = $rect.X
                y = $rect.Y
                width = $rect.Width
                height = $rect.Height
            }
        } else { $null }
    }
    [void]$items.Add([pscustomobject]$entry)
}

$result = [ordered]@{
    generatedAt = (Get-Date).ToString('o')
    processId = $target.Id
    hwnd = ('0x{0:X}' -f $target.MainWindowHandle.ToInt64())
    windowTitle = $target.MainWindowTitle
    rootClassName = Get-SafeProperty $rootElement ([System.Windows.Automation.AutomationElement]::ClassNameProperty)
    interactiveControls = $items.ToArray()
    notes = @(
        'Only interactive/button-like UI Automation controls are collected.',
        'Text, Edit and Document elements are deliberately excluded to avoid collecting Qwen chat or prompt contents.'
    )
}

$result | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -Path $OutputPath
Write-Host "Qwen UI Automation probe written to: $OutputPath"
Write-Host "PID: $($target.Id)"
Write-Host "HWND: 0x$($target.MainWindowHandle.ToInt64().ToString('X'))"
Write-Host "Interactive controls found: $($items.Count)"
