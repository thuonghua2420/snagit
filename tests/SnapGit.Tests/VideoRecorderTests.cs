using SnapGit.Core.Recording;
using SnapGit.Platform.Capture;
using SnapGit.Platform.Recording;
using Windows.Storage;
using Xunit;

namespace SnapGit.Tests;

/// <summary>Real MP4 encode through Windows Media Foundation. Needs a desktop session and the H.264 encoder.</summary>
[Collection("Heavy")]
public class VideoRecorderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapgit-vid-" + Guid.NewGuid().ToString("N"));

    public VideoRecorderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task RecordsAPlayableMp4OfTheRequestedLength()
    {
        var grabber = new ScreenGrabber();
        using var recorder = new VideoRecorder(grabber);
        var path = Path.Combine(_dir, "clip.mp4");
        var area = new Rectangle(grabber.VirtualScreen.X, grabber.VirtualScreen.Y, 321, 201); // odd size on purpose

        try { recorder.Start(new RecordingOptions(area, FramesPerSecond: 15), path); }
        catch (System.ComponentModel.Win32Exception) { return; } // no desktop session

        await Task.Delay(2000);
        var saved = await recorder.StopAsync();

        Assert.Equal(path, saved);
        Assert.True(new FileInfo(path).Length > 2_000, "mp4 is suspiciously small");

        var file = await StorageFile.GetFileFromPathAsync(path);
        var video = await file.Properties.GetVideoPropertiesAsync();
        Assert.Equal(320u, video.Width);   // rounded down to even
        Assert.Equal(200u, video.Height);
        Assert.InRange(video.Duration.TotalSeconds, 1.2, 3.5);
    }

    [Fact]
    public async Task RecordedVideoIsNotUpsideDown()
    {
        // Regression: Media Foundation treats RGB32 as bottom-up, so frames from a top-down bitmap came out flipped.
        var path = Path.Combine(_dir, "orientation.mp4");
        using var recorder = new VideoRecorder(new TopRedBottomBlueGrabber());

        try { recorder.Start(new RecordingOptions(new Rectangle(0, 0, 320, 200), FramesPerSecond: 10), path); }
        catch (System.ComponentModel.Win32Exception) { return; }

        await Task.Delay(1500);
        await recorder.StopAsync();

        var file = await StorageFile.GetFileFromPathAsync(path);
        using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, 320);
        using var stream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(thumbnail);
        using var frame = new Bitmap(stream);

        var top = frame.GetPixel(frame.Width / 2, frame.Height / 4);
        var bottom = frame.GetPixel(frame.Width / 2, frame.Height * 3 / 4);
        Assert.True(top.R > top.B + 60, $"top of the video should be red but is {top}");
        Assert.True(bottom.B > bottom.R + 60, $"bottom of the video should be blue but is {bottom}");
    }

    [Theory]
    [InlineData(640, 360, 1280, 720)]     // small area: enlarged (at most 2×)
    [InlineData(2560, 1440, 1920, 1080)]  // 1440p area: scaled down to Full HD
    public async Task ScaledToFullHdTheVideoHasTheRightSizeAndIsStillUpright(int w, int h, int outputW, int outputH)
    {
        var path = Path.Combine(_dir, $"scaled-{w}.mp4");
        using var recorder = new VideoRecorder(new TopRedBottomBlueGrabber());

        try { recorder.Start(new RecordingOptions(new Rectangle(0, 0, w, h), FramesPerSecond: 10) { Resolution = SnapGit.Core.Recording.VideoResolution.FullHd }, path); }
        catch (System.ComponentModel.Win32Exception) { return; }

        await Task.Delay(1500);
        await recorder.StopAsync();

        var file = await StorageFile.GetFileFromPathAsync(path);
        var props = await file.Properties.GetVideoPropertiesAsync();
        Assert.Equal((uint)outputW, props.Width);
        Assert.Equal((uint)outputH, props.Height);

        using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, 480);
        using var stream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(thumbnail);
        using var frame = new Bitmap(stream);
        var top = frame.GetPixel(frame.Width / 2, frame.Height / 4);
        var bottom = frame.GetPixel(frame.Width / 2, frame.Height * 3 / 4);
        Assert.True(top.R > top.B + 60, $"top should be red but is {top}");
        Assert.True(bottom.B > bottom.R + 60, $"bottom should be blue but is {bottom}");
    }

    private sealed class TopRedBottomBlueGrabber : SnapGit.Core.Models.IScreenGrabber
    {
        public Rectangle VirtualScreen => new(0, 0, 1920, 1080);
        public Rectangle MonitorUnderCursor => VirtualScreen;

        public Bitmap Grab(Rectangle bounds, bool includeCursor)
        {
            var bmp = new Bitmap(bounds.Width, bounds.Height);
            using var g = Graphics.FromImage(bmp);
            g.FillRectangle(Brushes.Red, 0, 0, bounds.Width, bounds.Height / 2);
            g.FillRectangle(Brushes.Blue, 0, bounds.Height / 2, bounds.Width, bounds.Height - bounds.Height / 2);
            return bmp;
        }

        public IReadOnlyList<SnapGit.Core.Models.WindowInfo> GetWindows(IntPtr excludeHandle = default) => [];
        public IReadOnlyList<SnapGit.Core.Models.WindowInfo> GetWindowChain(SnapGit.Core.Models.WindowInfo window, Point screenPoint) => [];
        public Bitmap? GrabWindow(IntPtr handle) => null;
    }

    [Fact]
    public async Task DisposingAFinishedRecorderKeepsTheVideo()
    {
        // Regression: Dispose() used to call Cancel(), which deleted the freshly saved file.
        var grabber = new ScreenGrabber();
        var path = Path.Combine(_dir, "keep.mp4");
        var area = new Rectangle(grabber.VirtualScreen.X, grabber.VirtualScreen.Y, 200, 120);

        var recorder = new VideoRecorder(grabber);
        try { recorder.Start(new RecordingOptions(area, FramesPerSecond: 10), path); }
        catch (System.ComponentModel.Win32Exception) { return; }

        await Task.Delay(1200);
        await recorder.StopAsync();
        recorder.Dispose();

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 1_000);
    }

    [Fact]
    public async Task CancellingDeletesThePartialFile()
    {
        var grabber = new ScreenGrabber();
        var path = Path.Combine(_dir, "discard.mp4");
        var area = new Rectangle(grabber.VirtualScreen.X, grabber.VirtualScreen.Y, 200, 120);

        using var recorder = new VideoRecorder(grabber);
        try { recorder.Start(new RecordingOptions(area, FramesPerSecond: 10), path); }
        catch (System.ComponentModel.Win32Exception) { return; }

        await Task.Delay(600);
        recorder.Cancel();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task RecordsWithMixedAudioInputsWithoutFailing()
    {
        var grabber = new ScreenGrabber();
        using var recorder = new VideoRecorder(grabber);
        var path = Path.Combine(_dir, "audio.mp4");
        var area = new Rectangle(grabber.VirtualScreen.X, grabber.VirtualScreen.Y, 160, 120);

        try
        {
            recorder.Start(new RecordingOptions(area, FramesPerSecond: 10) { CaptureSystemAudio = true, CaptureMicrophone = true }, path);
        }
        catch (System.ComponentModel.Win32Exception) { return; }

        await Task.Delay(1500);
        await recorder.StopAsync();

        var file = await StorageFile.GetFileFromPathAsync(path);
        var video = await file.Properties.GetVideoPropertiesAsync();
        Assert.InRange(video.Duration.TotalSeconds, 0.8, 3.5);
    }

    [Fact]
    public void TooSmallAreaIsRejected()
    {
        using var recorder = new VideoRecorder(new ScreenGrabber());
        Assert.Throws<ArgumentException>(() => recorder.Start(new RecordingOptions(new Rectangle(0, 0, 8, 8)), Path.Combine(_dir, "x.mp4")));
    }
}
