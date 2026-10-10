using System.Diagnostics;
using SnapGit.Core.Imaging;
using SnapGit.Core.Models;

namespace SnapGit.Core.Recording;

/// <summary>
/// Records the screen into an animated GIF: frames are grabbed on a background thread at the requested rate
/// and encoded when recording stops. Depends only on <see cref="IScreenGrabber"/>, so it can be tested with a fake.
/// </summary>
public sealed class GifRecorder : IRecorder
{
    private readonly IScreenGrabber _grabber;
    private readonly List<(Bitmap Image, TimeSpan At)> _frames = new();
    private readonly Stopwatch _clock = new();
    private readonly object _gate = new();

    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _discard;
    private RecordingOptions? _options;
    private string? _outputPath;
    private TimeSpan _finalElapsed;
    private string? _failure;

    public GifRecorder(IScreenGrabber grabber) => _grabber = grabber;

    public bool IsRecording => _thread is { IsAlive: true } && !_stop;

    public TimeSpan Elapsed => _clock.IsRunning ? _clock.Elapsed : _finalElapsed;

    public event EventHandler<string?>? StoppedByItself;

    public void Start(RecordingOptions options, string outputPath)
    {
        if (_thread is not null) throw new InvalidOperationException("This recorder was already started.");
        if (options.Area.Width < 2 || options.Area.Height < 2) throw new ArgumentException("Recording area is empty.", nameof(options));

        _options = options;
        _outputPath = outputPath;
        _clock.Start();
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "SnapGit GIF capture" };
        _thread.Start();
    }

    public async Task<string> StopAsync()
    {
        if (_thread is null || _outputPath is null) throw new InvalidOperationException("Recording was not started.");

        _stop = true;
        _thread.Join();
        _finalElapsed = _clock.Elapsed;
        _clock.Stop();

        if (_failure is not null) throw new InvalidOperationException(_failure);

        List<(Bitmap Image, TimeSpan At)> frames;
        lock (_gate) frames = _frames.ToList();
        if (frames.Count == 0) throw new InvalidOperationException("Nothing was recorded.");

        var path = _outputPath;
        await Task.Run(() =>
        {
            var gifFrames = new List<GifFrame>(frames.Count);
            for (var i = 0; i < frames.Count; i++)
            {
                var next = i + 1 < frames.Count ? frames[i + 1].At : _finalElapsed;
                var delay = (int)Math.Clamp((next - frames[i].At).TotalMilliseconds, 20, 2000);
                gifFrames.Add(new GifFrame(frames[i].Image, delay));
            }
            GifEncoder.Write(path, gifFrames);
        });

        ReleaseFrames();
        return path;
    }

    public void Cancel()
    {
        _discard = true;
        _stop = true;
        _thread?.Join();
        _clock.Stop();
        ReleaseFrames();
    }

    public void Dispose() => Cancel();

    private void CaptureLoop()
    {
        var options = _options!;
        var interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(options.FramesPerSecond, 1, 60));
        var maxFrames = options.MaxSeconds > 0 ? options.MaxSeconds * Math.Clamp(options.FramesPerSecond, 1, 60) : int.MaxValue;
        var next = TimeSpan.Zero;

        try
        {
            while (!_stop)
            {
                var wait = next - _clock.Elapsed;
                if (wait > TimeSpan.FromMilliseconds(1))
                {
                    Thread.Sleep(wait);
                    continue;
                }

                var at = _clock.Elapsed;
                var frame = _grabber.Grab(options.Area, options.IncludeCursor);
                if (frame.Width > options.MaxWidth)
                {
                    var scaled = ImageOps.Scale(frame, options.MaxWidth, (int)Math.Round(frame.Height * (double)options.MaxWidth / frame.Width));
                    frame.Dispose();
                    frame = scaled;
                }

                int count;
                lock (_gate)
                {
                    _frames.Add((frame, at));
                    count = _frames.Count;
                }

                // Fixed-rate schedule; if grabbing is slower than the interval we skip ahead instead of piling up.
                next += interval;
                if (next < _clock.Elapsed) next = _clock.Elapsed;

                if (count >= maxFrames)
                {
                    _stop = true;
                    if (!_discard) StoppedByItself?.Invoke(this, null);
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error("GIF capture loop failed", ex);
            _failure = ex.Message;
            _stop = true;
            StoppedByItself?.Invoke(this, ex.Message);
        }
    }

    private void ReleaseFrames()
    {
        lock (_gate)
        {
            foreach (var (image, _) in _frames) image.Dispose();
            _frames.Clear();
        }
    }
}
