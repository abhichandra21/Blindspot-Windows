namespace Blindspot;

// Blindspot: warns when a Teams chat window is visible in what you are
// screen sharing from Teams.
//
// How it detects things (found by watching the window list during a real share):
// - While sharing, Teams (ms-teams.exe) shows a window of class
//   "ScreenBorderWindow" that covers exactly the shared area: the whole
//   monitor for a screen share, the window for a window share. It follows the
//   shared window when it moves or resizes.
// - The main Teams window title starts with the current tab, e.g. "Chat | ..."
//   or "Activity | ...". Those tabs can show message content.
//
// This file has no Windows-only dependencies so the tests can run anywhere.

public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public long Area => (long)Width * Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public Rect Intersect(Rect other)
    {
        int left = Math.Max(X, other.X), top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right), bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : default;
    }
}

// IsNormal: an ordinary app window, not minimized, not a tool window, and not
// cloaked (on another virtual desktop). Minimized Teams windows sit at -32000.
public sealed record WindowInfo(nint Handle, string Process, string Title, string ClassName, Rect Bounds, bool IsNormal);

public enum GuardKind { Paused, ChecksDisabled, ObservationFailed, NotSharing, SharingClear, ChatExposed }

public sealed record GuardState(GuardKind Kind, IReadOnlyList<WindowInfo> Exposed, Rect SharedArea, bool WholeScreen)
{
    public static GuardState Of(GuardKind kind) => new(kind, [], default, false);

    // Only a whole-screen share is clear enough to act on. A window share
    // keeps the warning, since overlap alone does not prove the chat is the
    // shared window.
    public IReadOnlyList<WindowInfo> AutoMinimizeTargets =>
        Kind == GuardKind.ChatExposed && WholeScreen ? Exposed : [];

    public string StatusText => Kind switch
    {
        GuardKind.Paused => "Paused",
        GuardKind.ChecksDisabled => "Checks disabled: no Teams tabs selected",
        GuardKind.ObservationFailed => "Unable to check Teams sharing",
        GuardKind.NotSharing => "No Teams share detected",
        GuardKind.SharingClear => "Sharing; no watched Teams windows detected in the shared area",
        _ => $"Teams {Detection.TabName(Exposed[0])} may be visible in your share",
    };
}

public static class Detection
{
    public const string TeamsProcess = "ms-teams";
    public const string ShareBorderClass = "ScreenBorderWindow";
    public static readonly string[] AllRiskyTabs = ["Chat", "Activity"];
    public const int PollIntervalMs = 1000;

    // On Windows the border matches the shared screen or window exactly, so keep
    // the slack small: a shared maximized window is only the taskbar height
    // (about 40 px) short of its screen and must not count as a screen share.
    public const int BoundsMatchSlackPixels = 8;

    public static bool SameBounds(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < BoundsMatchSlackPixels
        && Math.Abs(a.Y - b.Y) < BoundsMatchSlackPixels
        && Math.Abs(a.Right - b.Right) < BoundsMatchSlackPixels
        && Math.Abs(a.Bottom - b.Bottom) < BoundsMatchSlackPixels;

    public static bool IsWholeScreen(Rect rect, IReadOnlyList<Rect> screens) =>
        screens.Any(s => SameBounds(s, rect));

    public static GuardState CurrentState(bool paused, IReadOnlyList<string> riskyTabs,
        Func<IReadOnlyList<WindowInfo>?> readWindows, Func<IReadOnlyList<Rect>?> readScreens)
    {
        if (paused) return GuardState.Of(GuardKind.Paused);
        if (riskyTabs.Count == 0) return GuardState.Of(GuardKind.ChecksDisabled);
        var observed = readWindows();
        if (observed == null) return GuardState.Of(GuardKind.ObservationFailed);
        var windows = observed
            .Where(w => string.Equals(w.Process, TeamsProcess, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var border = windows.FirstOrDefault(w => w.ClassName == ShareBorderClass && !w.Bounds.IsEmpty);
        if (border == null) return GuardState.Of(GuardKind.NotSharing);
        var shared = border.Bounds;
        var screens = readScreens();
        if (screens == null) return GuardState.Of(GuardKind.ObservationFailed);
        bool wholeScreen = IsWholeScreen(shared, screens);
        var exposed = windows
            .Where(w => w.IsNormal && riskyTabs.Any(tab => w.Title.StartsWith(tab, StringComparison.Ordinal)))
            // Screen share: any part of the chat on that screen is visible.
            // Window share: only that window's content is sent, so flag the chat
            // only if it is the shared window itself.
            .Where(w => wholeScreen ? !w.Bounds.Intersect(shared).IsEmpty : SameBounds(w.Bounds, shared))
            .ToList();
        return exposed.Count == 0
            ? new GuardState(GuardKind.SharingClear, [], shared, wholeScreen)
            : new GuardState(GuardKind.ChatExposed, exposed, shared, wholeScreen);
    }

    public const int TestWarningMs = 3000;

    // The test warning is shown only while checks are running and see no Teams
    // share, so a share that starts afterwards changes the state and hides it.
    // Returns why it can't be shown, or null.
    public static string? TestWarningBlocker(GuardKind kind) => kind switch
    {
        GuardKind.NotSharing => null,
        GuardKind.ChatExposed => "A real warning is showing.",
        GuardKind.SharingClear => "Stop sharing to show a test warning.",
        GuardKind.Paused or GuardKind.ChecksDisabled => "Resume checks to show a test warning.",
        _ => "Can't show a test warning without confirming no screen is being shared.",
    };

    public static string TabName(WindowInfo w)
    {
        int i = w.Title.IndexOf(" |", StringComparison.Ordinal);
        return i >= 0 ? w.Title[..i] : w.Title;
    }
}
