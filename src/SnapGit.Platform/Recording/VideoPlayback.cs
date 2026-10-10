using System.Diagnostics;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Storage;

namespace SnapGit.Platform.Recording;

/// <summary>
/// Preview playback for the video editor. The clock is a <see cref="MediaPlayer"/> (so there is sound and the timing is right);
/// the picture is pulled from a <see cref="MediaComposition"/> at the player's position as fast as it can be decoded, dropping frames when it
/// falls behind. Events are raised on the thread that opened the video (the UI thread).
/// </summary>
public sealed class VideoPlayback : IDisposable
{
    private readonly object _gate = new();
    private MediaPlayer? _player;
    private MediaComposition? _composition;
    private SynchronizationContext? _context;
    private CancellationTokenSource _loop = new();
    private Stopwatch? _fallbackClock;
    private TimeSpan _fallbackStart;
    private TimeSpan _pendingSeek = TimeSpan.MinValue;
    private bool _fetching;
    private bool _disposed;
    private const int StillMaxWidth = 3840;
    private int _maxWidth = 1920;
    private int _slowFrames;
    private int _playWidth;   // current decode width while playing: starts at the cap, drops when the machine cannot keep up

    public event Action<System.Drawing.Bitmap, TimeSpan>? FrameReady;
    public event Action? Ended;

    public TimeSpan Duration { get; private set; }
    public System.Drawing.Size Size { get; private set; }
    public bool IsPlaying { get; private set; }

    /// <summary>
    /// Widest picture decoded WHILE PLAYING. Still frames (paused, scrubbing, the end) are always decoded at the video's own resolution:
    /// the system's frame scaler aliases, so the picture is decoded once at full size and the view shrinks it with a proper filter.
    /// A machine that cannot decode full-size frames in time lowers the playing width by itself.
    /// </summary>
    public int MaxWidth
    {
        get => _maxWidth;
        set { _maxWidth = Math.Clamp(value, 160, StillMaxWidth); _playWidth = 0; }
    }

    public bool Muted
    {
        get => _player?.IsMuted ?? true;
        set { if (_player is not null) _player.IsMuted = value; }
    }

    public TimeSpan Position
    {
        get
        {
            if (_fallbackClock is not null) return Clamp(_fallbackStart + _fallbackClock.Elapsed);
            return _player is null ? TimeSpan.Zero : Clamp(_player.PlaybackSession.Position);
        }
    }

    public async Task OpenAsync(string path)
    {
        _context = SynchronizationContext.Current;
        var file = await StorageFile.GetFileFromPathAsync(path);
        var clip = await MediaClip.CreateFromFileAsync(file);
        var properties = await file.Properties.GetVideoPropertiesAsync();
        Duration = clip.OriginalDuration;
        Size = new System.Drawing.Size((int)properties.Width, (int)properties.Height);

        _composition = new MediaComposition();
        _composition.Clips.Add(clip);

        try
        {
            // Audio + clock only: the picture is drawn by us.
            _player = new MediaPlayer { AutoPlay = false, Source = MediaSource.CreateFromStorageFile(file) };
            _player.MediaEnded += (_, _) => Raise(() => { if (IsPlaying) EndPlayback(); });
            _player.MediaFailed += (_, _) => UseFallbackClock();
        }
        catch
        {
            UseFallbackClock(); // no audio device / codec: keep the picture working
        }
    }

    public void Play()
    {
        if (_composition is null || IsPlaying) return;
        if (Position >= Duration - TimeSpan.FromMilliseconds(50)) SetClock(TimeSpan.Zero);

        IsPlaying = true;
        if (_fallbackClock is not null) _fallbackClock.Start();
        else _player?.Play();

        _loop = new CancellationTokenSource();
        _ = PlayLoopAsync(_loop.Token);
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        _loop.Cancel();
        if (_fallbackClock is not null) _fallbackClock.Stop();
        else _player?.Pause();

        // The frame left on screen is a full-resolution one, however fast the playing frames had to be.
        RequestStill(Position);
    }

    /// <summary>Moves the clock and (when paused) shows the frame there. Rapid calls are coalesced: only the latest position is decoded.</summary>
    public void Seek(TimeSpan time)
    {
        time = Clamp(time);
        SetClock(time);
        if (IsPlaying) return; // the play loop will pick it up
        RequestStill(time);
    }

    private void RequestStill(TimeSpan time)
    {
        lock (_gate)
        {
            _pendingSeek = time;
            if (_fetching) return;
            _fetching = true;
        }
        _ = FetchPendingAsync();
    }

    // ------------------------------------------------------------------ internals

    private TimeSpan Clamp(TimeSpan t) => TimeSpan.FromTicks(Math.Clamp(t.Ticks, 0, Math.Max(0, Duration.Ticks)));

    private void SetClock(TimeSpan time)
    {
        if (_fallbackClock is not null)
        {
            _fallbackStart = time;
            _fallbackClock.Reset();
            if (IsPlaying) _fallbackClock.Start();
        }
        else if (_player is not null)
        {
            _player.PlaybackSession.Position = time;
        }
    }

    private void UseFallbackClock()
    {
        if (_fallbackClock is not null) return;
        var position = _player?.PlaybackSession.Position ?? TimeSpan.Zero;
        _fallbackStart = position;
        _fallbackClock = new Stopwatch();
        if (IsPlaying) _fallbackClock.Start();
    }

    private async Task FetchPendingAsync()
    {
        while (!_disposed)
        {
            TimeSpan time;
            lock (_gate)
            {
                if (_pendingSeek == TimeSpan.MinValue) { _fetching = false; return; }
                time = _pendingSeek;
                _pendingSeek = TimeSpan.MinValue;
            }
            try
            {
                var frame = await GrabAsync(time, StillMaxWidth);
                if (_disposed) { frame.Dispose(); return; }
                Raise(() => FrameReady?.Invoke(frame, time));
            }
            catch
            {
                // a frame that can't be decoded is simply not shown
            }
        }
    }

    private async Task PlayLoopAsync(CancellationToken cancel)
    {
        var last = TimeSpan.MinValue;
        while (!cancel.IsCancellationRequested && !_disposed)
        {
            var position = Position;
            if (position >= Duration - TimeSpan.FromMilliseconds(30))
            {
                Raise(EndPlayback);
                return;
            }

            if (position == last) { await Task.Delay(5, CancellationToken.None); continue; }
            last = position;
            try
            {
                var width = _playWidth > 0 ? _playWidth : _maxWidth;
                var started = Stopwatch.GetTimestamp();
                var frame = await GrabAsync(position, width);
                if (cancel.IsCancellationRequested || _disposed) { frame.Dispose(); return; }
                Raise(() => FrameReady?.Invoke(frame, position));

                // Too slow for ~15 fps at this size? Play smaller (softer) frames; the paused picture stays full size.
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds > 70) _slowFrames++; else _slowFrames = 0;
                if (_slowFrames >= 4 && width > 640)
                {
                    _playWidth = Math.Max(640, Math.Min(width, Size.Width) * 2 / 3 & ~1);
                    _slowFrames = 0;
                }
            }
            catch
            {
                await Task.Delay(20, CancellationToken.None);
            }
        }
    }

    private void EndPlayback()
    {
        if (!IsPlaying) return;
        Pause();
        Ended?.Invoke();
    }

    private async Task<System.Drawing.Bitmap> GrabAsync(TimeSpan at, int maxWidth)
    {
        var composition = _composition ?? throw new ObjectDisposedException(nameof(VideoPlayback));
        var width = Math.Min(Math.Max(Size.Width, 2), maxWidth) & ~1;
        var height = Math.Max(2, (int)Math.Round(width * (double)Size.Height / Math.Max(1, Size.Width)));
        at = TimeSpan.FromTicks(Math.Clamp(at.Ticks, 0, Math.Max(0, Duration.Ticks - TimeSpan.TicksPerMillisecond)));

        using var stream = await composition.GetThumbnailAsync(at, width, height, VideoFramePrecision.NearestFrame);
        using var managed = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream);
        using var loaded = new System.Drawing.Bitmap(managed);
        return new System.Drawing.Bitmap(loaded);
    }

    private void Raise(Action action)
    {
        if (_context is { } context && SynchronizationContext.Current != context) context.Post(_ => { if (!_disposed) action(); }, null);
        else action();
    }

    public void Dispose()
    {
        _disposed = true;
        IsPlaying = false;
        _loop.Cancel();
        _player?.Dispose();
        _player = null;
        _composition = null;
    }
}
