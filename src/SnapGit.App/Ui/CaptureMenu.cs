using SnapGit.App.Services;
using SnapGit.Core.Hotkeys;
using SnapGit.Core.Localization;
using SnapGit.Core.Models;

namespace SnapGit.App.Ui;

/// <summary>The capture / record entries, shared by the main window's menu bar, its toolbar and the library page.</summary>
public static class CaptureMenu
{
    public static void Fill(ToolStripItemCollection items, AppHost host)
    {
        Add(items, host, Loc.T("Region / window / control"), UiIcon.Capture, CaptureMode.Region);
        Add(items, host, Loc.T("Window"), UiIcon.Window, CaptureMode.Window);
        Add(items, host, Loc.T("Full screen"), UiIcon.FullScreen, CaptureMode.FullScreen);
        Add(items, host, Loc.T("Scrolling area"), UiIcon.Scroll, CaptureMode.Scrolling);
        Add(items, host, Loc.T("Free-hand"), UiIcon.FreeHand, CaptureMode.FreeHand);
        Add(items, host, Loc.T("Multiple areas"), UiIcon.MultiArea, CaptureMode.MultiArea);
        Add(items, host, Loc.T("Grab text (OCR)"), UiIcon.GrabText, CaptureMode.GrabText);
        items.Add(new ToolStripSeparator());
        items.Add(Item(Loc.T("Repeat last capture"), UiIcon.Repeat, () => host.Dispatch(HotkeyAction.RepeatLastCapture)));

        var profiles = new ToolStripMenuItem(Loc.T("Profiles"), Icons.Get(UiIcon.Profile));
        profiles.DropDownOpening += (_, _) => FillProfiles(profiles, host);
        profiles.DropDownItems.Add("…");
        items.Add(profiles);

        items.Add(new ToolStripSeparator());
        items.Add(Item(Loc.T("Record GIF"), UiIcon.Gif, () => host.Recording.Toggle(RecordingKind.Gif)));
        items.Add(Item(Loc.T("Record video"), UiIcon.Video, () => host.Recording.Toggle(RecordingKind.Video)));
    }

    private static void Add(ToolStripItemCollection items, AppHost host, string text, UiIcon icon, CaptureMode mode) =>
        items.Add(Item(text, icon, () => host.Capture.Run(mode)));

    private static ToolStripMenuItem Item(string text, UiIcon icon, Action onClick)
    {
        var item = new ToolStripMenuItem(text, Icons.Get(icon));
        item.Click += (_, _) => onClick();
        return item;
    }

    private static void FillProfiles(ToolStripMenuItem root, AppHost host)
    {
        root.DropDownItems.Clear();
        foreach (var profile in host.Settings.Current.Profiles)
        {
            var captured = profile;
            root.DropDownItems.Add(new ToolStripMenuItem(profile.Name, null, (_, _) => host.RunProfile(captured)));
        }
        if (root.DropDownItems.Count == 0)
            root.DropDownItems.Add(new ToolStripMenuItem(Loc.T("No profiles yet — add one in Settings"), null, (_, _) => host.ShowSettings(host.MainWindow)));
        if (Theme.IsDark)
            foreach (ToolStripItem item in root.DropDownItems) item.ForeColor = Color.FromArgb(235, 235, 238);
    }
}
