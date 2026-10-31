using System.ComponentModel;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace Blindspot;

// The panel that opens above the tray on left-click, like the flyouts of other
// tray apps: status in plain words, the pause switch, Settings and Quit.
// Follows the Windows light or dark setting each time it opens.
sealed class Flyout : Form
{
    const int WS_EX_TOOLWINDOW = 0x80, CS_DROPSHADOW = 0x20000;
    const int EdgeMargin = 12;

    readonly PictureBox logo = new() { SizeMode = PictureBoxSizeMode.Zoom };
    readonly Label title = new() { Text = "Blindspot", AutoSize = true };
    readonly StatusDot dot = new();
    readonly Label headline = new() { AutoSize = false };
    readonly Label detail = new() { AutoSize = false };
    readonly Label watching = new() { AutoSize = false };
    readonly Label switchLabel = new() { Text = "Check my Teams shares", AutoSize = true };
    readonly ToggleSwitch checking = new();
    readonly FlatButton settingsButton = new("Settings");
    readonly FlatButton quitButton = new("Quit");
    readonly Panel topDivider = new(), bottomDivider = new();
    Theme theme = Theme.Light;

    // When the panel last closed because focus moved away. A click on the tray
    // icon does that too, and should then not reopen it.
    public DateTime LastHidden { get; private set; }

    public Flyout(Icon appIcon, Action togglePause, Action openSettings, Action quit)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(320, 240);
        Font = new Font("Segoe UI", 9f);

        logo.Image = appIcon.ToBitmap();
        logo.SetBounds(16, 16, 28, 28);
        title.Font = new Font("Segoe UI Semibold", 11f);
        title.Location = new Point(52, 19);
        dot.SetBounds(18, 66, 10, 10);
        headline.Font = new Font("Segoe UI Semibold", 12f);
        headline.SetBounds(36, 58, 270, 26);
        detail.SetBounds(36, 86, 270, 36);
        watching.SetBounds(36, 124, 270, 20);
        topDivider.SetBounds(0, 152, 320, 1);
        switchLabel.Location = new Point(16, 168);
        checking.SetBounds(264, 166, 40, 20);
        checking.AccessibleName = "Check my Teams shares";
        bottomDivider.SetBounds(0, 198, 320, 1);
        settingsButton.SetBounds(6, 204, 96, 30);
        quitButton.SetBounds(218, 204, 96, 30);

        checking.Toggled += (_, _) => togglePause();
        settingsButton.Click += (_, _) => { Hide(); openSettings(); };
        quitButton.Click += (_, _) => quit();
        Controls.AddRange([logo, title, dot, headline, detail, watching, topDivider,
            switchLabel, checking, bottomDivider, settingsButton, quitButton]);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseRoundedCorners(Handle);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        LastHidden = DateTime.UtcNow;
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Hide();
    }

    // A thin border keeps the edge crisp where Windows 10 draws square corners.
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    public void ShowNearTray()
    {
        ApplyTheme(Theme.Current());
        PlaceNearTray();
        Show();
        // Moving to a screen with another scale resizes the panel, so place it again.
        PlaceNearTray();
        Activate();
    }

    public void UpdateState(GuardState state, AppSettings settings, string? minimizeNote)
    {
        var (head, body) = Describe(state);
        headline.Text = head;
        detail.Text = minimizeNote == null ? body : $"{body} {minimizeNote}.";
        dot.Color = TrayIcons.StateColor(state.Kind);
        var tabs = settings.RiskyTabs.Count == 0 ? "no Teams tabs" : string.Join(" and ", settings.RiskyTabs);
        watching.Text = $"Watching {tabs} · Minimizing {(settings.AutoMinimize ? "on" : "off")}";
        checking.On = state.Kind != GuardKind.Paused;
        checking.Invalidate();
    }

    static (string Headline, string Detail) Describe(GuardState s) => s.Kind switch
    {
        GuardKind.Paused => ("Paused", "Blindspot isn't checking your Teams shares."),
        GuardKind.ChecksDisabled => ("Checks are off", "No Teams tabs are selected in Settings."),
        GuardKind.ObservationFailed => ("Can't check Teams sharing", "Windows didn't return the window list. Blindspot keeps trying."),
        GuardKind.NotSharing => ("Ready", "No Teams share right now. Blindspot warns you if a chat shows up in one."),
        GuardKind.SharingClear => ("Your share looks clear", "No watched Teams windows are in the shared area."),
        _ => ($"Teams {Detection.TabName(s.Exposed[0])} may be visible",
              "It's in the area you are sharing. Minimize it or stop sharing."),
    };

    void ApplyTheme(Theme t)
    {
        theme = t;
        BackColor = t.Background;
        foreach (var label in new[] { title, headline, switchLabel }) label.ForeColor = t.Text;
        foreach (var label in new[] { detail, watching }) label.ForeColor = t.Secondary;
        topDivider.BackColor = bottomDivider.BackColor = t.Border;
        checking.Theme = t;
        settingsButton.Theme = quitButton.Theme = t;
        Invalidate(true);
    }

    // On the screen the pointer is on, in the corner next to the tray,
    // wherever the taskbar is docked.
    void PlaceNearTray()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        var work = screen.WorkingArea;
        int margin = LogicalToDeviceUnits(EdgeMargin);
        bool taskbarTop = work.Top > screen.Bounds.Top;
        bool taskbarLeft = work.Left > screen.Bounds.Left;
        int x = taskbarLeft ? work.Left + margin : work.Right - Width - margin;
        int y = taskbarTop ? work.Top + margin : work.Bottom - Height - margin;
        Location = new Point(x, y);
    }
}

sealed record Theme(Color Background, Color Text, Color Secondary, Color Border, Color Hover, Color Accent, Color SwitchOff)
{
    public static readonly Theme Light = new(Color.FromArgb(249, 249, 249), Color.FromArgb(26, 26, 26),
        Color.FromArgb(96, 96, 96), Color.FromArgb(222, 222, 222), Color.FromArgb(234, 234, 234),
        Color.FromArgb(0, 95, 184), Color.FromArgb(135, 135, 135));
    public static readonly Theme Dark = new(Color.FromArgb(39, 39, 39), Color.FromArgb(255, 255, 255),
        Color.FromArgb(200, 200, 200), Color.FromArgb(64, 64, 64), Color.FromArgb(58, 58, 58),
        Color.FromArgb(76, 194, 255), Color.FromArgb(160, 160, 160));

    // Tray flyouts follow the Windows mode (taskbar and Start), not the app mode.
    public static Theme Current()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int light && light == 0 ? Dark : Light;
    }
}

sealed class StatusDot : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color Color { get; set; } = Color.Gray;

    public StatusDot()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color);
        e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
    }
}

// A Windows 11 style on/off switch.
sealed class ToggleSwitch : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool On { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Theme Theme { get; set; } = Theme.Light;
    public event EventHandler? Toggled;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    void Flip()
    {
        On = !On;
        Invalidate();
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Flip(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) Flip();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float h = Height - 1, w = Width - 1;
        using var track = new GraphicsPath();
        track.AddArc(0, 0, h, h, 90, 180);
        track.AddArc(w - h, 0, h, h, 270, 180);
        track.CloseFigure();
        float knob = h * 0.55f, inset = (h - knob) / 2;
        if (On)
        {
            using var fill = new SolidBrush(Theme.Accent);
            g.FillPath(fill, track);
            using var white = new SolidBrush(Theme == Theme.Dark ? Color.Black : Color.White);
            g.FillEllipse(white, w - h + inset, inset, knob, knob);
        }
        else
        {
            using var pen = new Pen(Theme.SwitchOff, 1.2f);
            g.DrawPath(pen, track);
            using var gray = new SolidBrush(Theme.SwitchOff);
            g.FillEllipse(gray, inset, inset, knob, knob);
        }
        if (Focused) ControlPaint.DrawFocusRectangle(g, ClientRectangle);
    }
}

// A borderless button that highlights on hover, like flyout footer buttons.
sealed class FlatButton : Label
{
    bool hover;
    Theme theme = Theme.Light;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Theme Theme
    {
        get => theme;
        set { theme = value; Restyle(); }
    }

    public FlatButton(string text)
    {
        Text = text;
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleCenter;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Restyle(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Restyle(); }

    void Restyle()
    {
        BackColor = hover ? theme.Hover : theme.Background;
        ForeColor = theme.Text;
    }
}
