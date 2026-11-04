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
        using var app = new TrayApp(justInstalled: args.Contains("--installed"));
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
    readonly Flyout flyout;
    SettingsForm? settingsForm;
    DateTime testWarningUntil;
    bool paused;
    string? minimizeNote;
    GuardState state = GuardState.Of(GuardKind.NotSharing);

    public TrayApp(bool justInstalled)
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
        flyout = new Flyout(TrayIcons.AppIcon(32), TogglePause, OpenSettings, Application.Exit);
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleFlyout(); };
        timer.Tick += (_, _) => Check();
        Check();
        icon.Visible = true;
        timer.Start();
        // The install itself is silent, so say where the app went.
        if (justInstalled)
            icon.ShowBalloonTip(5000, "Blindspot is running",
                "It lives in the tray and warns you if a Teams chat may be visible in your screen share. Right-click the icon for Settings.",
                ToolTipIcon.None);
    }

    void ToggleFlyout()
    {
        // Clicking the icon while the panel is open first takes focus from it,
        // which already closed it; don't reopen it straight away.
        if (flyout.Visible || DateTime.UtcNow - flyout.LastHidden < TimeSpan.FromMilliseconds(300))
        {
            flyout.Hide();
            return;
        }
        flyout.ShowNearTray();
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
        icon.Icon = TrayIcons.For(s.Kind);
        flyout.UpdateState(s, settings, minimizeNote);
    }

    public void Dispose()
    {
        timer.Dispose();
        banner.Dispose();
        flyout.Dispose();
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

// Tray icons in the Mac menu bar style: one screen outline that never changes,
// filled or empty like a cup, so every state reads as the same app. Watching
// fills the screen with an eye cut out of it; paused or disabled leaves it
// empty; unable to check adds an orange dot; a warning turns it red. The ink
// follows the light or dark taskbar.
static class TrayIcons
{
    static readonly Color Red = Color.FromArgb(235, 60, 50);
    static readonly Color Orange = Color.FromArgb(250, 140, 40);
    static readonly Dictionary<(GuardKind, bool), Icon> cache = [];

    // Read on every check, so a theme switch shows within a second.
    public static Icon For(GuardKind kind)
    {
        bool dark = Theme.Current().IsDark;
        if (!cache.TryGetValue((kind, dark), out var icon))
        {
            Color ink = dark ? Color.White : Color.FromArgb(26, 26, 26);
            cache[(kind, dark)] = icon = kind switch
            {
                GuardKind.ChatExposed => Draw(Red, filled: true, dot: false),
                GuardKind.ObservationFailed => Draw(ink, filled: false, dot: true),
                GuardKind.Paused or GuardKind.ChecksDisabled => Draw(ink, filled: false, dot: false),
                _ => Draw(ink, filled: true, dot: false),
            };
        }
        return icon;
    }

    // The same colors for the status dot in the flyout.
    public static Color StateColor(GuardKind kind) => kind switch
    {
        GuardKind.ChatExposed => Color.FromArgb(220, 45, 35),
        GuardKind.ObservationFailed => Color.FromArgb(240, 120, 25),
        GuardKind.Paused or GuardKind.ChecksDisabled => Color.FromArgb(135, 135, 135),
        // Green: checks are running and nothing is exposed.
        _ => Color.FromArgb(16, 137, 62),
    };

    // The full app icon, embedded with every size; the exe icon only exposes one.
    public static Icon AppIcon(int size)
    {
        using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Blindspot.ico")!;
        return new Icon(stream, size, size);
    }

    static Icon Draw(Color ink, bool filled, bool dot)
    {
        float dpi;
        using (var screen = Graphics.FromHwnd(0)) dpi = screen.DpiX;
        int size = (int)Math.Round(16 * dpi / 96);
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // The Mac drawing is 22 by 16 points; the tray is square, so fit
            // the width and center it vertically.
            float u = size / 22f;
            g.ScaleTransform(u, u);
            g.TranslateTransform(0, 3);
            using var brush = new SolidBrush(ink);
            var screenRect = new RectangleF(1.6f, 1.4f, 18.8f, 11f);
            if (filled)
            {
                // Even-odd fill leaves the eye as a hole in the screen.
                using var fill = RoundedRect(screenRect, 2);
                fill.FillMode = FillMode.Alternate;
                using (var eye = EyePath(new PointF(11, 6.9f), 5.4f, 3.1f))
                    fill.AddPath(eye, false);
                g.FillPath(brush, fill);
                g.FillEllipse(brush, 11 - 1.6f, 6.9f - 1.6f, 3.2f, 3.2f);
            }
            using (var pen = new Pen(ink, 1.3f))
            using (var outline = RoundedRect(screenRect, 2))
                g.DrawPath(pen, outline);
            using (var pen = new Pen(ink, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, 8, 15, 14, 15);
        }
        // No room to widen a square tray icon as the Mac does, so the dot sits on
        // the screen's top-right corner with a clear gap around it.
        if (dot)
        {
            float u = size / 22f, r = 3.5f * u, gap = r + 1.2f * u;
            float cx = size - r, cy = r;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float keep = Math.Clamp(d - gap + 0.5f, 0, 1);
                    float dotCover = Math.Clamp(r - d + 0.5f, 0, 1);
                    var p = bitmap.GetPixel(x, y);
                    // Dot over a cleared background, so only the dot's own edge blends.
                    float a = p.A / 255f * keep;
                    float outA = dotCover + a * (1 - dotCover);
                    if (outA <= 0) { bitmap.SetPixel(x, y, Color.Transparent); continue; }
                    int Mix(int dc, int pc) => (int)Math.Round((dc * dotCover + pc * a * (1 - dotCover)) / outA);
                    bitmap.SetPixel(x, y, Color.FromArgb((int)Math.Round(outA * 255),
                        Mix(Orange.R, p.R), Mix(Orange.G, p.G), Mix(Orange.B, p.B)));
                }
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

    static GraphicsPath EyePath(PointF c, float w, float h)
    {
        // Cubic control points at 4/3 of the height give a peak of exactly h.
        var eye = new GraphicsPath();
        eye.AddBezier(new PointF(c.X - w, c.Y), new PointF(c.X - w / 2, c.Y - h * 4 / 3),
                      new PointF(c.X + w / 2, c.Y - h * 4 / 3), new PointF(c.X + w, c.Y));
        eye.AddBezier(new PointF(c.X + w, c.Y), new PointF(c.X + w / 2, c.Y + h * 4 / 3),
                      new PointF(c.X - w / 2, c.Y + h * 4 / 3), new PointF(c.X - w, c.Y));
        eye.CloseFigure();
        return eye;
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
