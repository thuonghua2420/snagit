namespace SnapGit.Core.Models;

/// <summary>A finished capture. Owns <see cref="Image"/> and disposes it.</summary>
public sealed class CaptureResult : IDisposable
{
    public CaptureResult(Bitmap image, CaptureMode mode, Rectangle screenBounds, string? sourceTitle = null)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        Mode = mode;
        ScreenBounds = screenBounds;
        SourceTitle = sourceTitle;
        CapturedAt = DateTime.Now;
    }

    public Bitmap Image { get; }
    public CaptureMode Mode { get; }

    /// <summary>Bounds of the captured area in virtual-screen pixel coordinates.</summary>
    public Rectangle ScreenBounds { get; }

    /// <summary>Window title when a window was captured, otherwise null.</summary>
    public string? SourceTitle { get; }

    public DateTime CapturedAt { get; }

    public void Dispose() => Image.Dispose();
}
