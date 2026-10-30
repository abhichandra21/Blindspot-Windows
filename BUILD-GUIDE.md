# Blindspot for Windows: build guide

This guide covers building a Windows version of Blindspot, the Mac menu bar app in `../Blindspot`.
It is written for a Windows laptop with Visual Studio or VS Code and the new Microsoft Teams installed.
It contains everything needed to start: what the app does, how the Mac version works, what is still unknown on Windows, the stack, the design, the steps in order, and how to ship.

## 1. What the app does

Blindspot warns you when a Teams Chat or Activity window may be visible in your Teams screen share.
It checks about once a second. When it finds a risky window inside the shared area, it:

- plays a warning sound (once, when the warning starts),
- turns its tray icon red,
- shows a red banner on a screen you are *not* sharing,
- and, if you turned on minimizing, minimizes those Teams windows when you share a whole screen.

Left-click the tray icon to pause or resume.
Right-click for Settings and Quit.

## 2. Rules carried over from the Mac version

These are fixed decisions in `../Blindspot/charter.md`. Keep them for Windows too.

- Teams only. No Zoom, Slack, Meet, or browser-share detectors, and no general detector framework.
- Fully local. No telemetry, no uploads, no network calls, and no capturing screen images.
- No paid code-signing certificate required. The app can ship unsigned.
- When a **whole screen** is shared, a watched Teams window on that screen counts even if other apps cover most of it.
  A chat partly visible behind another app is an intended warning case.
  A completely covered chat still warns. That is an accepted limit; do not build occlusion logic.
- Only minimize when the target is clear:
  - Minimize only during a **whole-screen** share.
  - During a **single-window** share, warn but never minimize.
    Overlapping rectangles do not prove the chat is the shared window.
  - If two windows look the same, or the window changed since it was seen, leave it alone.
- Minimizing is **off by default**. The user turns it on in Settings.
- While minimizing is on, keep retrying every check. Do not add a cooldown or a "once per share" rule.
- Show the warning (sound, icon, banner) before trying to minimize, so a slow minimize never delays the warning.
- Show the banner only on a screen that is not shared.
  With one screen, skip the banner and rely on the sound and red icon.
  Do not rely on "hide this window from capture" settings to make the banner safe on the shared screen.
- Keep it small and understandable for one person to maintain. No extra frameworks or architecture.

## 3. How the Mac version detects a share

Read `../Blindspot/Sources/Blindspot/Detection.swift` for the full logic. In short:

1. List every on-screen window with its owner app, title, layer, and bounds.
2. Keep only windows owned by Microsoft Teams.
3. If one of them is titled `Sharing Indicator`, a share is running.
   That window covers exactly the shared area: the whole screen for a screen share, or the window for a window share.
4. Decide whether the shared area is a whole screen by comparing it with each display's bounds, with about 60 points of slack (the indicator animates while starting and stopping).
5. Find "risky" Teams windows: normal windows whose title starts with `Chat` or `Activity` (for example `Chat | Jane Doe | Microsoft Teams`).
6. A risky window is exposed when:
   - whole-screen share: it overlaps the shared screen at all;
   - window share: the overlap covers at least half of the shared area.
7. Report one of these states, which drive the icon and status text:

| State | Status text |
|---|---|
| Paused | Paused |
| Checks disabled | Checks disabled: no Teams tabs selected |
| No permission | Needs Screen Recording permission (Mac only, see below) |
| Observation failed | Unable to check Teams sharing |
| Not sharing | No Teams share detected |
| Sharing, clear | Sharing; no watched Teams windows detected in the shared area |
| Chat exposed | Teams Chat may be visible in your share |

On Windows the "No permission" state likely goes away, because Windows lets any app read other windows' titles and positions without asking.

## 4. Step zero: find out how Teams for Windows looks (do this first)

The Mac detection was found by watching the window list during a real share.
**None of it is confirmed on Windows yet.** Before writing the app, answer these questions on the laptop:

1. What is the Teams process name? (New Teams is expected to be `ms-teams.exe`.)
2. While sharing, does Teams create a window that marks the shared area, like the Mac `Sharing Indicator`?
   What is its title and class name, and do its bounds match the shared screen or window?
   (Teams for Windows shows a red border around what you share and a small floating control bar. One of those is the likely signal.)
3. What are the main Teams window titles on the Chat and Activity tabs? Do they still start with `Chat |` and `Activity |`?
4. Do pop-out chat windows have their own titles?
5. Does the same signal appear for a whole-screen share and a single-window share, and can you tell them apart?

### Discovery script

Save this as `list-windows.ps1` and run it in PowerShell before a share, during a whole-screen share, and during a window share.
It prints every visible top-level window with its process, title, class, and bounds.
To share with no one else around, start a "Meet now" meeting in Teams alone and share your screen there.

```powershell
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
```

If the script prints nothing for Teams, remove the `Where-Object` filter to see every window and find the Teams process name.

Before sharing any output with anyone, note that the window titles include chat partner names. Replace them with placeholders.

**Write the answers into the "Windows findings" section at the end of this file.**
If Teams for Windows has no window that marks the shared area, stop and rethink detection before building anything else.

## 5. Tech stack

- **Language and runtime:** C# on .NET 10 (the current long-term support version).
- **UI:** Windows Forms. It gives a tray icon (`NotifyIcon`), a right-click menu, a small settings window, and a borderless banner with very little code.
- **Windows calls:** plain P/Invoke into `user32.dll` and `dwmapi.dll`. No extra packages are needed.
- **Tests:** xUnit for the detection and matching logic, like the Mac version's `swift test` tests.
- **Editor:** Visual Studio 2022 Community (free) or VS Code with the C# Dev Kit.

Install the .NET 10 SDK, then check with:

```powershell
dotnet --version
```

## 6. Project layout

```
Blindspot-Windows/
  BUILD-GUIDE.md
  Blindspot.sln
  src/Blindspot/
    Blindspot.csproj
    Program.cs        tray icon, menu, timer, state display, banner
    Detection.cs      window list, display list, detection state (pure logic kept separate)
    Minimizing.cs     matching and minimizing
    Settings.cs       saved settings and the settings window
    LoginItem.cs      start at login
    Native.cs         the P/Invoke declarations
    Assets/blindspot.ico, blindspot-red.ico, blindspot-off.ico
  tests/Blindspot.Tests/
    Blindspot.Tests.csproj
    DetectionTests.cs
    MinimizingTests.cs
```

This mirrors the Mac files one to one, so a change on one side is easy to find on the other.

Create it with:

```powershell
dotnet new sln -n Blindspot
dotnet new winforms -n Blindspot -o src/Blindspot
dotnet new xunit -n Blindspot.Tests -o tests/Blindspot.Tests
dotnet sln add src/Blindspot tests/Blindspot.Tests
dotnet add tests/Blindspot.Tests reference src/Blindspot
```

In `Blindspot.csproj`, set the target to `net10.0-windows`, turn on `UseWindowsForms`, and add an app manifest that declares **per-monitor DPI awareness (PerMonitorV2)**.
Without it, window bounds are scaled and will not line up with screen bounds on high-DPI or mixed-DPI monitors.

## 7. How each part maps to Windows

| Mac piece | Windows equivalent |
|---|---|
| Menu bar icon (`NSStatusItem`) | Tray icon (`NotifyIcon`) with a `ContextMenuStrip` |
| Poll every 1 s | `System.Windows.Forms.Timer` with a 1000 ms interval |
| Window list (`CGWindowListCopyWindowInfo`) | `EnumWindows`, then `IsWindowVisible`, `GetWindowText`, `GetClassName`, `GetWindowThreadProcessId` |
| Window bounds | `DwmGetWindowAttribute` with `DWMWA_EXTENDED_FRAME_BOUNDS` (better than `GetWindowRect`, which includes invisible borders) |
| Owner app name | Process name from the process ID |
| "Normal window" (layer 0) | Not a tool window, not minimized (`IsIconic`), not cloaked (`DWMWA_CLOAKED`, which hides windows on other virtual desktops) |
| Display bounds | `Screen.AllScreens` (`Bounds`) |
| Screen Recording permission | Not needed |
| Accessibility minimize | `ShowWindow(hwnd, SW_MINIMIZE)`; no permission needed |
| Warning sound | `SystemSounds.Exclamation.Play()` |
| Banner (`NSPanel`) | A borderless, topmost `Form` that does not take focus (`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `ShowWithoutActivation = true`) |
| Settings (`UserDefaults`) | A small JSON file in `%APPDATA%\Blindspot\settings.json` |
| Start at login | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Blindspot` set to the exe path in quotes |

### Detection

Keep the detection a pure function, as on Mac: it takes the window list, the display list, the settings, and the pause flag, and returns a state.
Then tests can feed it fake windows and displays.
Tune the Teams process name, the share-marker title or class, and the slack value from your step-zero findings.

### Minimizing

On Windows you minimize by window handle, so the matching problem on Mac (finding the Accessibility window that matches the listed one) mostly goes away.
Still keep the safety rule: just before minimizing, re-read that handle's title and bounds.
If either changed, or the window is gone, skip it and report it as changed.
Note that Windows blocks a normal app from minimizing windows of an app running as administrator.
Teams normally does not run that way, but report a failure clearly if it happens.

### Banner placement

Pick a screen whose bounds do not overlap the shared area.
Place the banner centered near the top of that screen's working area (`Screen.WorkingArea`), about 420 by 56 pixels, red background, white bold text.
If every screen overlaps the shared area, skip the banner.

### Tray icon states

| State | Icon | 
|---|---|
| Running, nothing wrong | Filled shield |
| Paused or checks disabled | Outline shield |
| Observation failed | Orange shield |
| Chat exposed | Red shield |

Show the status text as the tray tooltip (Windows limits it to about 127 characters) and as the first, disabled line of the right-click menu.

## 8. Settings window

Match the Mac settings:

- **General:** Play warning sound (on by default), Start at login, Show Test Warning button.
  The test shows the banner and sound with sample text for a few seconds and never minimizes anything.
  It works only while checks are running and no Teams share is detected.
- **Detection:** which tabs to watch (Chat, Activity; both on by default), and Minimize exposed windows (off by default).
  If no tabs are selected, show "Checks disabled: no Teams tabs selected".
- **About:** version, link to the repo, and a short privacy note: everything runs locally, nothing is sent anywhere, no screen images are captured.

## 9. Build order

Build in small steps and test each before the next.

1. Step zero discovery (section 4). Record findings.
2. Detection logic with unit tests: whole-screen share, window share, no share, partial overlap, covered chat, no tabs selected.
3. Tray app that shows the state and tooltip, with pause and quit. No banner or minimizing yet.
4. Sound and banner, including the one-screen case.
5. Settings window and saved settings.
6. Minimizing, off by default, with the re-check before each minimize.
7. Start at login.
8. Icons.
9. Packaging and a GitHub release.
10. A live test with a real Teams share (section 11).

## 10. Building and shipping

Run during development:

```powershell
dotnet run --project src/Blindspot
dotnet test
```

Build a single exe that needs nothing else installed:

```powershell
dotnet publish src/Blindspot -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -o out/x64
dotnet publish src/Blindspot -c Release -r win-arm64 -p:PublishSingleFile=true --self-contained true -o out/arm64
```

Zip each folder as `Blindspot-win-x64.zip` and `Blindspot-win-arm64.zip`.
Most laptops need x64. Arm64 is for Snapdragon laptops and Windows VMs on Apple silicon Macs.

**Unsigned app warning:** Windows SmartScreen will show "Windows protected your PC" the first time.
Users click **More info**, then **Run anyway**.
Document this in an install guide, as the Mac docs do for Gatekeeper.
Signing is optional later. Azure Trusted Signing is the cheapest real option if it is ever wanted.

**CI:** a GitHub Actions workflow on `windows-latest` that runs `dotnet test` and the two `dotnet publish` commands, and attaches the zips to a release on a version tag.
Follow the Mac repo's CI rules: read-only permissions by default, actions pinned to full commit SHAs, and a pinned .NET SDK version through `global.json`.

**Optional:** a winget package later, the Windows counterpart of the Homebrew cask.

## 11. Live test checklist

A unit test passing is not proof. Check these with a real Teams share (a solo "Meet now" works):

- [ ] Not in a meeting: status says "No Teams share detected".
- [ ] Share a whole screen with Chat open on that screen: sound, red icon, banner on the other screen.
- [ ] Chat partly behind another app on the shared screen: still warns.
- [ ] Chat on the other screen only: no warning.
- [ ] Switch Teams to a non-watched tab: warning clears.
- [ ] Share a single window that is not Teams: no warning and nothing minimized.
- [ ] Minimizing on, whole-screen share: Chat window minimizes; restore it and it minimizes again on the next check.
- [ ] Minimizing on, window share: warns but never minimizes.
- [ ] One screen only: no banner; sound and red icon still work.
- [ ] Pause: icon goes to outline and warnings stop.
- [ ] Mixed DPI monitors (laptop screen plus external): banner lands on the right screen and detection still matches.
- [ ] Start at login works after a reboot, and turning it off removes it.
- [ ] Moving the exe to a new folder: Start at login still points to the right place, or clearly shows it needs fixing.
- [ ] Stop sharing: warning and banner clear within about a second.

## 12. Windows findings

Observed on 2026-10-30 with new Teams on Windows 10 (10.0.19045), using `tools/list-windows.ps1`
in a solo "Meet now" meeting. Setups: laptop screen alone (1920x1080, 100% scale) and three
monitors (1920x1080 at -1920,0; 2560x1440 primary at 0,0; 2560x1440 at 2560,0; all 100% scale).
Names in titles are replaced with placeholders.

- Teams process name: `ms-teams` (two processes). The share border window belongs to a separate
  `ms-teams` child process, so filter by process name, not by the main window's PID.
- Share marker window (whole-screen share): class `ScreenBorderWindow`, empty title, bounds equal
  to the shared monitor exactly (for example `0,0 1920x1080`, and `-1920,0 1920x1080` on a
  monitor left of the primary). It exists only while sharing and disappears when the share stops.
- Share marker during single-window share: the same `ScreenBorderWindow`, with bounds equal to
  the shared window exactly. It follows the window when it moves or resizes. A maximized window
  is `1920x1040` (screen height minus the taskbar), so a small match tolerance still separates it
  from a whole-screen share. In EnumWindows order, the border sits directly above the shared
  window (topmost during a whole-screen share).
- Chat tab window title pattern: `Chat | <Person> | Microsoft Teams`, class `TeamsWebView`.
- Activity tab window title pattern: not captured in these runs. Assumed to be
  `Activity | ... | Microsoft Teams` by analogy with Chat; verify.
- Pop-out chat window titles: `<Person> | Microsoft Teams`, class `TeamsWebView`. This has the same
  shape as the meeting window (`<meeting name> | Microsoft Teams`).
- Other Teams windows during a share: `Sharing control bar | Microsoft Teams` (small, topmost,
  `WS_EX_NOACTIVATE`) and sometimes `Meeting compact view | <Person> | Microsoft Teams`.
  Starting a share minimizes the main Teams window. Minimized windows report bounds near
  `-32000,-32000`.
- Anything else surprising:
  - Pop-out chats cannot be told apart from the meeting window using Win32 attributes. Both have
    style `0x14CF0000`, extended style `0x00000100`, no owner, the same process, and the same
    first-level children (`TeamsOverlay;Chrome_WidgetWin_0`). Their UI Automation names equal
    their titles, and their AutomationIds are empty. The only difference found was in the UI
    Automation tree: the meeting window contains a "Leave" button and chat windows do not.
    This is not used for detection, for three reasons: the button names are localized, the tree
    is built lazily (the first query can return no buttons), and each query takes about 40-120 ms
    per window, every second.
  - Overlap is not exposure on a window share: a window share sends only that window's content,
    so a Chat window on top of the shared window is not in the share. Detection therefore warns
    on a window share only when the shared window is the Chat window itself.
  - A Teams reinstall did not change the process name, class names or title patterns.

### Known limits

- Pop-out chat windows are not detected; close them before sharing.
- Only tested at 100% scale. Mixed-DPI setups are untested.
