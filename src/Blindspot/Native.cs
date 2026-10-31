using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Blindspot;

static class Native
{
    delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    const int DWMWA_CLOAKED = 14;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x80;
    const int SW_MINIMIZE = 6;

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(nint hWnd, int command);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(nint hIcon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hWnd, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hWnd, int index);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hWnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hWnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hWnd, int attribute, ref int value, int size);

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;

    public static void UseRoundedCorners(nint hWnd)
    {
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    // Every visible top-level window, or null if the list could not be read.
    public static IReadOnlyList<WindowInfo>? VisibleWindows()
    {
        var windows = new List<WindowInfo>();
        var processNames = new Dictionary<uint, string>();
        bool ok = EnumWindows((hWnd, _) =>
        {
            if (IsWindowVisible(hWnd)) windows.Add(Describe(hWnd, processNames));
            return true;
        }, 0);
        return ok ? windows : null;
    }

    static WindowInfo Describe(nint hWnd, Dictionary<uint, string> processNames)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        if (!processNames.TryGetValue(pid, out var process))
        {
            try { using var p = Process.GetProcessById((int)pid); process = p.ProcessName; }
            catch (ArgumentException) { process = ""; } // already exited
            processNames[pid] = process;
        }
        bool cloaked = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloakedFlags, sizeof(int)) == 0 && cloakedFlags != 0;
        bool toolWindow = ((long)GetWindowLongPtr(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0;
        return new WindowInfo(hWnd, process, Title(hWnd), ClassName(hWnd), Bounds(hWnd),
            IsNormal: !IsIconic(hWnd) && !toolWindow && !cloaked);
    }

    // The window as it is now, or null if it closed or is already minimized.
    public static CurrentWindow? Read(nint hWnd) =>
        IsWindow(hWnd) && !IsIconic(hWnd) ? new CurrentWindow(Title(hWnd), Bounds(hWnd)) : null;

    // Posted rather than sent, so a busy Teams cannot hold up the check.
    public static bool Minimize(nint hWnd) => ShowWindowAsync(hWnd, SW_MINIMIZE);

    public static string Title(nint hWnd)
    {
        var text = new StringBuilder(512);
        GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }

    static string ClassName(nint hWnd)
    {
        var name = new StringBuilder(256);
        GetClassName(hWnd, name, name.Capacity);
        return name.ToString();
    }

    // Extended frame bounds exclude the invisible resize borders that
    // GetWindowRect includes.
    public static Rect Bounds(nint hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) != 0) return default;
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}
