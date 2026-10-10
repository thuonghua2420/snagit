using SnapGit.App.Controls;
using SnapGit.App.Services;
using SnapGit.App.Ui;
using SnapGit.Core.Diagnostics;
using SnapGit.Core.Localization;
using SnapGit.Core.Recording;
using SnapGit.Platform.Recording;

namespace SnapGit.App.Forms;

/// <summary>
/// Edits a recorded video right in the app: play it, cut parts out or keep one range, rotate/flip, crop, mute, burn in text, grab a frame
/// into the image editor, and export the result as a NEW library item (the original is never touched). Every edit is a
/// <see cref="VideoEditPlan"/> on an undo stack; nothing is rendered until Export.
/// </summary>
public sealed partial class VideoEditorPage : UserControl
{
    private readonly AppHost _host;
    private readonly string _path;
    private readonly string _name;
    private readonly VideoPlayback _playback = new();
    private readonly VideoView _view = new() { Dock = DockStyle.Fill };
    private readonly TimelineBar _timeline = new() { Dock = DockStyle.Bottom };
    private readonly List<VideoEditPlan> _history = [];
    private int _index = -1;
    private VideoEditPlan? _savedPlan;
    private CancellationTokenSource? _exportCancel;
    private bool _loaded;
    private bool _syncing;

    public VideoEditorPage(AppHost host, string path)
    {
        _host = host;
        _path = path;
        _name = Path.GetFileNameWithoutExtension(path);

        Dock = DockStyle.Fill;

        var bar = BuildToolbar();
        var transport = BuildTransport();
        var side = BuildCaptionPanel();
        var status = BuildStatus();

        // later added = docked earlier (closer to the edge): status is the outermost bottom row, the picture fills what is left.
        Controls.Add(_view);
        Controls.Add(side);
        Controls.Add(transport);
        Controls.Add(_timeline);
        Controls.Add(status);
        Controls.Add(bar);

        _timeline.SeekRequested += time => SeekTo(time);
        _timeline.SelectionChanged += UpdateButtons;
        _view.CropChanged += UpdateInfo;
        _playback.FrameReady += OnFrame;
        _playback.Ended += () => { UpdatePlayButton(); UpdateInfo(); };

        Theme.ApplyTo(this);
    }

    // Test seams (visible to SnapGit.Tests only).
    internal VideoEditPlan Plan => _history[_index];
    internal VideoPlayback Playback => _playback;
    internal TimelineBar Timeline => _timeline;
    internal VideoView View => _view;
    internal bool Loaded => _loaded;

    public string SourcePath => _path;

    /// <summary>The video's name (for the window title).</summary>
    public string DisplayName => _name;

    private Form? Window => FindForm();

    /// <summary>Opens the video (call once, after the page is on the window).</summary>
    public async Task OpenAsync()
    {
        try
        {
            await _playback.OpenAsync(_path);
            _timeline.Duration = _playback.Duration;
            _history.Clear();
            _index = -1;
            Push(VideoEditPlan.For(_playback.Duration, _playback.Size));
            _savedPlan = Plan;
            _loaded = true;
            _playback.Seek(TimeSpan.Zero);
            UpdateButtons();
            UpdateInfo();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open video {_path}", ex);
            _hint.Text = Loc.T("Could not open the video: {0}", ex.Message);
        }
    }

    // ------------------------------------------------------------------ plan history

    private void Push(VideoEditPlan plan)
    {
        if (_index >= 0 && ReferenceEquals(plan, Plan)) return;
        if (_index < _history.Count - 1) _history.RemoveRange(_index + 1, _history.Count - _index - 1);
        _history.Add(plan);
        _index = _history.Count - 1;
        ApplyPlan();
    }

    internal void Undo()
    {
        if (_index <= 0 || _exporting) return;
        _index--;
        ApplyPlan();
    }

    internal void Redo()
    {
        if (_index >= _history.Count - 1 || _exporting) return;
        _index++;
        ApplyPlan();
    }

    private void ApplyPlan()
    {
        var plan = Plan;
        _view.Plan = plan;
        _timeline.Plan = plan;
        _playback.Muted = plan.Mute;
        RefreshCaptions();
        UpdateButtons();
        UpdateInfo();
    }

    /// <summary>Applies an edit to the current plan as one undo step (the toolbar commands all end up here).</summary>
    internal void ApplyEdit(Func<VideoEditPlan, VideoEditPlan> edit) => Push(edit(Plan));

    internal bool IsDirty => _index >= 0 && !ReferenceEquals(Plan, _savedPlan) && !Plan.IsUnchanged;

    /// <summary>Forget the edits (so closing does not ask). Used by tests.</summary>
    internal void MarkClean() => _savedPlan = _index >= 0 ? Plan : null;

    /// <summary>True when the page may be replaced or closed: nothing unexported, or the user agreed to throw the edits away.</summary>
    public bool TryClose()
    {
        if (!IsDirty) return true;
        var answer = MessageBox.Show(Window, Loc.T("Close without exporting your edits?"), "SnapGit", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return answer == DialogResult.Yes;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible && _loaded && _playback.IsPlaying)
        {
            _playback.Pause();
            UpdatePlayButton();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _exportCancel?.Cancel();
            _playback.Dispose();
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ playback

    private void OnFrame(Bitmap frame, TimeSpan at)
    {
        if (IsDisposed) { frame.Dispose(); return; }
        _view.SetFrame(frame, at);
        _timeline.Position = at;
        UpdateInfo();

        // Parts that were cut out are skipped during playback.
        if (_playback.IsPlaying && _index >= 0 && !Plan.IsKept(at))
        {
            if (Plan.NextKeptStart(at) is { } next) _playback.Seek(next);
            else
            {
                _playback.Pause();
                var end = Plan.Keep.Count > 0 ? Plan.Keep[^1].End - TimeSpan.FromMilliseconds(40) : TimeSpan.Zero;
                _playback.Seek(end);
                UpdatePlayButton();
            }
        }
    }

    private void SeekTo(TimeSpan time)
    {
        if (!_loaded) return;
        _timeline.Position = time;
        _playback.Seek(time);
        UpdateInfo();
    }

    internal void TogglePlay()
    {
        if (!_loaded || _exporting) return;
        if (_playback.IsPlaying) _playback.Pause();
        else
        {
            if (_view.CropMode) SetCropMode(false);
            // start inside the kept parts
            if (!Plan.IsKept(_playback.Position) && Plan.NextKeptStart(_playback.Position) is { } next) _playback.Seek(next);
            else if (!Plan.IsKept(_playback.Position) && Plan.Keep.Count > 0) _playback.Seek(Plan.Keep[0].Start);
            _playback.Play();
        }
        UpdatePlayButton();
    }

    private void Step(TimeSpan delta) => SeekTo(_playback.Position + delta);

    // ------------------------------------------------------------------ selection edits

    private void SetSelectionStart()
    {
        if (!_loaded) return;
        var end = _timeline.Selection is { } s && s.End > _playback.Position ? s.End : _playback.Duration;
        _timeline.Selection = new TimeRange(_playback.Position, end);
    }

    private void SetSelectionEnd()
    {
        if (!_loaded) return;
        var start = _timeline.Selection is { } s && s.Start < _playback.Position ? s.Start : TimeSpan.Zero;
        _timeline.Selection = new TimeRange(start, _playback.Position);
    }

    /// <summary>Keeps only the selected range (everything else is cut away).</summary>
    internal void KeepSelection() => EditRange(keep: true);

    /// <summary>Cuts the selected range out; what is left on both sides is joined.</summary>
    internal void DeleteSelection() => EditRange(keep: false);

    private void EditRange(bool keep)
    {
        if (!_loaded || _exporting || _timeline.Selection is not { } selection || selection.Length < VideoEditPlan.MinPiece) return;
        var result = keep ? Plan.KeepOnly(selection.Start, selection.End) : Plan.Remove(selection.Start, selection.End);
        if (result.Keep.Count == 0)
        {
            _hint.Text = Loc.T("That would leave nothing of the video.");
            return;
        }

        Push(result);
        _timeline.Selection = null;
        if (!result.IsKept(_playback.Position)) SeekTo(result.NextKeptStart(_playback.Position) ?? result.Keep[0].Start);
    }

    // ------------------------------------------------------------------ picture edits

    internal void Rotate(bool clockwise)
    {
        if (!_loaded || _exporting) return;
        if (_view.CropMode) SetCropMode(false);
        Push(clockwise ? Plan.RotateClockwise() : Plan.RotateCounterClockwise());
    }

    internal void Flip(bool horizontal)
    {
        if (!_loaded || _exporting) return;
        if (_view.CropMode) SetCropMode(false);
        Push(horizontal ? Plan.FlipHorizontally() : Plan.FlipVertically());
    }

    internal void ToggleMute()
    {
        if (!_loaded || _exporting) return;
        Push(Plan with { Mute = !Plan.Mute });
    }

    /// <summary>Turns the crop rectangle on (draw it on the picture) or off (apply it).</summary>
    internal void SetCropMode(bool on)
    {
        if (!_loaded || _view.CropMode == on) return;
        if (on)
        {
            if (_playback.IsPlaying) { _playback.Pause(); UpdatePlayButton(); }
            _view.CropMode = true;
            _hint.Text = Loc.T("Drag on the picture to choose the area to keep. Enter applies it, Esc cancels.");
        }
        else
        {
            var draft = _view.CropDraft;
            _view.CropMode = false;
            var full = new Rectangle(Point.Empty, Plan.ViewSize);
            var crop = draft == full || draft.Width < 4 || draft.Height < 4 ? (Rectangle?)null : draft;
            Push(Plan with { Crop = crop });
            _hint.Text = string.Empty;
        }
        _syncing = true;
        _cropButton.Checked = on;
        _syncing = false;
    }

    private void CancelCrop()
    {
        if (!_view.CropMode) return;
        _view.CropMode = false;
        _syncing = true;
        _cropButton.Checked = false;
        _syncing = false;
        _hint.Text = string.Empty;
    }

    // ------------------------------------------------------------------ frame → image editor

    private async Task SnapshotAsync()
    {
        if (!_loaded) return;
        var at = _playback.Position;
        try
        {
            using var raw = await VideoEditor.GetFrameAsync(_path, at, Plan.SourceSize.Width);
            using var picture = VideoFrameTransform.Apply(raw, Plan, at);
            _host.OpenNewInEditor(new Bitmap(picture));
        }
        catch (Exception ex)
        {
            Log.Error("Could not grab a frame", ex);
            _hint.Text = Loc.T("Could not grab the frame: {0}", ex.Message);
        }
    }

    // ------------------------------------------------------------------ export

    private bool _exporting;

    /// <summary>Renders the plan to a new library item. Returns its path, or null when cancelled / failed.</summary>
    public async Task<string?> ExportAsync()
    {
        if (!_loaded || _exporting || Plan.Keep.Count == 0) return null;
        if (_view.CropMode) SetCropMode(false);
        _playback.Pause();
        UpdatePlayButton();

        var plan = Plan;
        var target = _host.Library.ReservePath(".mp4", _name + " " + Loc.T("(edited)"));
        _exportCancel = new CancellationTokenSource();
        SetExporting(true);
        try
        {
            await VideoEditor.RenderAsync(_path, target, plan, new Progress<double>(p => _progress.Value = Math.Clamp((int)(p * 100), 0, 100)), _exportCancel.Token);
            _host.Library.NotifyModified(target);
            _savedPlan = plan;
            _host.Notify(Loc.T("Saved {0}", Path.GetFileNameWithoutExtension(target)), () => _host.OpenItem(target));
            _hint.Text = Loc.T("Exported {0}", Path.GetFileName(target));
            return target;
        }
        catch (OperationCanceledException)
        {
            _hint.Text = Loc.T("Export cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Video export failed", ex);
            _hint.Text = Loc.T("Export failed: {0}", ex.Message);
            if (!IsDisposed && Visible) MessageBox.Show(Window, Loc.T("Could not export the video: {0}", ex.Message), "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            _exportCancel?.Dispose();
            _exportCancel = null;
            if (!IsDisposed) SetExporting(false);
        }
    }

    private void SetExporting(bool on)
    {
        _exporting = on;
        _progress.Visible = on;
        _cancelExport.Visible = on;
        _progress.Value = 0;
        UseWaitCursor = false;
        UpdateButtons();
        if (on) _hint.Text = Loc.T("Rendering the video…");
    }

    // ------------------------------------------------------------------ keys

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ActiveControl is TextBoxBase or ComboBox or NumericUpDown) return base.ProcessCmdKey(ref msg, keyData);

        switch (keyData)
        {
            case Keys.Space: TogglePlay(); return true;
            case Keys.I: SetSelectionStart(); return true;
            case Keys.O: SetSelectionEnd(); return true;
            case Keys.Delete: DeleteSelection(); return true;
            case Keys.Left: Step(TimeSpan.FromMilliseconds(-100)); return true;
            case Keys.Right: Step(TimeSpan.FromMilliseconds(100)); return true;
            case Keys.Shift | Keys.Left: Step(TimeSpan.FromSeconds(-1)); return true;
            case Keys.Shift | Keys.Right: Step(TimeSpan.FromSeconds(1)); return true;
            case Keys.Home: SeekTo(TimeSpan.Zero); return true;
            case Keys.End: SeekTo(_playback.Duration); return true;
            case Keys.Control | Keys.Z: Undo(); return true;
            case Keys.Control | Keys.Y: Redo(); return true;
            case Keys.Control | Keys.E: _ = ExportAsync(); return true;
            case Keys.Enter when _view.CropMode: SetCropMode(false); return true;
            case Keys.Escape when _view.CropMode: CancelCrop(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
