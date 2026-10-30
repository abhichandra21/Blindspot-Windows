using System.Media;

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

// Tray icon, menu, the once-a-second check, and the warning (sound and
// banner). Minimizing comes in a later build step.
sealed class TrayApp : IDisposable
{
    readonly NotifyIcon icon = new();
    readonly ToolStripMenuItem statusItem = new() { Enabled = false };
    readonly ToolStripMenuItem pauseItem = new("Pause");
    readonly System.Windows.Forms.Timer timer = new() { Interval = Detection.PollIntervalMs };
    readonly Banner banner = new();
    readonly AppSettings settings = AppSettings.Load();
    SettingsForm? settingsForm;
    DateTime testWarningUntil;
    bool paused;
    GuardState state = GuardState.Of(GuardKind.NotSharing);

    public TrayApp()
    {
        pauseItem.Click += (_, _) => TogglePause();
        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(pauseItem);
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
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

    void OpenSettings()
    {
        if (settingsForm == null || settingsForm.IsDisposed)
            settingsForm = new SettingsForm(settings, Check, ShowTestWarning);
        settingsForm.Show();
        settingsForm.Activate();
    }

    // Shows the banner and plays the sound with sample text. Never minimizes.
    // Returns why nothing was shown, or null.
    string? ShowTestWarning()
    {
        Check();
        if (Detection.TestWarningBlocker(state.Kind) is string reason) return reason;
        if (!banner.ShowOn("Test: Teams Chat may be visible in your screen share", default))
            return "No screen available to show the test banner on.";
        if (settings.PlaySound) SystemSounds.Exclamation.Play();
        testWarningUntil = DateTime.UtcNow.AddMilliseconds(Detection.TestWarningMs);
        return null;
    }

    void Check()
    {
        bool wasExposed = state.Kind == GuardKind.ChatExposed;
        state = Detection.CurrentState(paused, settings.RiskyTabs, Native.VisibleWindows, ScreenBounds);
        Show(state);
        if (state.Kind == GuardKind.ChatExposed)
        {
            // Bounds overlap only, so a chat hidden behind another window still
            // counts. Hence "may be".
            testWarningUntil = default;
            if (!wasExposed && settings.PlaySound) SystemSounds.Exclamation.Play();
            banner.ShowOn($"Teams {Detection.TabName(state.Exposed[0])} may be visible in your screen share", state.SharedArea);
        }
        else if (state.Kind != GuardKind.NotSharing || DateTime.UtcNow >= testWarningUntil)
        {
            // Any change away from "not sharing" ends a test warning at once.
            testWarningUntil = default;
            banner.Hide();
        }
    }

    static IReadOnlyList<Rect>? ScreenBounds() => Screen.AllScreens.Select(s => ToRect(s.Bounds)).ToList();

    public static Rect ToRect(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

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
        banner.Dispose();
        settingsForm?.Dispose();
        icon.Visible = false;
        icon.Dispose();
    }
}

// A small red banner, shown only on a screen that is not being shared.
// "Exclude from capture" is not relied on to hide it, so with no unshared
// screen we skip the banner and rely on sound and icon.
sealed class Banner : Form
{
    const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    readonly Label label = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.White,
        Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 11f, FontStyle.Bold),
    };

    public Banner()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(215, 38, 38);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(420, 56);
        Controls.Add(label);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    // Returns false when every screen overlaps the shared area.
    public bool ShowOn(string text, Rect avoid)
    {
        var screen = Screen.AllScreens.FirstOrDefault(s => TrayApp.ToRect(s.Bounds).Intersect(avoid).IsEmpty);
        if (screen == null)
        {
            Hide();
            return false;
        }
        label.Text = text;
        PlaceOn(screen);
        if (!Visible) Show();
        // Moving to a screen with another scale resizes the form, so center again.
        PlaceOn(screen);
        return true;
    }

    void PlaceOn(Screen screen)
    {
        var area = screen.WorkingArea;
        Location = new Point(area.X + (area.Width - Width) / 2, area.Y + 12);
    }
}
