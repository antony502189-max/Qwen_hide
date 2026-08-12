param(
    [string]$OutputPath,
    [switch]$CompileOnly
)

$ErrorActionPreference = 'Stop'

# Qwen 1.0.3 on the target machine exposes only its Chrome_WidgetWin_1 root through
# Windows UI Automation, even when Chromium is launched with renderer accessibility
# forced on. Probe the older Microsoft Active Accessibility (MSAA/oleacc) bridge as a
# read-only fallback. The helper records only button/menu-like controls in the lower
# composer region; text/document/edit roles are never serialized.
Add-Type -AssemblyName Accessibility
$accessibilityAssembly = [Accessibility.IAccessible].Assembly.Location

$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Accessibility;

public sealed class QdcMsaaEntry
{
    public int Role { get; set; }
    public string Name { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }
}

public sealed class QdcMsaaResult
{
    public int HResult { get; set; }
    public int RootChildCount { get; set; }
    public int NodesVisited { get; set; }
    public bool Truncated { get; set; }
    public QdcMsaaEntry[] InteractiveControls { get; set; }
}

public static class QdcMsaaProbe
{
    private const uint OBJID_CLIENT = 0xFFFFFFFC;
    private const int CHILDID_SELF = 0;
    private const int MAX_NODES = 5000;
    private const int MAX_DEPTH = 16;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("oleacc.dll", PreserveSig = true)]
    private static extern int AccessibleObjectFromWindow(
        IntPtr hwnd,
        uint dwId,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppvObject);

    [DllImport("oleacc.dll", PreserveSig = true)]
    private static extern int AccessibleChildren(
        IAccessible paccContainer,
        int iChildStart,
        int cChildren,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] rgvarChildren,
        out int pcObtained);

    public static QdcMsaaResult Probe(IntPtr hwnd)
    {
        QdcMsaaResult result = new QdcMsaaResult();
        result.InteractiveControls = new QdcMsaaEntry[0];

        RECT rect;
        if (!GetWindowRect(hwnd, out rect))
        {
            result.HResult = Marshal.GetLastWin32Error();
            return result;
        }

        Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        object accessibleObject;
        int hr = AccessibleObjectFromWindow(hwnd, OBJID_CLIENT, ref iid, out accessibleObject);
        result.HResult = hr;
        if (hr < 0 || accessibleObject == null) return result;

        IAccessible root = accessibleObject as IAccessible;
        if (root == null) return result;

        try { result.RootChildCount = root.accChildCount; } catch { result.RootChildCount = -1; }

        List<QdcMsaaEntry> entries = new List<QdcMsaaEntry>();
        int visited = 0;
        bool truncated = false;
        double minX = rect.Left + ((rect.Right - rect.Left) * 0.25);
        double minY = rect.Top + ((rect.Bottom - rect.Top) * 0.50);

        Walk(root, 0, rect.Left, rect.Top, rect.Right, rect.Bottom, minX, minY, entries, ref visited, ref truncated);

        result.NodesVisited = visited;
        result.Truncated = truncated;
        result.InteractiveControls = entries.ToArray();
        return result;
    }

    private static void Walk(
        IAccessible container,
        int depth,
        int windowLeft,
        int windowTop,
        int windowRight,
        int windowBottom,
        double minX,
        double minY,
        List<QdcMsaaEntry> entries,
        ref int visited,
        ref bool truncated)
    {
        if (container == null || depth > MAX_DEPTH || visited >= MAX_NODES)
        {
            if (visited >= MAX_NODES) truncated = true;
            return;
        }

        int childCount;
        try { childCount = container.accChildCount; }
        catch { return; }
        if (childCount <= 0) return;

        object[] children = new object[childCount];
        int obtained;
        int hr;
        try { hr = AccessibleChildren(container, 0, childCount, children, out obtained); }
        catch { return; }
        if (hr < 0 || obtained <= 0) return;

        for (int i = 0; i < obtained; i++)
        {
            if (visited >= MAX_NODES)
            {
                truncated = true;
                return;
            }
            visited++;

            object raw = children[i];
            IAccessible childAccessible = raw as IAccessible;
            IAccessible owner = childAccessible ?? container;
            object childId = childAccessible != null ? (object)CHILDID_SELF : NormalizeChildId(raw);
            if (childId == null) continue;

            int role = SafeRole(owner, childId);
            if (IsInteractiveRole(role))
            {
                int left, top, width, height;
                if (TryLocation(owner, childId, out left, out top, out width, out height))
                {
                    double centerX = left + (width / 2.0);
                    double centerY = top + (height / 2.0);
                    bool inWindow = width > 0 && height > 0 &&
                                    centerX >= windowLeft && centerX <= windowRight &&
                                    centerY >= windowTop && centerY <= windowBottom;
                    bool inComposerRegion = inWindow && centerX >= minX && centerY >= minY;
                    if (inComposerRegion)
                    {
                        entries.Add(new QdcMsaaEntry
                        {
                            Role = role,
                            Name = SafeName(owner, childId),
                            Left = left,
                            Top = top,
                            Width = width,
                            Height = height,
                            Depth = depth
                        });
                    }
                }
            }

            if (childAccessible != null)
                Walk(childAccessible, depth + 1, windowLeft, windowTop, windowRight, windowBottom, minX, minY, entries, ref visited, ref truncated);
        }
    }

    private static object NormalizeChildId(object raw)
    {
        if (raw == null) return null;
        try
        {
            if (raw is int) return raw;
            if (raw is short) return Convert.ToInt32(raw);
            if (raw is long) return Convert.ToInt32(raw);
        }
        catch { }
        return null;
    }

    private static int SafeRole(IAccessible owner, object childId)
    {
        try
        {
            object role = owner.get_accRole(childId);
            if (role == null) return -1;
            return Convert.ToInt32(role);
        }
        catch { return -1; }
    }

    private static string SafeName(IAccessible owner, object childId)
    {
        try
        {
            string name = owner.get_accName(childId);
            if (String.IsNullOrEmpty(name)) return null;
            // Accessibility labels for controls should be short. Refuse unexpectedly large
            // strings rather than risk serializing renderer text content.
            return name.Length <= 160 ? name : name.Substring(0, 160);
        }
        catch { return null; }
    }

    private static bool TryLocation(IAccessible owner, object childId, out int left, out int top, out int width, out int height)
    {
        left = top = width = height = 0;
        try
        {
            owner.accLocation(out left, out top, out width, out height, childId);
            return width >= 0 && height >= 0;
        }
        catch { return false; }
    }

    private static bool IsInteractiveRole(int role)
    {
        // MSAA role constants. Keep this list deliberately narrow so text, edit,
        // document, list-item and history/sidebar content never enters the JSON.
        switch (role)
        {
            case 0x0C: // ROLE_SYSTEM_MENUITEM
            case 0x2B: // ROLE_SYSTEM_PUSHBUTTON
            case 0x2C: // ROLE_SYSTEM_CHECKBUTTON
            case 0x2D: // ROLE_SYSTEM_RADIOBUTTON
            case 0x2E: // ROLE_SYSTEM_COMBOBOX
            case 0x2F: // ROLE_SYSTEM_DROPLIST
            case 0x38: // ROLE_SYSTEM_BUTTONDROPDOWN
            case 0x39: // ROLE_SYSTEM_BUTTONMENU
            case 0x3A: // ROLE_SYSTEM_BUTTONDROPDOWNGRID
            case 0x3E: // ROLE_SYSTEM_SPLITBUTTON
                return true;
            default:
                return false;
        }
    }
}
'@

Add-Type -TypeDefinition $source -ReferencedAssemblies $accessibilityAssembly -Language CSharp

if ($CompileOnly) {
    Write-Host 'Qwen MSAA probe helper compiled successfully.'
    exit 0
}

$scriptDirectory = $PSScriptRoot
$root = if ((Split-Path $scriptDirectory -Leaf) -ieq 'scripts') {
    Split-Path -Parent $scriptDirectory
} else {
    $scriptDirectory
}

if (-not $OutputPath) {
    $artifacts = Join-Path $root 'artifacts'
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    $OutputPath = Join-Path $artifacts 'qwen-msaa-probe.json'
}

$target = $null
foreach ($process in [System.Diagnostics.Process]::GetProcessesByName('Qwen')) {
    try {
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and -not [string]::IsNullOrWhiteSpace($process.MainWindowTitle)) {
            if ($null -eq $target -or $process.MainWindowTitle -match 'Qwen') { $target = $process }
        }
    } catch {}
}

if ($null -eq $target) {
    throw 'No visible Qwen main window was found. Open Qwen Desktop and run the probe again.'
}

$probe = [QdcMsaaProbe]::Probe($target.MainWindowHandle)

$result = [ordered]@{
    generatedAt = (Get-Date).ToString('o')
    processId = $target.Id
    hwnd = ('0x{0:X}' -f $target.MainWindowHandle.ToInt64())
    windowTitle = $target.MainWindowTitle
    accessibleObjectHResult = $probe.HResult
    rootChildCount = $probe.RootChildCount
    nodesVisited = $probe.NodesVisited
    truncated = $probe.Truncated
    composerInteractiveControls = @($probe.InteractiveControls)
    notes = @(
        'Read-only MSAA/oleacc fallback probe for Qwen Chrome_WidgetWin_1.',
        'Only button/menu-like roles located in the lower composer region are serialized.',
        'Text, Edit, Document, list-item and history/sidebar roles are not serialized.'
    )
}

$result | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -Path $OutputPath
Write-Host "Qwen MSAA probe written to: $OutputPath"
Write-Host "PID: $($target.Id)"
Write-Host "HWND: 0x$($target.MainWindowHandle.ToInt64().ToString('X'))"
Write-Host "AccessibleObjectFromWindow HRESULT: $($probe.HResult)"
Write-Host "MSAA root child count: $($probe.RootChildCount)"
Write-Host "MSAA nodes visited: $($probe.NodesVisited)"
Write-Host "Composer interactive controls found: $(@($probe.InteractiveControls).Count)"
