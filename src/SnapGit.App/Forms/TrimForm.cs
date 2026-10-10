using SnapGit.App.Services;
using SnapGit.App.Ui;
using SnapGit.Core.Imaging;
using SnapGit.Core.Library;
using SnapGit.Core.Localization;
using SnapGit.Platform.Recording;

namespace SnapGit.App.Forms;

/// <summary>
/// Cuts a GIF (by frames, with a live preview) or a video (by time) to a new library item.
/// The original is never touched.
/// </summary>
public sealed class TrimForm : Form
{
    private readonly AppHost _host;
    private readonly LibraryItem _item;
    private readonly bool _isGif;

    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(40, 40, 44) };
    private readonly TrackBar _start = new() { Dock = DockStyle.Fill, TickStyle = TickStyle.None };
    private readonly TrackBar _end = new() { Dock = DockStyle.Fill, TickStyle = TickStyle.None };
    private readonly Label _startLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _endLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _summary = new() { AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _save = new() { AutoSize = true };
    private readonly Button _play = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer _playTimer = new();

    private List<GifFrame> _frames = new();
    private TimeSpan _duration;
    private int _playIndex;

    public TrimForm(AppHost host, LibraryItem item)
    {
        _host = host;
        _item = item;
        _isGif = item.Kind == LibraryItemKind.Animation;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Text = Loc.T("Trim") + " — " + item.Name;
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 560);
        MinimumSize = new Size(520, 420);
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(_preview, 0, 0);
        layout.SetColumnSpan(_preview, 2);
        layout.Controls.Add(_startLabel, 0, 1);
        layout.Controls.Add(_start, 1, 1);
        layout.Controls.Add(_endLabel, 0, 2);
        layout.Controls.Add(_end, 1, 2);
        layout.Controls.Add(_summary, 0, 3);
        layout.SetColumnSpan(_summary, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = Loc.T("Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
        _save.Text = Loc.T("Save as new item");
        _play.Text = Loc.T("▶ Play selection");
        _play.Visible = _isGif;
        buttons.Controls.AddRange([cancel, _save, _play]);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        CancelButton = cancel;

        _start.ValueChanged += (_, _) => OnRangeChanged(startMoved: true);
        _end.ValueChanged += (_, _) => OnRangeChanged(startMoved: false);
        _save.Click += async (_, _) => await SaveAsync();
        _play.Click += (_, _) => TogglePlay();
        _playTimer.Tick += (_, _) => PlayNext();

        Theme.Apply(this);
        Shown += async (_, _) => await LoadAsync();
        FormClosed += (_, _) => { _playTimer.Dispose(); DisposeFrames(); _preview.Image = null; };
    }

    private async Task LoadAsync()
    {
        _save.Enabled = false;
        try
        {
            if (_isGif)
            {
                _frames = await Task.Run(() => GifTrimmer.ReadFrames(_item.FilePath));
                Configure(maximum: Math.Max(0, _frames.Count - 1));
            }
            else
            {
                _duration = await VideoTrimmer.GetDurationAsync(_item.FilePath);
                using var poster = await ThumbnailLoader.LoadAsync(_item);
                if (poster is not null) _preview.Image = new Bitmap(poster);
                Configure(maximum: (int)Math.Max(1, _duration.TotalSeconds * 10)); // 0.1 s steps
            }
            _save.Enabled = true;
        }
        catch (Exception ex)
        {
            _summary.Text = ex.Message;
        }
    }

    private void Configure(int maximum)
    {
        _start.Minimum = _end.Minimum = 0;
        _start.Maximum = _end.Maximum = maximum;
        _start.Value = 0;
        _end.Value = maximum;
        OnRangeChanged(startMoved: true);
    }

    private void OnRangeChanged(bool startMoved)
    {
        // The two handles may touch but never cross: the one being moved pushes the other.
        if (_start.Value > _end.Value)
        {
            if (startMoved) _end.Value = _start.Value;
            else _start.Value = _end.Value;
        }

        if (_isGif)
        {
            _startLabel.Text = Loc.T("First frame: {0}", _start.Value + 1);
            _endLabel.Text = Loc.T("Last frame: {0}", _end.Value + 1);
            var kept = _frames.Skip(_start.Value).Take(_end.Value - _start.Value + 1).ToList();
            _summary.Text = Loc.T("{0} of {1} frame(s) kept, {2:0.0} s", kept.Count, _frames.Count, GifTrimmer.TotalMilliseconds(kept) / 1000.0);
            ShowFrame(startMoved ? _start.Value : _end.Value);
        }
        else
        {
            var from = _start.Value / 10.0;
            var to = _end.Value / 10.0;
            _startLabel.Text = Loc.T("Start: {0:0.0} s", from);
            _endLabel.Text = Loc.T("End: {0:0.0} s", to);
            _summary.Text = Loc.T("Keeping {0:0.0} s of {1:0.0} s", to - from, _duration.TotalSeconds);
        }
        _save.Enabled = _end.Value > _start.Value || _isGif;
    }

    private void ShowFrame(int index)
    {
        if (_frames.Count == 0) return;
        index = Math.Clamp(index, 0, _frames.Count - 1);
        _preview.Image = _frames[index].Image;
    }

    private void TogglePlay()
    {
        if (_playTimer.Enabled)
        {
            _playTimer.Stop();
            _play.Text = Loc.T("▶ Play selection");
            return;
        }

        _playIndex = _start.Value;
        _play.Text = Loc.T("■ Stop");
        PlayNext();
    }

    private void PlayNext()
    {
        if (_playIndex > _end.Value) _playIndex = _start.Value;
        ShowFrame(_playIndex);
        _playTimer.Interval = Math.Max(20, _frames[Math.Clamp(_playIndex, 0, _frames.Count - 1)].DelayMs);
        _playTimer.Start();
        _playIndex++;
    }

    private async Task SaveAsync()
    {
        _playTimer.Stop();
        _save.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var target = _host.Library.ReservePath(_isGif ? ".gif" : ".mp4", _item.Name + " " + Loc.T("(trimmed)"));
            if (_isGif)
            {
                int first = _start.Value, last = _end.Value;
                await Task.Run(() => GifTrimmer.Trim(_item.FilePath, target, first, last));
            }
            else
            {
                await VideoTrimmer.TrimAsync(_item.FilePath, target, TimeSpan.FromSeconds(_start.Value / 10.0), TimeSpan.FromSeconds(_end.Value / 10.0));
            }

            _host.Library.NotifyModified(target);
            _host.Notify(Loc.T("Saved {0}", Path.GetFileNameWithoutExtension(target)), () => _host.OpenItem(target));
            Close();
        }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            _save.Enabled = true;
            MessageBox.Show(this, Loc.T("Could not trim: {0}", ex.Message), "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DisposeFrames()
    {
        foreach (var frame in _frames) frame.Image.Dispose();
        _frames = new List<GifFrame>();
    }
}
