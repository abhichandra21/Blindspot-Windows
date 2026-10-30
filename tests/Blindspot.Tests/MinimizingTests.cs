namespace Blindspot.Tests;

public class MinimizingTests
{
    static readonly WindowInfo Chat =
        new(42, "ms-teams", "Chat | Person | Microsoft Teams", "TeamsWebView", new(100, 100, 800, 600), true);

    static (List<MinimizeOutcome> Outcomes, List<nint> Minimized) Run(CurrentWindow? now, bool minimizeWorks = true)
    {
        var minimized = new List<nint>();
        var outcomes = Minimizing.Minimize([Chat], _ => now, h => { minimized.Add(h); return minimizeWorks; });
        return (outcomes, minimized);
    }

    [Fact]
    public void UnchangedWindowIsMinimized()
    {
        var (outcomes, minimized) = Run(new CurrentWindow(Chat.Title, Chat.Bounds));
        Assert.Equal([MinimizeOutcome.Minimized], outcomes);
        Assert.Equal([Chat.Handle], minimized);
        Assert.Null(Minimizing.Note(outcomes));
    }

    [Fact]
    public void MovedWindowIsLeftAlone()
    {
        var (outcomes, minimized) = Run(new CurrentWindow(Chat.Title, new(150, 100, 800, 600)));
        Assert.Equal([MinimizeOutcome.SkippedChanged], outcomes);
        Assert.Empty(minimized);
        Assert.Equal("Not minimized: the Teams window changed", Minimizing.Note(outcomes));
    }

    [Fact]
    public void RetitledWindowIsLeftAlone()
    {
        var (outcomes, minimized) = Run(new CurrentWindow("Calendar | Microsoft Teams", Chat.Bounds));
        Assert.Equal([MinimizeOutcome.SkippedChanged], outcomes);
        Assert.Empty(minimized);
    }

    [Fact]
    public void ClosedWindowIsLeftAlone()
    {
        var (outcomes, minimized) = Run(null);
        Assert.Equal([MinimizeOutcome.SkippedChanged], outcomes);
        Assert.Empty(minimized);
    }

    [Fact]
    public void RefusedMinimizeIsReportedAndWinsOverSkip()
    {
        var (outcomes, _) = Run(new CurrentWindow(Chat.Title, Chat.Bounds), minimizeWorks: false);
        Assert.Equal([MinimizeOutcome.Failed], outcomes);
        Assert.Equal("Not minimized: Windows refused to minimize Teams",
            Minimizing.Note([MinimizeOutcome.SkippedChanged, MinimizeOutcome.Failed]));
    }
}
