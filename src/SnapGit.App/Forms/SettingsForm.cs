using SnapGit.App.Controls;
using SnapGit.App.Ui;
using SnapGit.Core.Hotkeys;
using SnapGit.Core.Imaging;
using SnapGit.Core.Library;
using SnapGit.Core.Localization;
using SnapGit.Core.Models;
using SnapGit.Core.Settings;

namespace SnapGit.App.Forms;

/// <summary>Tab order of <see cref="SettingsForm"/> (the index is the position in the tab control).</summary>
public enum SettingsTab
{
    General,
    Capture,
    Hotkeys,
    Profiles,
    Recording,
    Sharing,
}

/// <summary>All settings, grouped in tabs. Every control maps 1:1 to a property of <see cref="AppSettings"/>.</summary>
public sealed class SettingsForm : Form
{
    private static readonly (HotkeyAction Action, string Label)[] HotkeyRows =
    [
        (HotkeyAction.CaptureRegion, "Capture region / window / control"),
        (HotkeyAction.CaptureWindow, "Capture window only"),
        (HotkeyAction.CaptureFullScreen, "Capture full screen"),
        (HotkeyAction.CaptureScrolling, "Scrolling capture"),
        (HotkeyAction.CaptureFreehand, "Free-hand capture"),
        (HotkeyAction.CaptureMultiArea, "Multi-area capture"),
        (HotkeyAction.GrabText, "Grab text (OCR)"),
        (HotkeyAction.RepeatLastCapture, "Repeat last capture"),
        (HotkeyAction.RecordGif, "Record GIF (start / stop)"),
        (HotkeyAction.RecordVideo, "Record video (start / stop)"),
        (HotkeyAction.OpenLibrary, "Open library"),
    ];

    private readonly ISettingsService _settings;
    private readonly Dictionary<HotkeyAction, HotkeyBox> _hotkeyBoxes = new();
    private readonly List<ShareDestination> _destinations = new();
    private readonly List<CaptureProfile> _profiles = new();
    private readonly ListBox _profileList = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    // general
    private readonly ComboBox _languageBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _themeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly CheckBox _startupCheck = new() { Text = Loc.T("Start SnapGit with Windows"), AutoSize = true };
    private readonly TextBox _folderBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _patternBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _projectCheck = new() { Text = Loc.T("Save captures as editable projects (.snapgit)"), AutoSize = true };
    private readonly ComboBox _formatBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly NumericUpDown _qualityBox = new() { Minimum = 10, Maximum = 100, Width = 70 };

    // capture
    private readonly CheckBox _editorCheck = new() { Text = Loc.T("Open the editor after each capture"), AutoSize = true };
    private readonly CheckBox _clipboardCheck = new() { Text = Loc.T("Copy every capture to the clipboard"), AutoSize = true };
    private readonly CheckBox _cursorCheck = new() { Text = Loc.T("Include the mouse cursor"), AutoSize = true };
    private readonly CheckBox _printWindowCheck = new() { Text = Loc.T("Capture windows with PrintWindow (works for covered windows)"), AutoSize = true };
    private readonly NumericUpDown _delayBox = new() { Minimum = 0, Maximum = 30, Width = 70 };
    private readonly ComboBox _fixedBox = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 140 };
    private readonly ComboBox _scrollDirectionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly NumericUpDown _scrollDelayBox = new() { Minimum = 100, Maximum = 2000, Increment = 50, Width = 80 };
    private readonly NumericUpDown _scrollFramesBox = new() { Minimum = 2, Maximum = 400, Width = 80 };
    private readonly ComboBox _ocrBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };

    // recording
    private readonly NumericUpDown _gifFpsBox = new() { Minimum = 1, Maximum = 30, Width = 70 };
    private readonly NumericUpDown _gifSecondsBox = new() { Minimum = 3, Maximum = 300, Width = 70 };
    private readonly NumericUpDown _videoFpsBox = new() { Minimum = 5, Maximum = 60, Width = 70 };
    private readonly ComboBox _videoResolutionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox _videoQualityBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly CheckBox _recordCursorCheck = new() { Text = Loc.T("Record the mouse cursor"), AutoSize = true };
    private readonly CheckBox _systemAudioCheck = new() { Text = Loc.T("Record system audio (video only)"), AutoSize = true };
    private readonly CheckBox _microphoneCheck = new() { Text = Loc.T("Record the microphone (video only)"), AutoSize = true };

    // sharing & updates
    private readonly ListBox _shareList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _feedBox = new() { Dock = DockStyle.Fill };

    private readonly IReadOnlyList<OcrLanguageItem> _ocrLanguages;

    public SettingsForm(ISettingsService settings, IReadOnlyList<OcrLanguage>? ocrLanguages = null, SettingsTab initialTab = SettingsTab.General)
    {
        _settings = settings;
        _ocrLanguages = (ocrLanguages ?? []).Select(l => new OcrLanguageItem(l)).ToList();

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Text = Loc.T("SnapGit Settings");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 560);

        // The pages live in a TabControl whose tab strip is hidden; the navigation list on the left drives it.
        var tabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, ItemSize = new Size(0, 1), SizeMode = TabSizeMode.Fixed, Padding = new Point(0, 0) };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildCaptureTab());
        tabs.TabPages.Add(BuildHotkeysTab());
        tabs.TabPages.Add(BuildProfilesTab());
        tabs.TabPages.Add(BuildRecordingTab());
        tabs.TabPages.Add(BuildSharingTab());
        tabs.SelectedIndex = Math.Clamp((int)initialTab, 0, tabs.TabPages.Count - 1);

        var nav = new SideNav { Dock = DockStyle.Left, Width = 172, AccessibleName = Loc.T("Settings sections") };
        UiIcon[] icons = [UiIcon.Settings, UiIcon.Capture, UiIcon.Keyboard, UiIcon.Profile, UiIcon.Record, UiIcon.Share];
        for (var i = 0; i < tabs.TabPages.Count; i++) nav.Add(new NavItem(icons[i], tabs.TabPages[i].Text));
        nav.SelectedIndex = tabs.SelectedIndex;
        nav.SelectedIndexChanged += (_, _) => tabs.SelectedIndex = nav.SelectedIndex;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(10, 8, 10, 8) };
        var cancel = new Button { Text = Loc.T("Cancel"), DialogResult = DialogResult.Cancel, Width = 92, Height = 28 };
        var ok = new Button { Text = Loc.T("Save"), Width = 92, Height = 28 };
        ok.Click += (_, _) => { if (SaveToSettings()) DialogResult = DialogResult.OK; };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(tabs);
        Controls.Add(nav);
        Controls.Add(buttons);

        LoadFromSettings();
        Theme.Apply(this);
        Theme.StylePrimary(ok);
    }

    // ------------------------------------------------------------------ tabs

    private TabPage BuildGeneralTab()
    {
        var page = NewPage(Loc.T("General"));
        var table = (TableLayoutPanel)page.Controls[0];

        foreach (var (code, name) in Loc.Available) _languageBox.Items.Add(new Choice(code, code == "auto" ? Loc.T("Auto (Windows language)") : name));
        _themeBox.Items.AddRange([new Choice("System", Loc.T("Follow Windows")), new Choice("Light", Loc.T("Light")), new Choice("Dark", Loc.T("Dark"))]);
        _formatBox.Items.AddRange([ImageFileFormat.Png, ImageFileFormat.Jpeg, ImageFileFormat.Bmp]);

        Row(table, Loc.T("Language"), _languageBox, Loc.T("Restart SnapGit to apply the language and theme."));
        Row(table, Loc.T("Theme"), _themeBox);
        Row(table, string.Empty, _startupCheck);

        var browse = new Button { Text = Loc.T("Browse…"), AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _folderBox.Text, UseDescriptionForTitle = true, Description = Loc.T("Choose the library folder") };
            if (dialog.ShowDialog(this) == DialogResult.OK) _folderBox.Text = dialog.SelectedPath;
        };
        var folderPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderPanel.Controls.Add(_folderBox);
        folderPanel.Controls.Add(browse);
        Row(table, Loc.T("Library folder"), folderPanel);

        Row(table, Loc.T("File name"), _patternBox, Loc.T("Tokens: {title} {counter} {user} {machine} and date formats like {yyyy-MM-dd HH-mm-ss}."));
        Row(table, string.Empty, _projectCheck);

        var formatPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        formatPanel.Controls.Add(_formatBox);
        formatPanel.Controls.Add(new Label { Text = Loc.T("JPEG quality"), AutoSize = true, Margin = new Padding(12, 6, 4, 0) });
        formatPanel.Controls.Add(_qualityBox);
        Row(table, Loc.T("Image format"), formatPanel, Loc.T("Used when captures are saved as plain images and for exports."));
        return page;
    }

    private TabPage BuildCaptureTab()
    {
        var page = NewPage(Loc.T("Capture"));
        var table = (TableLayoutPanel)page.Controls[0];

        Row(table, string.Empty, _editorCheck);
        Row(table, string.Empty, _clipboardCheck);
        Row(table, string.Empty, _cursorCheck);
        Row(table, string.Empty, _printWindowCheck);
        Row(table, Loc.T("Delay (seconds)"), _delayBox, Loc.T("Countdown before the screen is frozen: time to open a menu or tooltip."));

        _fixedBox.Items.AddRange(["", "640x480", "800x600", "1280x720", "1920x1080"]);
        Row(table, Loc.T("Fixed region size"), _fixedBox, Loc.T("Empty = free selection. Example: 1280x720"));

        _scrollDirectionBox.Items.AddRange([new Choice("Vertical", Loc.T("Vertical")), new Choice("Horizontal", Loc.T("Horizontal (panorama)"))]);
        Row(table, Loc.T("Scrolling capture"), _scrollDirectionBox);
        Row(table, Loc.T("Wait between scrolls (ms)"), _scrollDelayBox, Loc.T("Increase for pages that load slowly."));
        Row(table, Loc.T("Maximum frames"), _scrollFramesBox);

        _ocrBox.Items.Add(new OcrLanguageItem(new OcrLanguage(string.Empty, Loc.T("Automatic (profile languages)"))));
        foreach (var language in _ocrLanguages) _ocrBox.Items.Add(language);
        Row(table, Loc.T("OCR language"), _ocrBox, Loc.T("Only languages with a Windows OCR pack installed are listed."));
        return page;
    }

    private TabPage BuildHotkeysTab()
    {
        var page = NewPage(Loc.T("Hotkeys"));
        var table = (TableLayoutPanel)page.Controls[0];

        foreach (var (action, label) in HotkeyRows)
        {
            var box = new HotkeyBox { Width = 200 };
            _hotkeyBoxes[action] = box;
            Row(table, Loc.T(label), box);
        }
        Row(table, string.Empty, new Label { Text = Loc.T("Click a box and press the combination. Backspace clears it. PrintScreen needs Windows' Snipping Tool shortcut turned off."), AutoSize = true, MaximumSize = new Size(520, 0) });
        return page;
    }

    private TabPage BuildProfilesTab()
    {
        var page = new TabPage(Loc.T("Profiles")) { Padding = new Padding(12) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(new Label { Text = Loc.T("A profile is a ready-made way to capture (mode, delay, fixed size, what happens afterwards). Run it from the tray or library \"Profiles\" menu."), AutoSize = true, MaximumSize = new Size(580, 0), Margin = new Padding(0, 0, 0, 8) });
        table.Controls.Add(_profileList);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var add = new Button { Text = Loc.T("Add…"), AutoSize = true };
        var edit = new Button { Text = Loc.T("Edit…"), AutoSize = true };
        var remove = new Button { Text = Loc.T("Remove"), AutoSize = true };
        add.Click += (_, _) => EditProfile(null);
        edit.Click += (_, _) => { if (_profileList.SelectedIndex >= 0) EditProfile(_profileList.SelectedIndex); };
        _profileList.DoubleClick += (_, _) => { if (_profileList.SelectedIndex >= 0) EditProfile(_profileList.SelectedIndex); };
        remove.Click += (_, _) => { if (_profileList.SelectedIndex >= 0) { _profiles.RemoveAt(_profileList.SelectedIndex); RefreshProfileList(); } };
        buttons.Controls.AddRange([add, edit, remove]);
        table.Controls.Add(buttons);

        page.Controls.Add(table);
        return page;
    }

    private void EditProfile(int? index)
    {
        var current = index is { } i ? _profiles[i] : new CaptureProfile { Name = Loc.T("New profile") };
        var dialog = new ParameterDialog(Loc.T("Capture profile"))
            .AddText("name", Loc.T("Name"), current.Name)
            .AddCombo("mode", Loc.T("Mode"), CaptureModeNames.All.Select(CaptureModeNames.Display), Math.Max(0, CaptureModeNames.All.ToList().IndexOf(current.Mode)))
            .AddNumber("delay", Loc.T("Delay (seconds)"), current.DelaySeconds, 0, 30)
            .AddText("fixed", Loc.T("Fixed region size"), current.FixedRegionSize, 120)
            .AddCheck("cursor", Loc.T("Include the mouse cursor"), current.IncludeCursor)
            .AddCheck("clipboard", Loc.T("Copy every capture to the clipboard"), current.CopyToClipboard)
            .AddCheck("editor", Loc.T("Open the editor after each capture"), current.OpenEditor);
        if (!dialog.Prompt(this)) return;

        var fixedSize = dialog.String("fixed").Trim();
        if (fixedSize.Length > 0 && new AppSettings { FixedRegionSize = fixedSize }.GetFixedRegionSize() is null)
        {
            MessageBox.Show(this, Loc.T("The fixed region size must look like 1280x720."), "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var profile = new CaptureProfile
        {
            Name = dialog.String("name").Trim(),
            Mode = CaptureModeNames.All[dialog.Index("mode")],
            DelaySeconds = dialog.Int("delay"),
            FixedRegionSize = fixedSize,
            IncludeCursor = dialog.Bool("cursor"),
            CopyToClipboard = dialog.Bool("clipboard"),
            OpenEditor = dialog.Bool("editor"),
        };
        if (profile.Name.Length == 0) return;

        if (index is { } existing) _profiles[existing] = profile;
        else _profiles.Add(profile);
        RefreshProfileList();
    }

    private void RefreshProfileList()
    {
        _profileList.Items.Clear();
        foreach (var p in _profiles)
            _profileList.Items.Add($"{p.Name}   —   {CaptureModeNames.Display(p.Mode)}" + (p.DelaySeconds > 0 ? $", {p.DelaySeconds} s" : string.Empty) + (p.FixedRegionSize.Length > 0 ? $", {p.FixedRegionSize}" : string.Empty));
    }

    private TabPage BuildRecordingTab()
    {
        var page = NewPage(Loc.T("Recording"));
        var table = (TableLayoutPanel)page.Controls[0];

        Row(table, Loc.T("GIF frames per second"), _gifFpsBox);
        Row(table, Loc.T("GIF maximum length (s)"), _gifSecondsBox, Loc.T("GIF frames are kept in memory until you stop."));
        _videoResolutionBox.Items.AddRange([
            new Choice("FullHd", Loc.T("Full HD 1080p (1920 × 1080) — recommended")),
            new Choice("Hd", Loc.T("HD 720p (1280 × 720) — smaller files")),
            new Choice("Original", Loc.T("Original size of the recorded area (no scaling)"))]);
        _videoQualityBox.Items.AddRange([
            new Choice("Standard", Loc.T("Standard — smallest files")),
            new Choice("High", Loc.T("High — sharp text (recommended)")),
            new Choice("Best", Loc.T("Best — largest files"))]);
        Row(table, Loc.T("Video resolution"), _videoResolutionBox, Loc.T("Large areas are scaled down to fit; small ones are enlarged at most 2×."));
        Row(table, Loc.T("Video quality"), _videoQualityBox, Loc.T("Higher quality keeps small text and thin lines crisp, at the price of bigger files."));
        Row(table, Loc.T("Video frames per second"), _videoFpsBox);
        Row(table, string.Empty, _recordCursorCheck);
        Row(table, string.Empty, _systemAudioCheck);
        Row(table, string.Empty, _microphoneCheck);
        return page;
    }

    private TabPage BuildSharingTab()
    {
        var page = new TabPage(Loc.T("Sharing")) { Padding = new Padding(12) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(new Label { Text = Loc.T("Your own \"Share to…\" destinations (a folder such as OneDrive, or a program). Passwords are never stored."), AutoSize = true, MaximumSize = new Size(580, 0), Margin = new Padding(0, 0, 0, 8) });
        table.Controls.Add(_shareList);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        var add = new Button { Text = Loc.T("Add…"), AutoSize = true };
        var edit = new Button { Text = Loc.T("Edit…"), AutoSize = true };
        var remove = new Button { Text = Loc.T("Remove"), AutoSize = true };
        add.Click += (_, _) => EditDestination(null);
        edit.Click += (_, _) => { if (_shareList.SelectedIndex >= 0) EditDestination(_shareList.SelectedIndex); };
        remove.Click += (_, _) => { if (_shareList.SelectedIndex >= 0) { _destinations.RemoveAt(_shareList.SelectedIndex); RefreshShareList(); } };
        buttons.Controls.AddRange([add, edit, remove]);
        table.Controls.Add(buttons);

        table.Controls.Add(new Label { Text = Loc.T("Update feed URL (optional, JSON with version/url/notes)"), AutoSize = true });
        table.Controls.Add(_feedBox);

        page.Controls.Add(table);
        return page;
    }

    private void EditDestination(int? index)
    {
        var current = index is { } i ? _destinations[i] : new ShareDestination { Kind = ShareKind.Folder };
        var dialog = new ParameterDialog(Loc.T("Share destination"))
            .AddText("name", Loc.T("Name"), current.Name)
            .AddCombo("kind", Loc.T("Type"), [Loc.T("Copy to folder"), Loc.T("Run program")], current.Kind == ShareKind.Program ? 1 : 0)
            .AddText("target", Loc.T("Folder or program path"), current.Target, 300)
            .AddText("args", Loc.T("Arguments ({file} = the file)"), current.Arguments, 300);
        if (!dialog.Prompt(this)) return;

        var destination = new ShareDestination
        {
            Name = dialog.String("name").Trim(),
            Kind = dialog.Index("kind") == 1 ? ShareKind.Program : ShareKind.Folder,
            Target = dialog.String("target").Trim(),
            Arguments = dialog.String("args").Trim(),
        };
        if (destination.Name.Length == 0 || destination.Target.Length == 0) return;

        if (index is { } existing) _destinations[existing] = destination;
        else _destinations.Add(destination);
        RefreshShareList();
    }

    private void RefreshShareList()
    {
        _shareList.Items.Clear();
        foreach (var d in _destinations) _shareList.Items.Add($"{d.Name}   —   {(d.Kind == ShareKind.Program ? Loc.T("program") : Loc.T("folder"))}: {d.Target}");
    }

    // ------------------------------------------------------------------ layout helpers

    private static TabPage NewPage(string title)
    {
        var page = new TabPage(title) { Padding = new Padding(12), AutoScroll = true };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(table);
        return page;
    }

    private static void Row(TableLayoutPanel table, string label, Control control, string? note = null)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 7), MaximumSize = new Size(180, 0) });
        if (note is null)
        {
            table.Controls.Add(control);
            return;
        }

        var cell = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        cell.Controls.Add(control);
        cell.Controls.Add(new Label { Text = note, AutoSize = true, Tag = Theme.NoteTag, MaximumSize = new Size(480, 0), Margin = new Padding(0, 2, 0, 4) });
        table.Controls.Add(cell);
    }

    // ------------------------------------------------------------------ load / save

    private void LoadFromSettings()
    {
        var s = _settings.Current;
        var gestures = s.GetGestures();
        foreach (var (action, box) in _hotkeyBoxes)
            box.Gesture = gestures.TryGetValue(action, out var g) ? g : null;

        _languageBox.SelectedIndex = Math.Max(0, Array.FindIndex(_languageBox.Items.Cast<Choice>().ToArray(), c => c.Value == s.Language));
        _themeBox.SelectedIndex = Math.Max(0, Array.FindIndex(_themeBox.Items.Cast<Choice>().ToArray(), c => c.Value == s.Theme.ToString()));
        _startupCheck.Checked = s.StartWithWindows;
        _folderBox.Text = s.LibraryFolder;
        _patternBox.Text = s.NamePattern;
        _projectCheck.Checked = s.SaveCapturesAsProject;
        _formatBox.SelectedItem = s.DefaultFormat is ImageFileFormat.Gif ? ImageFileFormat.Png : s.DefaultFormat;
        _qualityBox.Value = Math.Clamp(s.JpegQuality, 10, 100);

        _editorCheck.Checked = s.OpenEditorAfterCapture;
        _clipboardCheck.Checked = s.CopyToClipboardAfterCapture;
        _cursorCheck.Checked = s.IncludeCursor;
        _printWindowCheck.Checked = s.UsePrintWindowForWindows;
        _delayBox.Value = Math.Clamp(s.CaptureDelaySeconds, 0, 30);
        _fixedBox.Text = s.FixedRegionSize;
        _scrollDirectionBox.SelectedIndex = s.ScrollDirection == ScrollDirection.Horizontal ? 1 : 0;
        _scrollDelayBox.Value = Math.Clamp(s.ScrollDelayMs, 100, 2000);
        _scrollFramesBox.Value = Math.Clamp(s.ScrollMaxFrames, 2, 400);
        _ocrBox.SelectedIndex = Math.Max(0, Array.FindIndex(_ocrBox.Items.Cast<OcrLanguageItem>().ToArray(), l => l.Language.Tag.Equals(s.OcrLanguage, StringComparison.OrdinalIgnoreCase)));

        _gifFpsBox.Value = Math.Clamp(s.GifFramesPerSecond, 1, 30);
        _gifSecondsBox.Value = Math.Clamp(s.GifMaxSeconds, 3, 300);
        _videoFpsBox.Value = Math.Clamp(s.VideoFramesPerSecond, 5, 60);
        _videoResolutionBox.SelectedIndex = Math.Max(0, Array.FindIndex(_videoResolutionBox.Items.Cast<Choice>().ToArray(), c => c.Value == s.VideoResolution.ToString()));
        _videoQualityBox.SelectedIndex = Math.Max(0, Array.FindIndex(_videoQualityBox.Items.Cast<Choice>().ToArray(), c => c.Value == s.VideoQuality.ToString()));
        _recordCursorCheck.Checked = s.RecordCursor;
        _systemAudioCheck.Checked = s.RecordSystemAudio;
        _microphoneCheck.Checked = s.RecordMicrophone;

        _profiles.Clear();
        _profiles.AddRange(s.Profiles.Select(p => new CaptureProfile { Name = p.Name, Mode = p.Mode, DelaySeconds = p.DelaySeconds, FixedRegionSize = p.FixedRegionSize, IncludeCursor = p.IncludeCursor, CopyToClipboard = p.CopyToClipboard, OpenEditor = p.OpenEditor }));
        RefreshProfileList();
        _destinations.Clear();
        _destinations.AddRange(s.ShareDestinations.Select(d => new ShareDestination { Name = d.Name, Kind = d.Kind, Target = d.Target, Arguments = d.Arguments }));
        RefreshShareList();
        _feedBox.Text = s.UpdateFeedUrl;
    }

    private bool SaveToSettings()
    {
        // Two actions on the same key can never both register: catch it here with a clear message.
        var duplicate = _hotkeyBoxes.Where(p => p.Value.Gesture is not null)
            .GroupBy(p => p.Value.Gesture!.Value)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            MessageBox.Show(this, Loc.T("\"{0}\" is assigned to more than one action.", duplicate.Key), "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (_fixedBox.Text.Trim().Length > 0 && new AppSettings { FixedRegionSize = _fixedBox.Text }.GetFixedRegionSize() is null)
        {
            MessageBox.Show(this, Loc.T("The fixed region size must look like 1280x720."), "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var s = _settings.Current;
        s.Hotkeys = _hotkeyBoxes.ToDictionary(p => p.Key, p => p.Value.Gesture?.ToString() ?? string.Empty);

        s.Language = ((Choice)_languageBox.SelectedItem!).Value;
        s.Theme = Enum.Parse<AppTheme>(((Choice)_themeBox.SelectedItem!).Value);
        s.StartWithWindows = _startupCheck.Checked;
        s.LibraryFolder = _folderBox.Text;
        s.NamePattern = string.IsNullOrWhiteSpace(_patternBox.Text) ? NamePattern.Default : _patternBox.Text;
        s.SaveCapturesAsProject = _projectCheck.Checked;
        s.DefaultFormat = (ImageFileFormat)_formatBox.SelectedItem!;
        s.JpegQuality = (int)_qualityBox.Value;

        s.OpenEditorAfterCapture = _editorCheck.Checked;
        s.CopyToClipboardAfterCapture = _clipboardCheck.Checked;
        s.IncludeCursor = _cursorCheck.Checked;
        s.UsePrintWindowForWindows = _printWindowCheck.Checked;
        s.CaptureDelaySeconds = (int)_delayBox.Value;
        s.FixedRegionSize = _fixedBox.Text.Trim();
        s.ScrollDirection = _scrollDirectionBox.SelectedIndex == 1 ? ScrollDirection.Horizontal : ScrollDirection.Vertical;
        s.ScrollDelayMs = (int)_scrollDelayBox.Value;
        s.ScrollMaxFrames = (int)_scrollFramesBox.Value;
        s.OcrLanguage = (_ocrBox.SelectedItem as OcrLanguageItem)?.Language.Tag ?? string.Empty;

        s.GifFramesPerSecond = (int)_gifFpsBox.Value;
        s.GifMaxSeconds = (int)_gifSecondsBox.Value;
        s.VideoFramesPerSecond = (int)_videoFpsBox.Value;
        s.VideoResolution = Enum.Parse<SnapGit.Core.Recording.VideoResolution>(((Choice)_videoResolutionBox.SelectedItem!).Value);
        s.VideoQuality = Enum.Parse<SnapGit.Core.Recording.VideoQuality>(((Choice)_videoQualityBox.SelectedItem!).Value);
        s.RecordCursor = _recordCursorCheck.Checked;
        s.RecordSystemAudio = _systemAudioCheck.Checked;
        s.RecordMicrophone = _microphoneCheck.Checked;

        s.ShareDestinations = _destinations.ToList();
        s.Profiles = _profiles.ToList();
        s.UpdateFeedUrl = _feedBox.Text.Trim();

        try
        {
            _settings.Save();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("Could not save settings:\n\n{0}", ex.Message), "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private sealed record Choice(string Value, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record OcrLanguageItem(OcrLanguage Language)
    {
        public override string ToString() => string.IsNullOrEmpty(Language.Tag) ? Language.DisplayName : $"{Language.DisplayName} ({Language.Tag})";
    }
}
