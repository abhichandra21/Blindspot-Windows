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

    const int PanelWidth = 320;
    // Room for text beside the status dot.
    const int TextWidth = 252;

    readonly TableLayoutPanel root = new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1, Padding = new Padding(0, 0, 0, 6),
    };
    readonly PictureBox logo = new() { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(28, 28), Margin = new Padding(0, 0, 8, 0) };
    readonly Label title = new() { Text = "Blindspot", AutoSize = true, Anchor = AnchorStyles.Left };
    readonly StatusDot dot = new() { Size = new Size(12, 12), Margin = new Padding(0, 9, 10, 0) };
    readonly Label headline = new() { AutoSize = true, MaximumSize = new Size(TextWidth, 0), Margin = new Padding(0, 0, 0, 2) };
    readonly Label detail = new() { AutoSize = true, MaximumSize = new Size(TextWidth, 0), Margin = new Padding(0, 0, 0, 6) };
    readonly Label watching = new() { AutoSize = true, MaximumSize = new Size(TextWidth, 0), Margin = Padding.Empty };
    readonly Label switchLabel = new() { Text = "Check my Teams shares", AutoSize = true, Anchor = AnchorStyles.Left };
    readonly ToggleSwitch checking = new() { Size = new Size(40, 20), Anchor = AnchorStyles.Right };
    readonly FooterRow settingsRow = new("\uE713", "Settings");
    readonly FooterRow quitRow = new("\uE7E8", "Quit Blindspot");
    readonly Panel topDivider = Divider(), bottomDivider = Divider();
    readonly System.Windows.Forms.Timer fadeIn = new() { Interval = 15 };
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
        ClientSize = new Size(PanelWidth, 240);
        Font = new Font("Segoe UI", 9f);
        title.Font = new Font("Segoe UI Semibold", 11f);
        headline.Font = new Font("Segoe UI Semibold", 12f);
        logo.Image = appIcon.ToBitmap();
        checking.AccessibleName = "Check my Teams shares";

        var header = Row(new Padding(16, 14, 16, 8), logo, title);
        var text = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty,
        };
        text.Controls.AddRange([headline, detail, watching]);
        var status = Row(new Padding(16, 2, 16, 14), dot, text);
        var switchRow = new TableLayoutPanel
        {
            ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(16, 10, 16, 10), Margin = Padding.Empty,
        };
        switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        switchRow.Controls.Add(switchLabel, 0, 0);
        switchRow.Controls.Add(checking, 1, 0);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (Control c in new Control[] { header, status, topDivider, switchRow, bottomDivider, settingsRow, quitRow })
        {
            c.Dock = DockStyle.Top;
            root.Controls.Add(c);
        }
        Controls.Add(root);

        checking.Toggled += (_, _) => togglePause();
        settingsRow.Click += (_, _) => { Hide(); openSettings(); };
        quitRow.Click += (_, _) => quit();
        fadeIn.Tick += (_, _) =>
        {
            Opacity = Math.Min(1, Opacity + 0.15);
            if (Opacity >= 1) fadeIn.Stop();
        };
    }

    static FlowLayoutPanel Row(Padding padding, params Control[] items)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Padding = padding, Margin = Padding.Empty };
        row.Controls.AddRange(items);
        return row;
    }

    static Panel Divider() => new() { Height = 1, Margin = new Padding(0, 0, 0, 4) };

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
        Opacity = 0;
        FitHeight();
        PlaceNearTray();
        Show();
        // Moving to a screen with another scale resizes the panel, so fit and place it again.
        FitHeight();
        PlaceNearTray();
        Activate();
        // A short fade, like the Windows flyouts.
        fadeIn.Start();
    }

    // The width is fixed; the height follows the text, which wraps as needed.
    void FitHeight()
    {
        root.PerformLayout();
        ClientSize = new Size(LogicalToDeviceUnits(PanelWidth), root.Height);
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
        if (Visible)
        {
            FitHeight();
            PlaceNearTray();
        }
    }

    static (string Headline, string Detail) Describe(GuardState s) => s.Kind switch
    {
        GuardKind.Paused => ("Paused", "Not checking your Teams shares."),
        GuardKind.ChecksDisabled => ("Checks are off", "Choose Teams tabs to watch in Settings."),
        GuardKind.ObservationFailed => ("Can't check sharing", "Windows didn't return the window list. Retrying."),
        GuardKind.NotSharing => ("Ready", "Not sharing right now."),
        GuardKind.SharingClear => ("Your share looks clear", "No watched Teams windows in the shared area."),
        _ => ($"Teams {Detection.TabName(s.Exposed[0])} may be visible", "Minimize Teams or stop sharing."),
    };

    void ApplyTheme(Theme t)
    {
        theme = t;
        BackColor = t.Background;
        foreach (var label in new[] { title, headline, switchLabel }) label.ForeColor = t.Text;
        foreach (var label in new[] { detail, watching }) label.ForeColor = t.Secondary;
        topDivider.BackColor = bottomDivider.BackColor = t.Border;
        checking.Theme = t;
        settingsRow.Theme = quitRow.Theme = t;
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

// A full-width row with an icon and a label that highlights on hover, like
// the footer actions of other tray flyouts.
sealed class FooterRow : Control
{
    readonly string glyph;
    bool hover;
    Theme theme = Theme.Light;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Theme Theme
    {
        get => theme;
        set { theme = value; Invalidate(); }
    }

    public FooterRow(string glyph, string text)
    {
        this.glyph = glyph;
        Text = text;
        AccessibleName = text;
        AccessibleRole = AccessibleRole.PushButton;
        Height = 36;
        Margin = new Padding(4, 0, 4, 0);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(theme.Background);
        if (hover)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(theme.Hover);
            using var path = new GraphicsPath();
            float r = LogicalToDeviceUnits(4), d = r * 2, w = Width - 1, h = Height - 1;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(w - d, 0, d, d, 270, 90);
            path.AddArc(w - d, h - d, d, d, 0, 90);
            path.AddArc(0, h - d, d, d, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
        // Segoe MDL2 Assets ships with Windows 10 and 11.
        using var iconFont = new Font("Segoe MDL2 Assets", 10.5f);
        var iconBox = new Rectangle(LogicalToDeviceUnits(12), 0, LogicalToDeviceUnits(20), Height);
        var textBox = new Rectangle(LogicalToDeviceUnits(40), 0, Width - LogicalToDeviceUnits(44), Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, glyph, iconFont, iconBox, theme.Text, flags | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, textBox, theme.Text, flags | TextFormatFlags.Left);
    }
}
