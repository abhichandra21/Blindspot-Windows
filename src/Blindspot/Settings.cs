using System.Diagnostics;
using System.Reflection;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Text.Json;

namespace Blindspot;

// User choices from the Settings window, stored in %APPDATA%\Blindspot\settings.json.
sealed class AppSettings
{
    public bool PlaySound { get; set; } = true;
    // Off until the user turns it on.
    public bool AutoMinimize { get; set; }
    public List<string> RiskyTabs { get; set; } = [.. Detection.AllRiskyTabs];

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blindspot", "settings.json");

    // A missing or unreadable file means defaults.
    public static AppSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            s.RiskyTabs = (s.RiskyTabs ?? []).Where(Detection.AllRiskyTabs.Contains).Distinct().ToList();
            return s;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    // Returns an error message, or null on success.
    public string? Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Failed to save settings: {e.Message}";
        }
    }
}

// Settings window in the style of Windows 11 Settings: a sidebar with
// General, Detection and About, and each option on its own card with a short
// explanation and a switch. Follows the Windows app mode, light or dark.
sealed class SettingsForm : Form
{
    const string RepoUrl = "https://github.com/abhichandra21/Blindspot-Windows";
    const int NavWidth = 220, CardWidth = 500;

    readonly AppSettings settings;
    readonly Action onChange;
    readonly Theme theme = Theme.CurrentForApps();
    readonly Panel content = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(28, 32, 28, 24) };
    readonly List<(NavItem Item, Control Page)> pages = [];
    readonly Label errorLabel = new() { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(0, 8, 0, 0) };
    readonly ToggleSwitch login = new();
    bool refreshingLogin;

    public SettingsForm(AppSettings settings, Action onChange, Func<string?> onTestWarning)
    {
        this.settings = settings;
        this.onChange = onChange;
        Text = "Blindspot Settings";
        Icon = TrayIcons.AppIcon(32);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(NavWidth + CardWidth + 56, 520);
        Font = new Font("Segoe UI", 9.75f);
        BackColor = theme.Window;
        ForeColor = theme.Text;

        Styled(login);
        RefreshLogin();
        login.Toggled += (_, _) =>
        {
            if (refreshingLogin) return;
            Report(LoginItem.SetEnabled(login.On));
            // Show what Windows actually has, even if the change failed.
            RefreshLogin();
        };
        var sound = Switch(settings.PlaySound, on => settings.PlaySound = on);
        var test = new PillButton("Show test warning", theme);
        var testCard = new Card(theme, "Test warning",
            "Shows the banner and plays the sound with sample text for a few seconds. Never minimizes anything.", test, CardWidth);
        test.Click += (_, _) => testCard.Detail = onTestWarning() ?? "Showing the test warning.";
        var general = Page("General",
            new Card(theme, "Start at login", "Opens Blindspot automatically when you sign in to Windows.", SwitchRow.For(login, theme), CardWidth),
            new Card(theme, "Warning sound", "Plays a short sound when a Teams chat may be visible in your share.", sound, CardWidth),
            testCard,
            Note("Click the tray shield for status and the pause switch. Right-click it for a quick menu."),
            errorLabel);

        var tabSwitches = Detection.AllRiskyTabs.ToDictionary(tab => tab, tab => new ToggleSwitch { On = settings.RiskyTabs.Contains(tab) });
        foreach (var (tab, toggle) in tabSwitches)
            toggle.Toggled += (_, _) =>
            {
                settings.RiskyTabs = Detection.AllRiskyTabs.Where(t => tabSwitches[t].On).ToList();
                Changed();
            };
        var minimize = Switch(settings.AutoMinimize, on => settings.AutoMinimize = on);
        var detection = Page("Detection",
            Section("Watch these Teams tabs"),
            new Card(theme, "Chat", "Warn when the Teams Chat tab is on the screen you are sharing.", SwitchRow.For(Styled(tabSwitches["Chat"]), theme), CardWidth),
            new Card(theme, "Activity", "Warn when the Teams Activity tab is on the screen you are sharing.", SwitchRow.For(Styled(tabSwitches["Activity"]), theme), CardWidth),
            Section("When a chat is exposed"),
            new Card(theme, "Minimize exposed Teams windows",
                "Minimizes those windows when a whole screen is shared. Never during a single-window share.", minimize, CardWidth),
            new Card(theme, "Pop-out chats are not detected", "Close chat windows you popped out of Teams before you share.", null, CardWidth, infoGlyph: true));

        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        // The informational version can carry a +commit suffix.
        version = version.Split('+')[0];
        var github = new PillButton("View on GitHub", theme);
        github.Click += (_, _) => Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
        var about = Page("About",
            AboutHeader(version),
            new Card(theme, "Source code and issues", RepoUrl, github, CardWidth),
            new Card(theme, "Privacy",
                "Everything runs on this PC. Blindspot reads window titles and positions about once a second. "
                + "It never captures images of your screen and never sends anything anywhere.", null, CardWidth));

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, Width = NavWidth, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(8, 16, 8, 8), BackColor = theme.Window,
        };
        var brand = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(8, 0, 0, 18) };
        brand.Controls.Add(new PictureBox { Image = TrayIcons.AppIcon(32).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(24, 24), Margin = new Padding(0, 0, 10, 0) });
        brand.Controls.Add(new Label { Text = "Blindspot", AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, 2, 0, 0) });
        nav.Controls.Add(brand);
        foreach (var (glyph, name, page) in new[] { ("\uE713", "General", general), ("\uE890", "Detection", detection), ("\uE946", "About", about) })
        {
            var item = new NavItem(glyph, name, theme) { Width = NavWidth - 16 };
            item.Click += (_, _) => Select(item);
            nav.Controls.Add(item);
            content.Controls.Add(page);
            pages.Add((item, page));
        }
        Controls.Add(content);
        Controls.Add(nav);
        Select(pages[0].Item);
    }

    // As tall as the tallest page, so no page scrolls and none sits in empty space.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        int tallest = pages.Max(p => p.Page.GetPreferredSize(new Size(content.ClientSize.Width - content.Padding.Horizontal, 0)).Height);
        ClientSize = new Size(ClientSize.Width, Math.Max(LogicalToDeviceUnits(360), tallest + content.Padding.Vertical));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(Handle, theme.IsDark);
    }

    // The Run value can change while the window stays open (an uninstall,
    // another copy of the app), so show what is really there.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RefreshLogin();
    }

    void RefreshLogin()
    {
        refreshingLogin = true;
        login.On = LoginItem.IsEnabled();
        login.Invalidate();
        SwitchRow.Update(login);
        refreshingLogin = false;
    }

    void Select(NavItem selected)
    {
        foreach (var (item, page) in pages)
        {
            item.Selected = item == selected;
            page.Visible = item == selected;
        }
    }

    void Changed()
    {
        Report(settings.Save());
        onChange();
    }

    void Report(string? error) => errorLabel.Text = error ?? "";

    ToggleSwitch Styled(ToggleSwitch toggle)
    {
        toggle.Theme = theme;
        toggle.Size = new Size(46, 26);
        return toggle;
    }

    Control Switch(bool on, Action<bool> apply)
    {
        var toggle = Styled(new ToggleSwitch { On = on });
        toggle.Toggled += (_, _) => { apply(toggle.On); Changed(); };
        return SwitchRow.For(toggle, theme);
    }

    Control AboutHeader(string version)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        row.Controls.Add(new PictureBox { Image = TrayIcons.AppIcon(64).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(56, 56), Margin = new Padding(0, 0, 14, 0) });
        var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        text.Controls.Add(new Label { Text = "Blindspot", AutoSize = true, Font = new Font("Segoe UI Semibold", 14f), ForeColor = theme.Text, Margin = Padding.Empty });
        text.Controls.Add(new Label { Text = $"Version {version}", AutoSize = true, ForeColor = theme.Secondary, Margin = Padding.Empty });
        text.Controls.Add(new Label
        {
            Text = "Warns when a Teams Chat or Activity window may be visible in your Teams screen share.",
            AutoSize = true, MaximumSize = new Size(CardWidth - 70, 0), ForeColor = theme.Secondary, Margin = new Padding(0, 4, 0, 0),
        });
        row.Controls.Add(text);
        return row;
    }

    FlowLayoutPanel Page(string title, params Control[] items)
    {
        var page = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false,
        };
        page.Controls.Add(new Label
        {
            Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 20f), ForeColor = theme.Text, Margin = new Padding(0, 0, 0, 16),
        });
        page.Controls.AddRange(items);
        return page;
    }

    Label Section(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 10f), ForeColor = theme.Text, Margin = new Padding(0, 10, 0, 6),
    };

    Label Note(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(CardWidth, 0), ForeColor = theme.Secondary, Margin = new Padding(0, 12, 0, 0),
    };
}

// One option: a title and explanation on the left, its control on the right,
// on a rounded card like Windows 11 Settings.
sealed class Card : Panel
{
    readonly Theme theme;
    readonly Label detail;

    public Card(Theme theme, string title, string description, Control? action, int width, bool infoGlyph = false)
    {
        this.theme = theme;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = MaximumSize = new Size(width, 0);
        Padding = new Padding(16, 12, 16, 12);
        Margin = new Padding(0, 0, 0, 4);
        BackColor = theme.Window;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        int textWidth = width - 32 - (action == null ? 0 : action.Width + 16) - (infoGlyph ? 32 : 0);
        var text = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, BackColor = theme.Card,
        };
        text.Controls.Add(new Label { Text = title, AutoSize = true, MaximumSize = new Size(textWidth, 0), ForeColor = theme.Text, BackColor = theme.Card, Margin = new Padding(0, 0, 0, 2) });
        text.Controls.Add(new Label { Text = description, AutoSize = true, MaximumSize = new Size(textWidth, 0), ForeColor = theme.Secondary, BackColor = theme.Card, Font = new Font("Segoe UI", 9f), Margin = Padding.Empty });
        detail = new Label { AutoSize = true, MaximumSize = new Size(textWidth, 0), ForeColor = theme.Accent, BackColor = theme.Card, Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 4, 0, 0), Visible = false };
        text.Controls.Add(detail);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = Padding.Empty, BackColor = theme.Card,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        if (infoGlyph)
            layout.Controls.Add(new Label
            {
                Text = "\uE946", Font = new Font("Segoe MDL2 Assets", 14f), ForeColor = theme.Accent, BackColor = theme.Card,
                AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0),
            }, 0, 0);
        layout.Controls.Add(text, 1, 0);
        if (action != null)
        {
            action.Anchor = AnchorStyles.Right;
            action.Margin = new Padding(12, 0, 0, 0);
            layout.Controls.Add(action, 2, 0);
        }
        Controls.Add(layout);
    }

    // A one-line result under the description, e.g. why the test warning didn't show.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Detail
    {
        get => detail.Text;
        set { detail.Text = value; detail.Visible = value.Length > 0; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Shapes.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(6));
        using var fill = new SolidBrush(theme.Card);
        using var border = new Pen(theme.CardBorder);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
    }
}

// A sidebar entry: icon and name, with a highlight and accent bar when selected.
sealed class NavItem : Control
{
    readonly string glyph;
    readonly Theme theme;
    bool hover, selected;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => selected;
        set { selected = value; Invalidate(); }
    }

    public NavItem(string glyph, string text, Theme theme)
    {
        this.glyph = glyph;
        this.theme = theme;
        Text = text;
        AccessibleName = text;
        AccessibleRole = AccessibleRole.PageTab;
        Height = 38;
        Margin = new Padding(0, 0, 0, 2);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (selected || hover)
        {
            using var path = Shapes.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(4));
            using var fill = new SolidBrush(selected ? theme.Card : theme.Hover);
            g.FillPath(fill, path);
        }
        if (selected)
        {
            float barHeight = Height * 0.4f;
            using var bar = Shapes.RoundedRect(new RectangleF(0, (Height - barHeight) / 2, LogicalToDeviceUnits(3), barHeight), LogicalToDeviceUnits(3) / 2f);
            using var accent = new SolidBrush(theme.Accent);
            g.FillPath(accent, bar);
        }
        using var iconFont = new Font("Segoe MDL2 Assets", 11f);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, glyph, iconFont, new Rectangle(LogicalToDeviceUnits(12), 0, LogicalToDeviceUnits(20), Height), theme.Text, flags | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(LogicalToDeviceUnits(42), 0, Width - LogicalToDeviceUnits(46), Height), theme.Text, flags);
    }
}

// A rounded, bordered button like the secondary buttons in Windows 11 Settings.
sealed class PillButton : Control
{
    readonly Theme theme;
    bool hover;

    public PillButton(string text, Theme theme)
    {
        this.theme = theme;
        Text = text;
        AccessibleName = text;
        AccessibleRole = AccessibleRole.PushButton;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        Size = new Size(TextRenderer.MeasureText(text, new Font("Segoe UI", 9.75f)).Width + 32, 32);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(theme.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Shapes.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), LogicalToDeviceUnits(4));
        using var fill = new SolidBrush(hover ? theme.Hover : theme.IsDark ? Color.FromArgb(55, 55, 55) : Color.White);
        using var border = new Pen(theme.IsDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(208, 208, 208));
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) Shapes.DrawFocusRing(this, g, theme);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

// A switch with "On" or "Off" to its left, like Windows 11 Settings.
static class SwitchRow
{
    public static Control For(ToggleSwitch toggle, Theme theme)
    {
        var state = new Label
        {
            AutoSize = false, Size = new Size(30, 26), TextAlign = ContentAlignment.MiddleRight,
            ForeColor = theme.Text, BackColor = theme.Card, Margin = new Padding(0, 0, 6, 0), Tag = "state",
        };
        var row = new FlowLayoutPanel { Size = new Size(84, 26), WrapContents = false, Margin = Padding.Empty, BackColor = theme.Card };
        toggle.Margin = Padding.Empty;
        row.Controls.Add(state);
        row.Controls.Add(toggle);
        toggle.Toggled += (_, _) => Update(toggle);
        Update(toggle);
        return row;
    }

    public static void Update(ToggleSwitch toggle)
    {
        if (toggle.Parent?.Controls.OfType<Label>().FirstOrDefault(l => "state".Equals(l.Tag)) is Label state)
            state.Text = toggle.On ? "On" : "Off";
    }
}

static class Shapes
{
    // A rounded 2 px ring like Windows 11's keyboard focus, shown only after keyboard navigation.
    public static void DrawFocusRing(Control control, Graphics g, Theme theme)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float width = control.LogicalToDeviceUnits(2);
        using var pen = new Pen(theme.Text, width);
        using var ring = RoundedRect(new RectangleF(width / 2, width / 2, control.Width - width - 1, control.Height - width - 1),
            control.LogicalToDeviceUnits(control.Height / 2 > 10 ? 6 : 4));
        g.DrawPath(pen, ring);
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(1, radius * 2);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
