using System.Drawing.Drawing2D;
using System.Media;

namespace Blindspot;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Run by the uninstaller so start at login doesn't point at a deleted exe.
        if (args.Contains("--remove-login-item"))
        {
            LoginItem.SetEnabled(false);
            return;
        }
        // One tray icon per user session, even if started again from the Start menu.
        using var single = new Mutex(true, @"Local\Blindspot", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        using var app = new TrayApp();
        // Lets the installer's Restart Manager close the app before replacing it,
        // and removes the tray icon cleanly at sign-out.
        Microsoft.Win32.SystemEvents.SessionEnded += (_, _) => Application.Exit();
        Application.Run();
    }
}

// Tray icon, menu, the once-a-second check, the warning (sound and banner),
// and optional minimizing.
sealed class TrayApp : IDisposable
{
    readonly NotifyIcon icon = new();
    readonly ToolStripMenuItem statusItem = new() { Enabled = false };
    readonly ToolStripMenuItem minimizeNoteItem = new() { Enabled = false, Visible = false };
    readonly ToolStripMenuItem pauseItem = new("Pause");
    readonly System.Windows.Forms.Timer timer = new() { Interval = Detection.PollIntervalMs };
    readonly Banner banner = new();
    readonly AppSettings settings = AppSettings.Load();
    SettingsForm? settingsForm;
    DateTime testWarningUntil;
    bool paused;
    string? minimizeNote;
    GuardState state = GuardState.Of(GuardKind.NotSharing);

    public TrayApp()
    {
        pauseItem.Click += (_, _) => TogglePause();
        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(minimizeNoteItem);
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
            // After the warning, so a slow minimize never delays it. Retried on
            // every check while the chat stays exposed.
            var targets = settings.AutoMinimize ? state.AutoMinimizeTargets : [];
            SetMinimizeNote(targets.Count == 0 ? null : Minimizing.Note(Minimizing.Minimize(targets, Native.Read, Native.Minimize)));
        }
        else if (state.Kind != GuardKind.NotSharing || DateTime.UtcNow >= testWarningUntil)
        {
            // Any change away from "not sharing" ends a test warning at once.
            testWarningUntil = default;
            banner.Hide();
        }
        if (state.Kind != GuardKind.ChatExposed) SetMinimizeNote(null);
    }

    void SetMinimizeNote(string? note)
    {
        if (note == minimizeNote) return;
        minimizeNote = note;
        minimizeNoteItem.Text = note ?? "";
        minimizeNoteItem.Visible = note != null;
        Show(state);
    }

    static IReadOnlyList<Rect>? ScreenBounds() => Screen.AllScreens.Select(s => ToRect(s.Bounds)).ToList();

    public static Rect ToRect(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    void Show(GuardState s)
    {
        statusItem.Text = s.StatusText;
        string text = minimizeNote == null ? s.StatusText : $"{s.StatusText}\n{minimizeNote}";
        // NotifyIcon.Text is limited to 127 characters.
        icon.Text = text.Length > 127 ? text[..127] : text;
        icon.Icon = s.Kind switch
        {
            GuardKind.ChatExposed => TrayIcons.Exposed,
            GuardKind.ObservationFailed => TrayIcons.Failed,
            GuardKind.Paused or GuardKind.ChecksDisabled => TrayIcons.Off,
            _ => TrayIcons.Running,
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
    const int CS_DROPSHADOW = 0x20000;

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

    // Rounded corners on Windows 11; Windows 10 ignores this and keeps square ones.
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseRoundedCorners(Handle);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            cp.ClassStyle |= CS_DROPSHADOW;
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

// Tray icons drawn at startup in the app icon's style, a white shield on a
// colored tile, sized for the tray at the primary screen's scale: blue while
// checking, gray while paused or disabled, orange when checks fail, red for
// a warning.
static class TrayIcons
{
    public static readonly Icon Running = Tile(Color.FromArgb(59, 122, 237), Color.FromArgb(23, 59, 158));
    public static readonly Icon Off = Tile(Color.FromArgb(150, 150, 150), Color.FromArgb(105, 105, 105));
    public static readonly Icon Failed = Tile(Color.FromArgb(250, 140, 40), Color.FromArgb(214, 90, 10));
    public static readonly Icon Exposed = Tile(Color.FromArgb(235, 60, 50), Color.FromArgb(180, 25, 20));

    static Icon Tile(Color top, Color bottom)
    {
        float dpi;
        using (var screen = Graphics.FromHwnd(0)) dpi = screen.DpiX;
        int size = (int)Math.Round(16 * dpi / 96);
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = size;
            using (var tile = RoundedRect(new RectangleF(0, 0, s, s), 0.2f * s))
            using (var gradient = new LinearGradientBrush(new PointF(0, 0), new PointF(0, s), top, bottom))
                g.FillPath(gradient, tile);
            // Same shield as assets/make-icon.swift, drawn larger to read at tray size.
            PointF P(float x, float y) => new((0.5f + (x - 0.5f) * 1.3f) * s, (0.5f + (y - 0.5f) * 1.3f) * s);
            using var shield = new GraphicsPath();
            shield.AddBezier(P(0.5f, 0.22f), P(0.58f, 0.26f), P(0.66f, 0.29f), P(0.74f, 0.30f));
            shield.AddLine(P(0.74f, 0.30f), P(0.74f, 0.48f));
            shield.AddBezier(P(0.74f, 0.48f), P(0.74f, 0.64f), P(0.62f, 0.74f), P(0.5f, 0.80f));
            shield.AddBezier(P(0.5f, 0.80f), P(0.38f, 0.74f), P(0.26f, 0.64f), P(0.26f, 0.48f));
            shield.AddLine(P(0.26f, 0.48f), P(0.26f, 0.30f));
            shield.AddBezier(P(0.26f, 0.30f), P(0.34f, 0.29f), P(0.42f, 0.26f), P(0.5f, 0.22f));
            shield.CloseFigure();
            g.FillPath(Brushes.White, shield);
        }
        nint handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
