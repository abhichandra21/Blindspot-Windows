namespace Blindspot;

// Minimizing works by window handle on Windows, so there is no matching step
// like on Mac. The safety rule stays: re-read the window just before acting.
// Like Detection.cs, no Windows-only code here; the tray app passes in the
// native calls.

public enum MinimizeOutcome { Minimized, SkippedChanged, Failed }

// A window as re-read just before minimizing.
public readonly record struct CurrentWindow(string Title, Rect Bounds);

public static class Minimizing
{
    // Acts only on a window whose title and bounds are unchanged since the
    // check that found it. A changed or closed window is left alone and tried
    // again on the next check.
    public static List<MinimizeOutcome> Minimize(IReadOnlyList<WindowInfo> targets,
        Func<nint, CurrentWindow?> read, Func<nint, bool> minimize) =>
        targets.Select(t =>
        {
            var now = read(t.Handle);
            if (now == null || now.Value.Title != t.Title || now.Value.Bounds != t.Bounds)
                return MinimizeOutcome.SkippedChanged;
            return minimize(t.Handle) ? MinimizeOutcome.Minimized : MinimizeOutcome.Failed;
        }).ToList();

    // A short reason shown under the warning when minimizing did not happen;
    // null when every window was minimized. A failure wins over a skip.
    public static string? Note(IReadOnlyList<MinimizeOutcome> outcomes)
    {
        // Windows blocks this when Teams runs as administrator and Blindspot does not.
        if (outcomes.Contains(MinimizeOutcome.Failed)) return "Not minimized: Windows refused to minimize Teams";
        if (outcomes.Contains(MinimizeOutcome.SkippedChanged)) return "Not minimized: the Teams window changed";
        return null;
    }
}
