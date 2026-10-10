using SnapGit.App.Ui;
using SnapGit.Core.Localization;
using SnapGit.Core.Recording;

namespace SnapGit.App.Forms;

/// <summary>Construction of the video editor's bars and side panel, and the code that keeps them in step with the plan.</summary>
public sealed partial class VideoEditorPage
{
    private readonly ToolStripStatusLabel _hint = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _progress = new() { Visible = false, Width = 180 };
    private readonly ToolStripButton _cancelExport = new() { Visible = false, DisplayStyle = ToolStripItemDisplayStyle.Text };

    private readonly ToolStripButton _playButton = new();
    private readonly ToolStripLabel _timeLabel = new() { Margin = new Padding(8, 1, 8, 2) };
    private readonly ToolStripLabel _resultLabel = new() { Alignment = ToolStripItemAlignment.Right };

    private readonly ToolStripButton _exportButton = new();
    private readonly ToolStripButton _snapshotButton = new();
    private readonly ToolStripButton _keepButton = new();
    private readonly ToolStripButton _cutButton = new();
    private readonly ToolStripButton _undoButton = new();
    private readonly ToolStripButton _redoButton = new();
    private readonly ToolStripButton _cropButton = new() { CheckOnClick = true };
    private readonly ToolStripButton _muteButton = new();

    private readonly ListBox _captionList = new() { Dock = DockStyle.Fill, IntegralHeight = false, DisplayMember = nameof(CaptionItem.Label) };
    private readonly Button _addCaption = new() { AutoSize = true };
    private readonly Button _editCaption = new() { AutoSize = true };
    private readonly Button _removeCaption = new() { AutoSize = true };

    private sealed record CaptionItem(VideoCaption Caption, string Label);

    /// <summary>Icon button wired to <paramref name="onClick"/>; <paramref name="withText"/> shows the caption next to the icon.</summary>
    private static void Setup(ToolStripButton button, UiIcon icon, string caption, string tip, Action onClick, bool withText = false)
    {
        button.Image = Icons.Get(icon);
        button.Text = withText ? caption : string.Empty;
        button.ToolTipText = tip;
        button.AccessibleName = tip;
        button.DisplayStyle = withText ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image;
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
        button.Padding = new Padding(8, 8, withText ? 10 : 8, 8);
        button.Margin = new Padding(1);
        button.AutoSize = true;
        button.Click += (_, _) => onClick();
    }

    private ToolStrip BuildToolbar()
    {
        var strip = Toolbars.NewBar(DockStyle.Top);

        var library = new ToolStripButton();
        Setup(library, UiIcon.Library, Loc.T("Library"), Loc.T("Show every capture so far"), () => _host.ShowLibrary(), withText: true);
        strip.Items.AddRange([library, Toolbars.Separator()]);

        Setup(_exportButton, UiIcon.Export, Loc.T("Export"), Loc.T("Render the edited video as a new file (Ctrl+E)"), () => _ = ExportAsync(), withText: true);
        _exportButton.Tag = ModernRenderer.PrimaryTag;                       // the accent button of this page
        _exportButton.Image = Icons.Get(UiIcon.Export, Theme.Colors.OnAccent);
        _exportButton.Padding = new Padding(10, 8, 12, 8);
        Setup(_snapshotButton, UiIcon.Camera, Loc.T("Grab frame"), Loc.T("Open the picture at the playhead in the image editor"), () => _ = SnapshotAsync());
        strip.Items.AddRange([_exportButton, _snapshotButton, Toolbars.Separator()]);

        Setup(_undoButton, UiIcon.Undo, Loc.T("Undo"), Loc.T("Undo (Ctrl+Z)"), Undo);
        Setup(_redoButton, UiIcon.Redo, Loc.T("Redo"), Loc.T("Redo (Ctrl+Y)"), Redo);
        strip.Items.AddRange([_undoButton, _redoButton, Toolbars.Separator()]);

        Setup(_keepButton, UiIcon.Trim, Loc.T("Keep selection"), Loc.T("Keep only the selected part and cut the rest away"), KeepSelection, withText: true);
        Setup(_cutButton, UiIcon.CutOut, Loc.T("Cut out selection"), Loc.T("Remove the selected part (Delete); the rest is joined"), DeleteSelection, withText: true);
        strip.Items.AddRange([_keepButton, _cutButton, Toolbars.Separator()]);

        var rotateLeft = new ToolStripButton();
        var rotateRight = new ToolStripButton();
        var flipH = new ToolStripButton();
        var flipV = new ToolStripButton();
        Setup(rotateLeft, UiIcon.RotateLeft, Loc.T("Rotate left"), Loc.T("Rotate the picture a quarter turn counter-clockwise"), () => Rotate(clockwise: false));
        Setup(rotateRight, UiIcon.RotateRight, Loc.T("Rotate right"), Loc.T("Rotate the picture a quarter turn clockwise"), () => Rotate(clockwise: true));
        Setup(flipH, UiIcon.FlipHorizontal, Loc.T("Flip horizontally"), Loc.T("Mirror the picture left ↔ right"), () => Flip(horizontal: true));
        Setup(flipV, UiIcon.FlipVertical, Loc.T("Flip vertically"), Loc.T("Mirror the picture top ↔ bottom"), () => Flip(horizontal: false));
        Setup(_cropButton, UiIcon.Crop, Loc.T("Crop"), Loc.T("Choose the area of the picture to keep"), () => { if (!_syncing) SetCropMode(_cropButton.Checked); });

        strip.Items.AddRange([rotateLeft, rotateRight, flipH, flipV, _cropButton, Toolbars.Separator()]);

        Setup(_muteButton, UiIcon.Volume, Loc.T("Mute"), Loc.T("Remove the sound from the exported video"), ToggleMute);
        strip.Items.Add(_muteButton);
        return strip;
    }

    private ToolStrip BuildTransport()
    {
        var strip = Toolbars.NewBar(DockStyle.Bottom);
        strip.Padding = new Padding(8, 0, 8, 0);
        _playButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        _playButton.Click += (_, _) => TogglePlay();
        strip.Items.Add(_playButton);
        strip.Items.Add(_timeLabel);
        strip.Items.Add(_resultLabel);
        UpdatePlayButton();
        return strip;
    }

    private Control BuildCaptionPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Right, Width = 210, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label { Text = Loc.T("Text on the video"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) }, 0, 0);
        panel.Controls.Add(_captionList, 0, 1);

        _addCaption.Text = Loc.T("Add text…");
        _editCaption.Text = Loc.T("Edit…");
        _removeCaption.Text = Loc.T("Remove");
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange([_addCaption, _editCaption, _removeCaption]);
        panel.Controls.Add(buttons, 0, 2);

        _addCaption.Click += (_, _) => EditCaption(null);
        _editCaption.Click += (_, _) => { if (_captionList.SelectedItem is CaptionItem item) EditCaption(item.Caption); };
        _removeCaption.Click += (_, _) => { if (_captionList.SelectedItem is CaptionItem item) Push(Plan.WithoutCaption(item.Caption)); };
        _captionList.DoubleClick += (_, _) => { if (_captionList.SelectedItem is CaptionItem item) EditCaption(item.Caption); };
        _captionList.SelectedIndexChanged += (_, _) =>
        {
            if (!_syncing && _captionList.SelectedItem is CaptionItem item) SeekTo(item.Caption.Start);
            UpdateButtons();
        };
        return panel;
    }

    private StatusStrip BuildStatus()
    {
        _cancelExport.Text = Loc.T("Cancel");
        _cancelExport.Click += (_, _) => _exportCancel?.Cancel();
        var strip = new StatusStrip { SizingGrip = false };
        strip.Items.AddRange([_hint, _progress, _cancelExport]);
        _hint.Text = Loc.T("Loading video…");
        return strip;
    }

    // ------------------------------------------------------------------ state

    private void UpdatePlayButton()
    {
        var playing = _playback.IsPlaying;
        _playButton.Image = Icons.Get(playing ? UiIcon.Pause : UiIcon.Play);
        _playButton.Text = playing ? Loc.T("Pause") : Loc.T("Play");
        _playButton.ToolTipText = Loc.T("Play / pause (Space)");
        _playButton.AccessibleName = _playButton.ToolTipText;
    }

    private void UpdateButtons()
    {
        var ready = _loaded && !_exporting && _index >= 0;
        var hasSelection = ready && _timeline.Selection is { } s && s.Length >= VideoEditPlan.MinPiece;

        _exportButton.Enabled = ready;
        _snapshotButton.Enabled = ready;
        _undoButton.Enabled = ready && _index > 0;
        _redoButton.Enabled = ready && _index < _history.Count - 1;
        _keepButton.Enabled = hasSelection;
        _cutButton.Enabled = hasSelection;
        _cropButton.Enabled = ready;
        _muteButton.Enabled = ready;
        _playButton.Enabled = ready;
        _addCaption.Enabled = ready;
        _editCaption.Enabled = _removeCaption.Enabled = ready && _captionList.SelectedItem is not null;

        if (_index >= 0)
        {
            _muteButton.Image = Icons.Get(Plan.Mute ? UiIcon.Muted : UiIcon.Volume);
            _muteButton.Text = Plan.Mute ? Loc.T("Muted") : Loc.T("Mute");
            _muteButton.Checked = Plan.Mute;
        }
    }

    private void UpdateInfo()
    {
        if (!_loaded || _index < 0) return;
        _timeLabel.Text = $"{TimeText.Format(_playback.Position)} / {TimeText.Format(_playback.Duration)}";

        var plan = Plan;
        var size = _view.CropMode ? _view.CropDraft.Size : plan.OutputSize;
        _resultLabel.Text = Loc.T("Result: {0} · {1} × {2}", TimeText.Format(plan.KeptDuration), size.Width & ~1, size.Height & ~1);
    }

    // ------------------------------------------------------------------ captions

    private void RefreshCaptions()
    {
        _syncing = true;
        try
        {
            var selected = (_captionList.SelectedItem as CaptionItem)?.Caption;
            _captionList.Items.Clear();
            foreach (var caption in Plan.Captions)
            {
                var text = caption.Text.ReplaceLineEndings(" ");
                if (text.Length > 28) text = text[..27] + "…";
                _captionList.Items.Add(new CaptionItem(caption, $"{text}   {TimeText.Format(caption.Start)}–{TimeText.Format(caption.End)}"));
            }
            for (var i = 0; i < _captionList.Items.Count; i++)
                if (ReferenceEquals(((CaptionItem)_captionList.Items[i]!).Caption, selected)) _captionList.SelectedIndex = i;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void EditCaption(VideoCaption? existing)
    {
        if (!_loaded) return;
        var duration = _playback.Duration;
        var start = existing?.Start ?? _playback.Position;
        var length = existing is null ? TimeSpan.FromSeconds(Math.Min(3, Math.Max(0.5, (duration - start).TotalSeconds))) : existing.End - existing.Start;

        var dialog = new ParameterDialog(existing is null ? Loc.T("Add text") : Loc.T("Edit text"))
            .AddMultiline("text", Loc.T("Text"), existing?.Text ?? string.Empty, 70)
            .AddCombo("position", Loc.T("Position"), [Loc.T("Top"), Loc.T("Middle"), Loc.T("Bottom")], (int)(existing?.Position ?? CaptionPosition.Bottom))
            .AddNumber("size", Loc.T("Size (% of the height)"), (decimal)(existing?.SizePercent ?? 7f), 2, 30, 1)
            .AddColor("color", Loc.T("Color"), existing is null ? Color.White : Color.FromArgb(existing.ColorArgb))
            .AddNumber("start", Loc.T("Appears at (seconds)"), (decimal)Math.Round(start.TotalSeconds, 1), 0, (decimal)Math.Max(0.1, duration.TotalSeconds), 1)
            .AddNumber("length", Loc.T("Stays for (seconds)"), (decimal)Math.Round(length.TotalSeconds, 1), 0.1m, 3600, 1);
        if (!dialog.Prompt(Window)) return;

        var text = dialog.String("text").Trim();
        if (text.Length == 0) return;
        var from = TimeSpan.FromSeconds(dialog.Double("start"));
        var caption = new VideoCaption(text, from, from + TimeSpan.FromSeconds(dialog.Double("length")), (CaptionPosition)dialog.Index("position"),
            (float)dialog.Double("size"), dialog.ColorValue("color").ToArgb());

        var plan = existing is null ? Plan : Plan.WithoutCaption(existing);
        Push(plan.WithCaption(caption));
    }
}
