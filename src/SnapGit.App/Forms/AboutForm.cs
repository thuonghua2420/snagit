using System.Reflection;
using SnapGit.App.Services;
using SnapGit.App.Ui;
using SnapGit.Core.Diagnostics;
using SnapGit.Core.Localization;
using SnapGit.Core.Updates;

namespace SnapGit.App.Forms;

/// <summary>Version, paths, diagnostics and the optional update check.</summary>
public sealed class AboutForm : Form
{
    private readonly AppHost _host;
    private readonly Label _updateStatus = new() { AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 8, 0, 0) };

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0);

    public AboutForm(AppHost host)
    {
        _host = host;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Text = Loc.T("About SnapGit");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var icon = new PictureBox { Image = AppIcon.Create().ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64), Margin = new Padding(0, 0, 14, 0) };
        var title = new Label { Text = "SnapGit", Font = new Font("Segoe UI Semibold", 18f), AutoSize = true };
        var version = new Label { Text = Loc.T("Version {0}", CurrentVersion.ToString(3)), AutoSize = true };
        var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var headText = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        headText.Controls.AddRange([title, version]);
        head.Controls.AddRange([icon, headText]);

        var info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 12, 0, 0),
            Text = Loc.T("Settings: {0}", AppPaths.SettingsFile) + Environment.NewLine +
                   Loc.T("Library: {0}", host.Library.Folder) + Environment.NewLine +
                   Loc.T("Logs: {0}", AppPaths.LogFolder) + Environment.NewLine +
                   $".NET {Environment.Version} • {Environment.OSVersion.VersionString}",
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var logs = new Button { Text = Loc.T("Open logs folder"), AutoSize = true };
        logs.Click += (_, _) => OpenFolder(AppPaths.LogFolder);
        var copy = new Button { Text = Loc.T("Copy diagnostics"), AutoSize = true };
        copy.Click += (_, _) => { Clipboard.SetText(info.Text); _updateStatus.Text = Loc.T("Copied."); };
        var check = new Button { Text = Loc.T("Check for updates"), AutoSize = true };
        check.Click += async (_, _) => await CheckForUpdatesAsync();
        var close = new Button { Text = Loc.T("Close"), AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.AddRange([logs, copy, check, close]);

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(18), WrapContents = false };
        layout.Controls.AddRange([head, info, buttons, _updateStatus]);
        Controls.Add(layout);
        AcceptButton = close;
        Theme.Apply(this);
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async Task CheckForUpdatesAsync()
    {
        var feed = _host.Settings.Current.UpdateFeedUrl;
        if (string.IsNullOrWhiteSpace(feed))
        {
            _updateStatus.Text = Loc.T("No update feed is configured (Settings > Sharing).");
            return;
        }

        _updateStatus.Text = Loc.T("Checking…");
        var update = await UpdateChecker.CheckAsync(feed, CurrentVersion);
        _updateStatus.Text = update is null
            ? Loc.T("You are up to date.")
            : Loc.T("Version {0} is available: {1}", update.Version.ToString(3), update.Url);
    }
}
