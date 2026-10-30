namespace Blindspot.Tests;

public class DetectionTests
{
    static readonly Rect Screen1 = new(0, 0, 1920, 1080);
    static readonly Rect Screen2 = new(1920, 0, 2560, 1440);
    static readonly IReadOnlyList<Rect> TwoScreens = [Screen1, Screen2];

    static WindowInfo Teams(string title, Rect bounds, bool normal = true) =>
        new(1, "ms-teams", title, "TeamsWebView", bounds, normal);

    static WindowInfo Border(Rect bounds) =>
        new(2, "ms-teams", "", Detection.ShareBorderClass, bounds, false);

    static GuardState Run(IReadOnlyList<WindowInfo>? windows, IReadOnlyList<string>? tabs = null,
        bool paused = false, IReadOnlyList<Rect>? screens = null) =>
        Detection.CurrentState(paused, tabs ?? Detection.AllRiskyTabs, () => windows, () => screens ?? TwoScreens);

    [Fact]
    public void WholeScreenShareWithChatOnThatScreenIsExposed()
    {
        var chat = Teams("Chat | Person | Microsoft Teams", new(227, 104, 1465, 825));
        var s = Run([chat, Border(Screen1)]);
        Assert.Equal(GuardKind.ChatExposed, s.Kind);
        Assert.True(s.WholeScreen);
        Assert.Equal([chat], s.AutoMinimizeTargets);
        Assert.Equal("Teams Chat may be visible in your share", s.StatusText);
    }

    [Fact]
    public void WholeScreenShareCountsAPartlyOnScreenChat()
    {
        // Mostly on the other screen; a sliver on the shared one still counts.
        var chat = Teams("Chat | Person | Microsoft Teams", new(1800, 100, 1000, 800));
        Assert.Equal(GuardKind.ChatExposed, Run([chat, Border(Screen1)]).Kind);
    }

    [Fact]
    public void WholeScreenShareWithChatOnOtherScreenIsClear()
    {
        var chat = Teams("Chat | Person | Microsoft Teams", new(2000, 100, 1000, 800));
        var s = Run([chat, Border(Screen1)]);
        Assert.Equal(GuardKind.SharingClear, s.Kind);
        Assert.Equal(Screen1, s.SharedArea);
    }

    [Fact]
    public void SingleLaptopScreenShares()
    {
        IReadOnlyList<Rect> laptop = [Screen1];
        var chat = Teams("Chat | Person | Microsoft Teams", new(227, 104, 1465, 825));
        var screenShare = Run([chat, Border(Screen1)], screens: laptop);
        Assert.Equal(GuardKind.ChatExposed, screenShare.Kind);
        Assert.True(screenShare.WholeScreen);
        var chromeShare = Run([chat, Border(new(320, 137, 1368, 643))], screens: laptop);
        Assert.Equal(GuardKind.SharingClear, chromeShare.Kind);
        Assert.Equal(GuardKind.NotSharing, Run([chat], screens: laptop).Kind);
    }

    [Fact]
    public void ScreenShareOnLeftMonitorWithNegativeCoordinates()
    {
        // Layout seen on the test laptop: a monitor left of the primary one.
        IReadOnlyList<Rect> screens = [new(-1920, 0, 1920, 1080), new(0, 0, 2560, 1440), new(2560, 0, 2560, 1440)];
        var left = new Rect(-1920, 0, 1920, 1080);
        var chatOnPrimary = Teams("Chat | Person | Microsoft Teams", new(100, 100, 1274, 680));
        var chatOnLeft = Teams("Chat | Person | Microsoft Teams", new(-1500, 100, 1274, 680));
        Assert.Equal(GuardKind.SharingClear, Run([chatOnPrimary, Border(left)], screens: screens).Kind);
        var s = Run([chatOnLeft, Border(left)], screens: screens);
        Assert.Equal(GuardKind.ChatExposed, s.Kind);
        Assert.True(s.WholeScreen);
    }

    [Fact]
    public void BorderAFewPixelsOffStillCountsAsWholeScreen()
    {
        var chat = Teams("Chat | Person | Microsoft Teams", new(100, 100, 800, 600));
        var s = Run([chat, Border(new(2, 2, 1916, 1076))]);
        Assert.True(s.WholeScreen);
    }

    [Fact]
    public void WindowShareOfTheChatItselfIsExposedButNotMinimized()
    {
        var chat = Teams("Chat | Person | Microsoft Teams", new(200, 100, 1200, 800));
        var s = Run([chat, Border(new(200, 100, 1200, 800))]);
        Assert.Equal(GuardKind.ChatExposed, s.Kind);
        Assert.False(s.WholeScreen);
        Assert.Empty(s.AutoMinimizeTargets);
    }

    [Fact]
    public void MaximizedWindowShareIsNotWholeScreen()
    {
        // Seen on Windows: a shared maximized window stops at the taskbar, 40 px
        // short of the screen. Treating it as a screen share would allow minimizing.
        var chat = Teams("Chat | Person | Microsoft Teams", new(0, 0, 1920, 1040));
        var s = Run([chat, Border(new(0, 0, 1920, 1040))]);
        Assert.Equal(GuardKind.ChatExposed, s.Kind);
        Assert.False(s.WholeScreen);
        Assert.Empty(s.AutoMinimizeTargets);
    }

    [Fact]
    public void WindowShareWithChatOnTopOfSharedWindowIsClear()
    {
        // Seen on Windows: a window share sends only that window's content, so a
        // chat covering most of it is not in the share.
        var chat = Teams("Chat | Person | Microsoft Teams", new(2772, 279, 1274, 680));
        var s = Run([chat, Border(new(2814, 423, 1368, 643))]);
        Assert.Equal(GuardKind.SharingClear, s.Kind);
    }

    [Fact]
    public void WindowShareWithSmallChatOverlapIsClear()
    {
        var chat = Teams("Chat | Person | Microsoft Teams", new(0, 0, 300, 300));
        var s = Run([chat, Border(new(200, 100, 1200, 800))]);
        Assert.Equal(GuardKind.SharingClear, s.Kind);
        Assert.False(s.WholeScreen);
    }

    [Fact]
    public void NoBorderMeansNotSharing()
    {
        var chat = Teams("Chat | Person | Microsoft Teams", new(0, 0, 800, 600));
        Assert.Equal(GuardKind.NotSharing, Run([chat]).Kind);
    }

    [Fact]
    public void BorderFromAnotherProcessIsIgnored()
    {
        var chat = Teams("Chat | Person | Microsoft Teams", new(0, 0, 800, 600));
        var other = new WindowInfo(3, "notepad", "", Detection.ShareBorderClass, Screen1, false);
        Assert.Equal(GuardKind.NotSharing, Run([chat, other]).Kind);
    }

    [Fact]
    public void MinimizedChatIsIgnored()
    {
        // Seen on Windows: Teams minimizes its main window when a share starts.
        var chat = Teams("Calendar | Microsoft Teams", new(-31993, -32000, 146, 28), normal: false);
        var minimizedChat = Teams("Chat | Person | Microsoft Teams", new(0, 0, 800, 600), normal: false);
        Assert.Equal(GuardKind.SharingClear, Run([chat, minimizedChat, Border(Screen1)]).Kind);
    }

    [Fact]
    public void CoveredChatStillWarnsOnWholeScreenShare()
    {
        // No occlusion logic by design: a chat under other apps still counts.
        var chat = Teams("Chat | Person | Microsoft Teams", new(100, 100, 800, 600));
        var editor = new WindowInfo(4, "Code", "editor", "Chrome_WidgetWin_1", Screen1, true);
        Assert.Equal(GuardKind.ChatExposed, Run([editor, chat, Border(Screen1)]).Kind);
    }

    [Fact]
    public void NonRiskyTeamsTabsAreIgnored()
    {
        var calendar = Teams("Calendar | Microsoft Teams", new(100, 100, 800, 600));
        var meeting = Teams("Meeting compact view | Person | Microsoft Teams", new(455, 249, 209, 334));
        var bar = Teams("Sharing control bar | Microsoft Teams", new(797, 8, 327, 39));
        Assert.Equal(GuardKind.SharingClear, Run([calendar, meeting, bar, Border(Screen1)]).Kind);
    }

    [Fact]
    public void ActivityTabWarnsAndCanBeTurnedOff()
    {
        var activity = Teams("Activity | Microsoft Teams", new(100, 100, 800, 600));
        var s = Run([activity, Border(Screen1)]);
        Assert.Equal("Teams Activity may be visible in your share", s.StatusText);
        Assert.Equal(GuardKind.SharingClear, Run([activity, Border(Screen1)], tabs: ["Chat"]).Kind);
    }

    [Fact]
    public void NoTabsSelectedDisablesChecks()
    {
        Assert.Equal(GuardKind.ChecksDisabled, Run([], tabs: []).Kind);
    }

    [Fact]
    public void PausedWinsOverEverything()
    {
        var chat = Teams("Chat | Person | Microsoft Teams", new(0, 0, 800, 600));
        Assert.Equal(GuardKind.Paused, Run([chat, Border(Screen1)], paused: true).Kind);
    }

    [Fact]
    public void UnreadableWindowListIsObservationFailure()
    {
        Assert.Equal(GuardKind.ObservationFailed, Run(null).Kind);
    }

    [Fact]
    public void TabNameIsTitleBeforeFirstBar()
    {
        Assert.Equal("Chat", Detection.TabName(Teams("Chat | Person | Microsoft Teams", default)));
        Assert.Equal("Chat", Detection.TabName(Teams("Chat", default)));
    }
}
