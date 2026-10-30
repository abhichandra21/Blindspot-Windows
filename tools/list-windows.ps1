Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[W]::SetProcessDPIAware() | Out-Null
$rows = New-Object System.Collections.ArrayList
[W]::EnumWindows({
    param($h, $l)
    if ([W]::IsWindowVisible($h)) {
        $t = New-Object System.Text.StringBuilder 512
        $c = New-Object System.Text.StringBuilder 256
        [W]::GetWindowText($h, $t, 512) | Out-Null
        [W]::GetClassName($h, $c, 256) | Out-Null
        $procId = 0; [W]::GetWindowThreadProcessId($h, [ref]$procId) | Out-Null
        $p = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName
        $r = New-Object W+RECT
        [W]::DwmGetWindowAttribute($h, 9, [ref]$r, 16) | Out-Null   # 9 = extended frame bounds
        [void]$rows.Add([pscustomobject]@{
            Process = $p; Title = $t.ToString(); Class = $c.ToString()
            X = $r.L; Y = $r.T; W = $r.R - $r.L; H = $r.B - $r.T })
    }
    $true
}, [IntPtr]::Zero) | Out-Null
$rows | Where-Object { $_.Process -like "*teams*" } | Format-Table -AutoSize
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Screen]::AllScreens | Format-Table DeviceName, Bounds, Primary -AutoSize
