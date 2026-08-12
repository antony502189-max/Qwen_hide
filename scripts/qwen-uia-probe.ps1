param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# Windows PowerShell 5.1 uses the legacy CodeDOM compiler, so keep this embedded C# conservative.
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class QwenUiaProbeNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
'@

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

$uiaRootRect = Get-SafeProperty $rootElement ([System.Windows.Automation.AutomationElement]::BoundingRectangleProperty)
$rootBoundsSource = 'UIAutomation'
$rootRect = $uiaRootRect

# Chromium can expose a valid root AutomationElement while temporarily omitting its bounding rectangle.
# Falling back to Win32 GetWindowRect keeps the diagnostic useful instead of aborting before the tree scan.
if ($null -eq $rootRect -or $rootRect.Width -le 0 -or $rootRect.Height -le 0) {
    $nativeRect = New-Object QwenUiaProbeNative+RECT
    if ([QwenUiaProbeNative]::GetWindowRect($target.MainWindowHandle, [ref]$nativeRect)) {
        $rootRect = [pscustomobject]@{
            X = [double]$nativeRect.Left
            Y = [double]$nativeRect.Top
            Width = [double]($nativeRect.Right - $nativeRect.Left)
            Height = [double]($nativeRect.Bottom - $nativeRect.Top)
        }
        $rootBoundsSource = 'Win32.GetWindowRect'
    } else {
        throw 'Both UI Automation and Win32 root bounding rectangles are unavailable.'
    }
}

# Probe only the lower-right composer region where send/attachment/voice controls normally live.
# Text/Edit/Document/Hyperlink values are never collected.
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
$totalDescendants = $all.Count
$boundedDescendants = 0
$composerRegionDescendants = 0
$allowedTypeDescendants = 0

foreach ($element in $all) {
    $rect = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::BoundingRectangleProperty)
    if ($null -eq $rect -or $rect.Width -le 0 -or $rect.Height -le 0) { continue }
    $boundedDescendants++

    $centerX = $rect.X + ($rect.Width / 2.0)
    $centerY = $rect.Y + ($rect.Height / 2.0)
    if ($centerX -lt $minX -or $centerY -lt $minY) { continue }
    $composerRegionDescendants++

    $controlType = Get-SafeProperty $element ([System.Windows.Automation.AutomationElement]::ControlTypeProperty)
    if ($null -eq $controlType) { continue }

    $isAllowedType = $false
    foreach ($allowed in $allowedControlTypes) {
        if ($controlType -eq $allowed) { $isAllowedType = $true; break }
    }
    if (-not $isAllowedType) { continue }
    $allowedTypeDescendants++

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
    rootFrameworkId = Get-SafeProperty $rootElement ([System.Windows.Automation.AutomationElement]::FrameworkIdProperty)
    rootBoundsSource = $rootBoundsSource
    uiaRootBoundsAvailable = -not ($null -eq $uiaRootRect -or $uiaRootRect.Width -le 0 -or $uiaRootRect.Height -le 0)
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
    treeStats = [ordered]@{
        totalDescendants = $totalDescendants
        boundedDescendants = $boundedDescendants
        composerRegionDescendants = $composerRegionDescendants
        allowedTypeDescendants = $allowedTypeDescendants
        interactiveControls = $items.Count
    }
    interactiveControls = $items.ToArray()
    accessibilityTreeLikelyUnavailable = ($totalDescendants -eq 0 -or $boundedDescendants -eq 0)
    notes = @(
        'Only composer-area interactive/button-like UI Automation controls are collected.',
        'Text, Edit, Document and Hyperlink controls are excluded to avoid collecting chats, prompts, message text, or history titles.',
        'Win32 window bounds are used only as a geometry fallback when Chromium omits the root UIA bounding rectangle.'
    )
}

$result | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -Path $OutputPath
Write-Host "Qwen UI Automation probe written to: $OutputPath"
Write-Host "PID: $($target.Id)"
Write-Host "HWND: 0x$($target.MainWindowHandle.ToInt64().ToString('X'))"
Write-Host "Root bounds source: $rootBoundsSource"
Write-Host "UIA descendants: $totalDescendants"
Write-Host "Composer-region descendants: $composerRegionDescendants"
Write-Host "Composer-area interactive controls found: $($items.Count)"
