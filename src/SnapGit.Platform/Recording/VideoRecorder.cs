using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using SnapGit.Core.Diagnostics;
using SnapGit.Core.Models;
using SnapGit.Core.Recording;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace SnapGit.Platform.Recording;

/// <summary>
/// Records the screen (and optionally system audio / microphone) into an H.264/AAC MP4 using Windows' own
/// Media Foundation encoder: raw frames are fed to a MediaStreamSource that a MediaTranscoder encodes in real time.
/// </summary>
public sealed class VideoRecorder : IRecorder
{
    private sealed record Frame(byte[] Data, TimeSpan At);

    private static readonly TimeSpan AudioChunk = TimeSpan.FromMilliseconds(20);

    private readonly IScreenGrabber _grabber;
    private readonly BlockingCollection<Frame> _frames = new(boundedCapacity: 12);
    private readonly Stopwatch _clock = new();

    private RecordingOptions? _options;
    private string? _outputPath;
    private Thread? _captureThread;
    private Task? _pipeline;
    private AudioMixer? _audio;
    private volatile bool _stop;
    private volatile bool _discard;
    private volatile bool _finished;
    private TimeSpan _endTime = TimeSpan.MaxValue;
    private TimeSpan _finalElapsed;
    private long _audioIndex;
    private int _width;
    private int _height;
    private Size _captureSize;
    private int _framesCaptured;
    private int _framesDropped;
    private Exception? _failure;

    public VideoRecorder(IScreenGrabber grabber) => _grabber = grabber;

    /// <summary>Use the GPU encoder when there is one (experiments and tests can switch it off to compare).</summary>
    public bool HardwareEncoding { get; set; } = true;

    /// <summary>The slower, higher-quality BGRA → YUV conversion of Media Foundation (see the transcoder set-up).</summary>
    public bool HighQualityColorConversion { get; set; } = true;

    /// <summary>Overrides the bitrate derived from <see cref="RecordingOptions.Quality"/> (bits per second).</summary>
    public uint? BitrateOverride { get; set; }

    public bool IsRecording => _captureThread is { IsAlive: true } && !_stop;

    public TimeSpan Elapsed => _clock.IsRunning ? _clock.Elapsed : _finalElapsed;

    /// <summary>Frames handed to the encoder so far.</summary>
    public int FramesCaptured => _framesCaptured;

    /// <summary>Frames that were grabbed but dropped because the encoder fell behind (should stay 0).</summary>
    public int FramesDropped => _framesDropped;

    /// <summary>Size of the video that is being written (after Full HD / HD scaling).</summary>
    public Size OutputSize => new(_width, _height);

    public event EventHandler<string?>? StoppedByItself;

    public void Start(RecordingOptions options, string outputPath)
    {
        if (_captureThread is not null) throw new InvalidOperationException("This recorder was already started.");

        // H.264 needs even dimensions. The recorded area keeps its own pixels; the file may be a different size (Full HD, HD).
        _captureSize = new Size(options.Area.Width & ~1, options.Area.Height & ~1);
        if (_captureSize.Width < 16 || _captureSize.Height < 16) throw new ArgumentException("Recording area is too small.", nameof(options));
        var output = VideoEncoding.OutputSize(_captureSize, options.Resolution);
        _width = output.Width;
        _height = output.Height;

        _options = options;
        _outputPath = outputPath;

        if (options.CaptureSystemAudio || options.CaptureMicrophone)
        {
            try
            {
                _audio = new AudioMixer(options.CaptureSystemAudio, options.CaptureMicrophone);
                if (!_audio.HasInputs) { _audio.Dispose(); _audio = null; }
            }
            catch (Exception ex)
            {
                Log.Warn($"Audio capture unavailable, recording video only: {ex.Message}");
                _audio = null;
            }
        }

        _pipeline = Task.Run(RunPipelineAsync);

        _audio?.Start();
        _clock.Start();
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "SnapGit video capture" };
        _captureThread.Start();
    }

    public async Task<string> StopAsync()
    {
        if (_captureThread is null || _pipeline is null || _outputPath is null)
            throw new InvalidOperationException("Recording was not started.");

        _endTime = _clock.Elapsed;
        _stop = true;
        _captureThread.Join();
        _finalElapsed = _clock.Elapsed;
        _frames.CompleteAdding();

        try
        {
            // The transcoder drains the remaining frames and writes the MP4 trailer.
            var finished = await Task.WhenAny(_pipeline, Task.Delay(TimeSpan.FromSeconds(60)));
            if (finished != _pipeline) throw new TimeoutException("Finishing the video took too long.");
            await _pipeline;
        }
        finally
        {
            _clock.Stop();
            _audio?.Dispose();
        }

        if (_failure is not null) throw new InvalidOperationException($"Recording failed: {_failure.Message}", _failure);
        if (_framesCaptured == 0) throw new InvalidOperationException("Nothing was recorded.");
        _finished = true; // from now on Dispose/Cancel must leave the finished file alone
        return _outputPath;
    }

    public void Cancel()
    {
        if (_finished) return; // already saved: disposing the recorder must never delete the result

        _discard = true;
        _endTime = _clock.IsRunning ? _clock.Elapsed : TimeSpan.Zero;
        _stop = true;
        _captureThread?.Join();
        if (!_frames.IsAddingCompleted) _frames.CompleteAdding();
        _clock.Stop();
        try { _pipeline?.Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { /* discarded anyway */ }
        _audio?.Dispose();

        if (_outputPath is not null && File.Exists(_outputPath))
        {
            try { File.Delete(_outputPath); } catch (IOException) { /* still locked by the encoder: leave it */ }
        }
    }

    public void Dispose() => Cancel();

    // ------------------------------------------------------------------ capture

    // A 1080p frame is 8 MB: allocating one per frame at 30 fps (large-object heap, gen-2 collections) slows the capture loop,
    // so buffers the encoder has finished with are handed back and reused.
    private readonly ConcurrentBag<byte[]> _pool = new();

    private byte[] RentBuffer(int length) => _pool.TryTake(out var buffer) && buffer.Length == length ? buffer : new byte[length];

    private void ReturnBuffer(byte[] buffer)
    {
        if (_pool.Count < 16) _pool.Add(buffer);
    }

    private void CaptureLoop()
    {
        var options = _options!;
        var fps = Math.Clamp(options.FramesPerSecond, 5, 60);
        var interval = TimeSpan.FromSeconds(1.0 / fps);
        var area = new Rectangle(options.Area.X, options.Area.Y, _captureSize.Width, _captureSize.Height);
        var next = TimeSpan.Zero;

        try
        {
            while (!_stop)
            {
                var wait = next - _clock.Elapsed;
                if (wait > TimeSpan.FromMilliseconds(1)) { Thread.Sleep(wait); continue; }

                var at = _clock.Elapsed;
                using (var bitmap = _grabber.Grab(area, options.IncludeCursor))
                {
                    var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        // Media Foundation reads uncompressed RGB32 as BOTTOM-UP (a positive stride is not assumed), GDI+ hands us
                        // top-down rows: write them in reverse order or every video comes out upside down.
                        var stride = Math.Abs(data.Stride);
                        var bytes = RentBuffer(stride * data.Height);
                        for (var row = 0; row < data.Height; row++)
                            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + row * data.Stride, bytes, (data.Height - 1 - row) * stride, stride);
                        // Block briefly if the encoder is behind; drop the frame rather than stall the clock for long.
                        if (_frames.TryAdd(new Frame(bytes, at), 250)) _framesCaptured++;
                        else _framesDropped++;
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }
                }

                next += interval;
                if (next < _clock.Elapsed) next = _clock.Elapsed;

                if (options.MaxSeconds > 0 && _clock.Elapsed.TotalSeconds >= options.MaxSeconds)
                {
                    _endTime = _clock.Elapsed;
                    _stop = true;
                    if (!_discard) StoppedByItself?.Invoke(this, null);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Video capture loop failed", ex);
            _failure = ex;
            _stop = true;
            _endTime = _clock.Elapsed;
            if (!_frames.IsAddingCompleted) _frames.CompleteAdding();
            StoppedByItself?.Invoke(this, ex.Message);
        }
    }

    // ------------------------------------------------------------------ encoding

    private async Task RunPipelineAsync()
    {
        var options = _options!;
        var fps = Math.Clamp(options.FramesPerSecond, 5, 60);
        var path = _outputPath!;

        try
        {
            // Frames go in at the size of the recorded area; the profile below has the OUTPUT size (Full HD…) and the transcoder
            // scales in between (video processor / GPU) — far cheaper than scaling every frame on the CPU here.
            var videoProperties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_captureSize.Width, (uint)_captureSize.Height);
            videoProperties.FrameRate.Numerator = (uint)fps;
            videoProperties.FrameRate.Denominator = 1;
            var videoDescriptor = new VideoStreamDescriptor(videoProperties);

            MediaStreamSource source;
            if (_audio is not null)
            {
                var audioDescriptor = new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(AudioMixer.SampleRate, AudioMixer.Channels, 16));
                source = new MediaStreamSource(videoDescriptor, audioDescriptor);
            }
            else
            {
                source = new MediaStreamSource(videoDescriptor);
            }
            source.BufferTime = TimeSpan.Zero;
            source.SampleRequested += OnSampleRequested;

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Video.Width = (uint)_width;
            profile.Video.Height = (uint)_height;
            profile.Video.FrameRate.Numerator = (uint)fps;
            profile.Video.FrameRate.Denominator = 1;
            profile.Video.ProfileId = H264ProfileIds.High;   // better compression of sharp text than Main/Baseline
            profile.Video.Bitrate = BitrateOverride ?? VideoEncoding.Bitrate(new Size(_width, _height), fps, options.Quality);
            if (_audio is null) profile.Audio = null;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using (File.Create(path)) { } // the transcoder needs an existing file
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var output = await file.OpenAsync(FileAccessMode.ReadWrite);

            // MrfCrf444 = the high-quality BGRA → YUV conversion: keeps coloured text edges from smearing (the default is the fast one).
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = HardwareEncoding, VideoProcessingAlgorithm = HighQualityColorConversion ? MediaVideoProcessingAlgorithm.MrfCrf444 : MediaVideoProcessingAlgorithm.Default };
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, output, profile);
            if (!prepared.CanTranscode) throw new InvalidOperationException($"Cannot encode video: {prepared.FailureReason}.");
            await prepared.TranscodeAsync();
        }
        catch (Exception ex)
        {
            _failure = ex;
            Log.Error("Video encoding failed", ex);
            _stop = true;
            if (!_frames.IsAddingCompleted) _frames.CompleteAdding();
            StoppedByItself?.Invoke(this, ex.Message);
        }
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        if (args.Request.StreamDescriptor is VideoStreamDescriptor) ProvideVideo(args.Request);
        else ProvideAudio(args.Request);
    }

    private void ProvideVideo(MediaStreamSourceSampleRequest request)
    {
        var fps = Math.Clamp(_options!.FramesPerSecond, 5, 60);

        // Real-time pull: wait for the next captured frame, or end of stream once capture completed and drained.
        while (true)
        {
            if (_frames.TryTake(out var frame, 50))
            {
                var sample = MediaStreamSample.CreateFromBuffer(frame.Data.AsBuffer(), frame.At);
                sample.Duration = TimeSpan.FromSeconds(1.0 / fps);
                var data = frame.Data;
                sample.Processed += (_, _) => ReturnBuffer(data); // the encoder is done with it: the capture loop can reuse it
                request.Sample = sample;
                return;
            }
            if (_frames.IsCompleted) { request.Sample = null; return; } // end of stream
        }
    }

    private void ProvideAudio(MediaStreamSourceSampleRequest request)
    {
        var framesPerChunk = (int)(AudioMixer.SampleRate * AudioChunk.TotalSeconds);
        var timestamp = TimeSpan.FromSeconds((double)_audioIndex * framesPerChunk / AudioMixer.SampleRate);

        // Pace to the wall clock: audio arrives in real time, so a chunk may only be produced once it has elapsed.
        while (_clock.IsRunning && _clock.Elapsed < timestamp + AudioChunk && timestamp + AudioChunk <= _endTime)
            Thread.Sleep(2);

        if (_stop && timestamp >= _endTime) { request.Sample = null; return; }

        var pcm = _audio!.Read(framesPerChunk);
        _audioIndex++;
        var sample = MediaStreamSample.CreateFromBuffer(pcm.AsBuffer(), timestamp);
        sample.Duration = AudioChunk;
        request.Sample = sample;
    }
}
