using System.Drawing.Imaging;
using SnapGit.Core.Models;
using SnapGit.Core.Recording;
using Xunit;

namespace SnapGit.Tests;

public class RecorderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapgit-rec-" + Guid.NewGuid().ToString("N"));

    public RecorderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Returns a frame whose colour depends on the call number, so frames differ.</summary>
    private sealed class FakeGrabber : IScreenGrabber
    {
        private int _calls;
        public int Calls => _calls;
        public Rectangle VirtualScreen => new(0, 0, 1000, 800);
        public Rectangle MonitorUnderCursor => VirtualScreen;
        public IReadOnlyList<WindowInfo> GetWindows(IntPtr excludeHandle = default) => [];
        public IReadOnlyList<WindowInfo> GetWindowChain(WindowInfo window, Point screenPoint) => [window];
        public Bitmap? GrabWindow(IntPtr handle) => null;

        public Bitmap Grab(Rectangle bounds, bool includeCursor)
        {
            var n = Interlocked.Increment(ref _calls);
            var bmp = new Bitmap(bounds.Width, bounds.Height);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Crimson, (n * 7) % Math.Max(1, bounds.Width - 20), 10, 20, 20);
            return bmp;
        }
    }

    [Fact]
    public async Task GifRecorderProducesAnAnimationAtRoughlyTheRequestedRate()
    {
        var grabber = new FakeGrabber();
        using var recorder = new GifRecorder(grabber);
        var path = Path.Combine(_dir, "rec.gif");

        recorder.Start(new RecordingOptions(new Rectangle(0, 0, 200, 120), FramesPerSecond: 20), path);
        Assert.True(recorder.IsRecording);
        await Task.Delay(700);
        var saved = await recorder.StopAsync();

        Assert.Equal(path, saved);
        Assert.False(recorder.IsRecording);
        Assert.InRange(recorder.Elapsed.TotalMilliseconds, 600, 3000);

        using var gif = Image.FromFile(path);
        var frames = gif.GetFrameCount(new FrameDimension(gif.FrameDimensionsList[0]));
        Assert.InRange(frames, 6, 20);   // ~14 expected at 20 fps over 0.7 s
        Assert.Equal(new Size(200, 120), gif.Size);
    }

    [Fact]
    public async Task GifRecorderScalesDownWideFramesAndStopsAtTheLimit()
    {
        var grabber = new FakeGrabber();
        using var recorder = new GifRecorder(grabber);
        var stoppedByItself = new TaskCompletionSource<string?>();
        recorder.StoppedByItself += (_, reason) => stoppedByItself.TrySetResult(reason);

        var path = Path.Combine(_dir, "limit.gif");
        recorder.Start(new RecordingOptions(new Rectangle(0, 0, 800, 100), FramesPerSecond: 30, MaxSeconds: 1, MaxWidth: 400), path);

        Assert.Null(await stoppedByItself.Task.WaitAsync(TimeSpan.FromSeconds(5))); // limit reached: no error
        await recorder.StopAsync();

        using var gif = Image.FromFile(path);
        Assert.Equal(new Size(400, 50), gif.Size);
    }

    [Fact]
    public async Task CancelThrowsEverythingAwayAndStopRequiresStart()
    {
        using var recorder = new GifRecorder(new FakeGrabber());
        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.StopAsync());

        var path = Path.Combine(_dir, "never.gif");
        recorder.Start(new RecordingOptions(new Rectangle(0, 0, 50, 50)), path);
        await Task.Delay(150);
        recorder.Cancel();

        Assert.False(File.Exists(path));
        Assert.False(recorder.IsRecording);
        Assert.Throws<InvalidOperationException>(() => recorder.Start(new RecordingOptions(new Rectangle(0, 0, 50, 50)), path));
    }

    [Fact]
    public void EmptyAreaIsRejected()
    {
        using var recorder = new GifRecorder(new FakeGrabber());
        Assert.Throws<ArgumentException>(() => recorder.Start(new RecordingOptions(Rectangle.Empty), "x.gif"));
    }
}
