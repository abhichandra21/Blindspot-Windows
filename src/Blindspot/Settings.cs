using System.Diagnostics;
using System.Reflection;
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

// Settings window: General, Detection and About tabs, each option with a short
// gray explanation underneath, like the Mac version.
sealed class SettingsForm : Form
{
    const string RepoUrl = "https://github.com/abhichandra21/Blindspot-Windows";

    readonly AppSettings settings;
    readonly Action onChange;
    readonly Label errorLabel = new() { AutoSize = true, ForeColor = Color.Firebrick };
    readonly CheckBox login = new() { Text = "Start at login", AutoSize = true };
    bool refreshingLogin;

    public SettingsForm(AppSettings settings, Action onChange, Func<string?> onTestWarning)
    {
        this.settings = settings;
        this.onChange = onChange;
        Text = "Blindspot Settings";
        Icon = TrayIcons.AppIcon(32);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(470, 330);

        RefreshLogin();
        login.CheckedChanged += (_, _) => { if (!refreshingLogin) Report(LoginItem.SetEnabled(login.Checked)); };
        var sound = new CheckBox { Text = "Play warning sound", AutoSize = true, Checked = settings.PlaySound };
        sound.CheckedChanged += (_, _) => { settings.PlaySound = sound.Checked; Changed(); };
        var testResult = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        var test = new Button { Text = "Show Test Warning", AutoSize = true };
        test.Click += (_, _) => testResult.Text = onTestWarning() ?? "";
        var general = Page(
            Option(login, "Opens Blindspot automatically when you sign in to Windows."),
            Option(sound, "Plays a short sound when a Teams chat may be visible in your share."),
            Option(Row(test, testResult), "Shows the warning with sample text for a few seconds. Never minimizes anything."),
            Note("Click the tray shield for status and the pause switch. Right-click it for a quick menu."),
            errorLabel);

        var tabBoxes = Detection.AllRiskyTabs
            .Select(tab => new CheckBox { Text = tab, AutoSize = true, Checked = settings.RiskyTabs.Contains(tab) })
            .ToList();
        foreach (var box in tabBoxes)
            box.CheckedChanged += (_, _) =>
            {
                settings.RiskyTabs = tabBoxes.Where(b => b.Checked).Select(b => b.Text).ToList();
                Changed();
            };
        var minimize = new CheckBox { Text = "Minimize exposed Teams windows", AutoSize = true, Checked = settings.AutoMinimize };
        minimize.CheckedChanged += (_, _) => { settings.AutoMinimize = minimize.Checked; Changed(); };
        var detection = Page(
            Note("Watch these Teams tabs"),
            Option(Row([.. tabBoxes]), "Warn when these Teams tabs are on the screen you are sharing. Both can show message text."),
            Option(minimize, "Minimizes those Teams windows when a whole screen is shared. Never during a single-window share."),
            Note("Pop-out chat windows are not detected. Close them before sharing."));

        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        var link = new LinkLabel { Text = RepoUrl, AutoSize = true };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
        var appIcon = new PictureBox
        {
            Image = TrayIcons.AppIcon(64).ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(64, 64),
        };
        var about = Page(
            appIcon,
            new Label { Text = "Blindspot", AutoSize = true, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold) },
            Note($"Version {version}"),
            Note("Warns when a Teams Chat or Activity window may be visible in your Teams screen share."),
            link,
            Note("Everything runs on this PC. Blindspot reads window titles and positions about once a second. "
                + "It never captures images of your screen and never sends anything anywhere."));

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Tab("General", general));
        tabs.TabPages.Add(Tab("Detection", detection));
        tabs.TabPages.Add(Tab("About", about));
        Controls.Add(tabs);
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
        login.Checked = LoginItem.IsEnabled();
        refreshingLogin = false;
    }

    void Changed()
    {
        Report(settings.Save());
        onChange();
    }

    void Report(string? error) => errorLabel.Text = error ?? "";

    static TabPage Tab(string title, Control content)
    {
        var page = new TabPage(title) { UseVisualStyleBackColor = true };
        page.Controls.Add(content);
        return page;
    }

    static FlowLayoutPanel Page(params Control[] items)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12),
        };
        panel.Controls.AddRange(items);
        return panel;
    }

    static FlowLayoutPanel Row(params Control[] items)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(items);
        return row;
    }

    static Control Option(Control control, string help)
    {
        var box = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10),
        };
        box.Controls.Add(control);
        box.Controls.Add(Note(help));
        return box;
    }

    static Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 2, 3, 6),
    };
}
