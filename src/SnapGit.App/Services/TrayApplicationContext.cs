using SnapGit.App.Forms;
using SnapGit.App.Ui;
using SnapGit.Core.Hotkeys;
using SnapGit.Core.Localization;
using SnapGit.Core.Models;
using SnapGit.Core.Updates;

namespace SnapGit.App.Services;

/// <summary>Keeps the app alive in the system tray and owns the tray menu and notifications.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppHost _host;
    private readonly NotifyIcon _tray;
    private Action? _balloonClick;

    public TrayApplicationContext(AppHost host, bool startInBackground)
    {
        _host = host;

        var menu = new ContextMenuStrip { ImageScalingSize = new Size(16, 16) };
        AddMenu(menu, Loc.T("Capture region / window"), UiIcon.Capture, () => _host.Capture.Run(CaptureMode.Region));
        AddMenu(menu, Loc.T("Capture window"), UiIcon.Window, () => _host.Capture.Run(CaptureMode.Window));
        AddMenu(menu, Loc.T("Capture full screen"), UiIcon.FullScreen, () => _host.Capture.Run(CaptureMode.FullScreen));
        AddMenu(menu, Loc.T("Scrolling capture"), UiIcon.Scroll, () => _host.Capture.Run(CaptureMode.Scrolling));
        AddMenu(menu, Loc.T("Free-hand capture"), UiIcon.FreeHand, () => _host.Capture.Run(CaptureMode.FreeHand));
        AddMenu(menu, Loc.T("Multi-area capture"), UiIcon.MultiArea, () => _host.Capture.Run(CaptureMode.MultiArea));
        AddMenu(menu, Loc.T("Grab text (OCR)"), UiIcon.GrabText, () => _host.Capture.Run(CaptureMode.GrabText));
        AddMenu(menu, Loc.T("Repeat last capture"), UiIcon.Repeat, () => _host.Dispatch(HotkeyAction.RepeatLastCapture));
        var profiles = new ToolStripMenuItem(Loc.T("Profiles"), Icons.Get(UiIcon.Profile));
        profiles.DropDownOpening += (_, _) => FillProfiles(profiles);
        profiles.DropDownItems.Add("…");
        menu.Items.Add(profiles);
        menu.Items.Add(new ToolStripSeparator());
        AddMenu(menu, Loc.T("Record GIF"), UiIcon.Gif, () => _host.Recording.Toggle(RecordingKind.Gif));
        AddMenu(menu, Loc.T("Record video"), UiIcon.Video, () => _host.Recording.Toggle(RecordingKind.Video));
        menu.Items.Add(new ToolStripSeparator());
        AddMenu(menu, Loc.T("Open library"), UiIcon.Folder, _host.ShowLibrary);        AddMenu(menu, Loc.T("Settings…"), UiIcon.Settings, () => _host.ShowSettings());
        AddMenu(menu, Loc.T("About"), UiIcon.Info, () => _host.ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        AddMenu(menu, Loc.T("Exit"), null, ExitThread);
        Theme.ThemeToolStrip(menu);

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Create(),
            Text = "SnapGit",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _host.ShowLibrary();
        };
        _tray.BalloonTipClicked += (_, _) => _balloonClick?.Invoke();

        _host.Notification += (_, n) =>
        {
            _balloonClick = n.OnClick;
            _tray.ShowBalloonTip(3500, "SnapGit", n.Message, ToolTipIcon.Info);
        };
        _host.HotkeyConflicts += OnHotkeyConflicts;

        _host.Start();

        if (!startInBackground) _host.ShowLibrary(); // the library page is the home of the single main window

        OfferRecovery();
        _ = CheckForUpdatesAsync();
    }

    private void FillProfiles(ToolStripMenuItem root)
    {
        root.DropDownItems.Clear();
        foreach (var profile in _host.Settings.Current.Profiles)
        {
            var captured = profile;
            root.DropDownItems.Add(new ToolStripMenuItem(profile.Name, null, (_, _) => _host.RunProfile(captured)));
        }
        if (root.DropDownItems.Count == 0)
            root.DropDownItems.Add(new ToolStripMenuItem(Loc.T("No profiles yet — add one in Settings"), null, (_, _) => _host.ShowSettings()));
        if (Theme.IsDark)
            foreach (ToolStripItem item in root.DropDownItems) item.ForeColor = Color.FromArgb(235, 235, 238);
    }

    private static void AddMenu(ContextMenuStrip menu, string text, UiIcon? icon, Action onClick)
    {
        var item = new ToolStripMenuItem(text, icon is { } i ? Icons.Get(i) : null);
        item.Click += (_, _) => onClick();
        menu.Items.Add(item);
    }

    private void OnHotkeyConflicts(object? sender, IReadOnlyList<HotkeyAction> failed)
    {
        var names = string.Join(", ", failed);
        _balloonClick = () => _host.ShowSettings();
        _tray.ShowBalloonTip(6000, "SnapGit", Loc.T("These hotkeys are already used by another app: {0}. Click to change them in Settings.", names), ToolTipIcon.Warning);
    }

    /// <summary>Unsaved editor work from a previous crash is offered back once.</summary>
    private void OfferRecovery()
    {
        var pending = RecoveryService.Pending();
        if (pending.Count == 0) return;

        var answer = MessageBox.Show(Loc.T("SnapGit found {0} unsaved image(s) from a previous session. Open them now?", pending.Count),
            "SnapGit", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        // One window holds one picture at a time, so recovered pictures go into the library first (nothing is lost) and the last one is opened.
        var recovered = new List<string>();
        foreach (var path in pending)
        {
            if (answer == DialogResult.Yes)
            {
                try { recovered.Add(_host.Library.Import(path).FilePath); }
                catch (Exception ex) { Core.Diagnostics.Log.Warn($"Could not recover {path}: {ex.Message}"); continue; } // keep the file for the next start
            }
            RecoveryService.Discard(path); // declined ones are dropped
        }

        if (recovered.Count == 0) return;
        if (recovered.Count > 1) _host.Notify(Loc.T("{0} recovered pictures were added to the library.", recovered.Count));
        _host.OpenInEditor(recovered[^1]);
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await UpdateChecker.CheckAsync(_host.Settings.Current.UpdateFeedUrl, AboutForm.CurrentVersion);
            if (update is null) return;

            _balloonClick = string.IsNullOrWhiteSpace(update.Url) ? null : () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(update.Url) { UseShellExecute = true });
            _tray.ShowBalloonTip(6000, "SnapGit", Loc.T("Version {0} is available. Click to download.", update.Version.ToString(3)), ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn($"Update check failed: {ex.Message}");
        }
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
