namespace Blindspot;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var app = new TrayApp();
        Application.Run();
    }
}

// Tray icon, menu and the once-a-second check. Banner, sound and minimizing
// come in later build steps.
sealed class TrayApp : IDisposable
{
    readonly NotifyIcon icon = new();
    readonly ToolStripMenuItem statusItem = new() { Enabled = false };
    readonly ToolStripMenuItem pauseItem = new("Pause");
    readonly System.Windows.Forms.Timer timer = new() { Interval = Detection.PollIntervalMs };
    bool paused;
    GuardState state = GuardState.Of(GuardKind.NotSharing);

    public TrayApp()
    {
        pauseItem.Click += (_, _) => TogglePause();
        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(pauseItem);
        menu.Items.Add("Quit", null, (_, _) => Application.Exit());
        icon.ContextMenuStrip = menu;
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePause(); };
        timer.Tick += (_, _) => Check();
        Check();
        icon.Visible = true;
        timer.Start();
    }

    void TogglePause()
    {
        paused = !paused;
        pauseItem.Text = paused ? "Resume" : "Pause";
        Check();
    }

    void Check()
    {
        state = Detection.CurrentState(paused, Detection.AllRiskyTabs, Native.VisibleWindows, ScreenBounds);
        Show(state);
    }

    static IReadOnlyList<Rect>? ScreenBounds() =>
        Screen.AllScreens.Select(s => new Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToList();

    void Show(GuardState s)
    {
        string text = s.StatusText;
        statusItem.Text = text;
        // NotifyIcon.Text is limited to 127 characters.
        icon.Text = text.Length > 127 ? text[..127] : text;
        icon.Icon = s.Kind switch
        {
            GuardKind.ChatExposed => SystemIcons.Error,
            GuardKind.ObservationFailed => SystemIcons.Warning,
            _ => SystemIcons.Shield,
        };
    }

    public void Dispose()
    {
        timer.Dispose();
        icon.Visible = false;
        icon.Dispose();
    }
}
