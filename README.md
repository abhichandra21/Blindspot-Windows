# Blindspot for Windows

A Windows tray app that warns you when a Microsoft Teams chat may be visible in your Teams screen share, and can minimize it for you.
It is the Windows version of [Blindspot for Mac](https://github.com/abhichandra21/Blindspot).

If a Teams Chat or Activity window is on the screen you share, everyone in the meeting can read it.
Blindspot checks about once a second, so it notices within a second or so, then:

- plays a warning sound and turns its tray shield red,
- shows a red banner on a screen you are *not* sharing,
- and, if you turn on minimizing, minimizes those Teams windows when you share a whole screen.

Needs Windows 10 or 11 and the new Microsoft Teams.

## Install

1. Download `Blindspot-win-x64.msi` from [Releases](../../releases).
   Use `Blindspot-win-arm64.msi` on a Snapdragon (Arm) laptop.
2. Double-click it.
   Windows may say **Windows protected your PC**, because the app is not code-signed.
   Click **More info**, then **Run anyway**.
3. Blindspot installs for your user only, so it needs no admin rights.
   It starts when the install finishes; look for the blue shield in the tray.
   If it's hidden, click the **^** arrow next to the clock and drag it onto the taskbar.
4. To open it at sign-in, turn on **Start at login** in **Settings > General**.

Updates install the same way, over the old version, and keep your settings.
To uninstall, use **Settings > Apps > Installed apps > Blindspot**.

There is also a zip with just `Blindspot.exe` if you'd rather not install anything.
Put it in a folder you keep, since Start at login remembers where it is.

## Use it

- **Click** the shield to open the Blindspot panel: what it sees right now, a switch to pause or resume checks, Settings and Quit.
  It follows the Windows light or dark mode.
- **Right-click** for a quick menu with the same actions.
- Minimizing is off until you turn it on in **Settings > Detection**.
- To see what a warning looks like, click **Show Test Warning** in **Settings > General**.
  It shows the banner and sound with sample text for a few seconds and never minimizes anything.
  It works only while checks are running and no Teams share is detected.

## What the shield says

Click the shield, hold the pointer over it, or right-click it to read its status.

| Status | Shield |
|---|---|
| Paused | Gray |
| Checks disabled: no Teams tabs selected | Gray |
| Unable to check Teams sharing | Orange |
| No Teams share detected | Blue |
| Sharing; no watched Teams windows detected in the shared area | Blue |
| Teams Chat may be visible in your share | Red |
| Teams Activity may be visible in your share | Red |

For whole-screen shares, when minimizing is on and it didn't happen, a second line says why:

- Not minimized: the Teams window changed (it moved or switched tabs; Blindspot tries again a second later)
- Not minimized: Windows refused to minimize Teams (usually Teams running as administrator)

The warning stays in place in every case.

## Privacy

- Blindspot reads window titles and positions on your PC.
  It never captures images of your screen, uploads anything, or uses the network.
- It stores its settings in `%APPDATA%\Blindspot\settings.json`, and Start at login as the `Blindspot` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Known limits

- Teams only.
  Shares from Zoom, Slack, or a browser are not detected.
- A chat fully hidden behind other windows still triggers the warning during a whole-screen share, so it says the chat *may* be visible.
- When you share a single window, Blindspot warns only if that window is the Teams chat itself, and never minimizes anything.
- With one screen there is nowhere safe for the banner, so you get the sound and red icon only.
- It relies on how the new Teams names its windows ("Chat | ..." and "Activity | ..." in English), not a documented API, so a Teams update or another language could break detection.
- A chat popped out into its own window is not detected.
  Close pop-out chats before sharing.
  See [#1](../../issues/1).
- Teams notification pop-ups are not detected.
  Teams can turn them off while you share: see [Change your status in Microsoft Teams](https://support.microsoft.com/en-us/teams/notifications-settings/change-your-status-in-microsoft-teams) for **Set my status to Do not disturb when I'm sharing my screen**.

## Build it yourself

Needs the .NET 10 SDK.

```powershell
dotnet test
dotnet run --project src/Blindspot
.\installer\build.ps1 -Version 0.1.0 -Rid win-x64   # exe, zip and MSI in out\
```

The unit tests also run on a Mac.
[BUILD-GUIDE.md](BUILD-GUIDE.md) explains how detection works and what was found on Windows.
Pushing a `v*` tag builds both MSIs and zips and attaches them to a GitHub release.

## License

[MIT](LICENSE).
