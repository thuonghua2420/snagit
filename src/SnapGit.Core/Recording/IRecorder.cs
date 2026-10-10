namespace SnapGit.Core.Recording;

/// <param name="Area">Screen rectangle (virtual-screen pixels) to record.</param>
/// <param name="MaxSeconds">Recording stops by itself after this long (0 = unlimited).</param>
/// <param name="MaxWidth">Frames wider than this are scaled down (GIF only; keeps memory and file size sane).</param>
public sealed record RecordingOptions(Rectangle Area, int FramesPerSecond = 10, bool IncludeCursor = true, int MaxSeconds = 0, int MaxWidth = 960)
{
    public bool CaptureSystemAudio { get; init; }
    public bool CaptureMicrophone { get; init; }

    /// <summary>Video only: size of the file relative to the recorded area.</summary>
    public VideoResolution Resolution { get; init; } = VideoResolution.Original;

    /// <summary>Video only: bitrate budget.</summary>
    public VideoQuality Quality { get; init; } = VideoQuality.High;
}

/// <summary>A screen recorder. One instance records once: Start → (Stop | Cancel).</summary>
public interface IRecorder : IDisposable
{
    bool IsRecording { get; }

    TimeSpan Elapsed { get; }

    /// <summary>Raised (on a worker thread) when recording stopped without <see cref="StopAsync"/> — limit reached or an error.</summary>
    event EventHandler<string?>? StoppedByItself;

    /// <param name="outputPath">Where the finished file will be written.</param>
    void Start(RecordingOptions options, string outputPath);

    /// <summary>Stops recording, finalises the file and returns its path.</summary>
    Task<string> StopAsync();

    /// <summary>Stops and throws everything away.</summary>
    void Cancel();
}
