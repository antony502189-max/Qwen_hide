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

$rootRect = Get-SafeProperty $rootElement ([System.Windows.Automation.AutomationElement]::BoundingRectangleProperty)
if ($null -eq $rootRect -or $rootRect.Width -le 0 -or $rootRect.Height -le 0) {
    throw 'Qwen root bounding rectangle is unavailable.'
}

# Probe only the lower-right composer region where send/attachment/voice controls normally live.
# This intentionally avoids the history/sidebar and does not enumerate Text/Edit/Document controls,
# reducing the chance of collecting chat titles, prompts, or message contents.
$minX = $rootRect.X + ($rootRect.Width * 0.25)
$minY = $rootRect.Y + ($rootRect.Height * 0.50)

$allowedControlTypes = @(
    [System.Windows.Automation.ControlType]::Button,
    [System.Windows.Automation.ControlType]::CheckBox,
    [System.Windows.Automation.ControlType]::RadioButton,
    [System.Windows.Automation.ControlType]::MenuItem,
    [System.Windows.Automation.ControlType]::ComboBox,
    [System.Windows.Automation.ControlType]::Custom
)

$all = $rootElement.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)

$items = New-Object System.Collections.ArrayList
foreach ($element in $all) {
    $rect = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::BoundingRectangleProperty)
    if ($null -eq $rect -or $rect.Width -le 0 -or $rect.Height -le 0) { continue }

    $centerX = $rect.X + ($rect.Width / 2.0)
    $centerY = $rect.Y + ($rect.Height / 2.0)
    if ($centerX -lt $minX -or $centerY -lt $minY) { continue }

    $controlType = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::ControlTypeProperty)
    if ($null -eq $controlType) { continue }

    $isAllowedType = $false
    foreach ($allowed in $allowedControlTypes) {
        if ($controlType -eq $allowed) { $isAllowedType = $true; break }
    }
    if (-not $isAllowedType) { continue }

    $hasInvoke = Test-Pattern $element ([System.Windows.Automation.InvokePattern]::Pattern)
    $hasToggle = Test-Pattern $element ([System.Windows.Automation.TogglePattern]::Pattern)
    $hasSelectionItem = Test-Pattern $element ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $hasInvoke -and -not $hasToggle -and -not $hasSelectionItem -and $controlType -eq [System.Windows.Automation.ControlType]::Custom) { continue }

    $name = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::NameProperty)
    $automationId = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::AutomationIdProperty)
    $className = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::ClassNameProperty)
    $frameworkId = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::FrameworkIdProperty)
    $enabled = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::IsEnabledProperty)
    $offscreen = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::IsOffscreenProperty)

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
        bounds = [ordered]@{
            x = $rect.X
            y = $rect.Y
            width = $rect.Width
            height = $rect.Height
        }
    }
    [void]$items.Add([pscustomobject]$entry)
}

$result = [ordered]@{
    generatedAt = (Get-Date).ToString('o')
    processId = $target.Id
    hwnd = ('0x{0:X}' -f $target.MainWindowHandle.ToInt64())
    windowTitle = $target.MainWindowTitle
    rootClassName = Get-SafeProperty $rootElement ([System.Windows.Automation.AutomationElement]::ClassNameProperty)
    rootBounds = [ordered]@{
        x = $rootRect.X
        y = $rootRect.Y
        width = $rootRect.Width
        height = $rootRect.Height
    }
    probeRegion = [ordered]@{
        minX = $minX
        minY = $minY
        description = 'Lower-right 75% width / lower 50% height composer-oriented region'
    }
    interactiveControls = $items.ToArray()
    notes = @(
        'Only composer-area interactive/button-like UI Automation controls are collected.',
        'Text, Edit, Document and Hyperlink controls are excluded to avoid collecting chats, prompts, message text, or history titles.'
    )
}

$result | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -Path $OutputPath
Write-Host "Qwen UI Automation probe written to: $OutputPath"
Write-Host "PID: $($target.Id)"
Write-Host "HWND: 0x$($target.MainWindowHandle.ToInt64().ToString('X'))"
Write-Host "Composer-area interactive controls found: $($items.Count)"
