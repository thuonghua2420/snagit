using SnapGit.Core.Models;
using SnapGit.Core.Recording;
using SnapGit.Platform.Recording;
using Windows.Storage;
using Xunit;
using Xunit.Abstractions;

namespace SnapGit.Tests;

public class VideoEncodingTests
{
    [Theory]
    [InlineData(1920, 1080, VideoResolution.Original, 1920, 1080)]
    [InlineData(1921, 1081, VideoResolution.Original, 1920, 1080)]   // always even
    [InlineData(3840, 2160, VideoResolution.FullHd, 1920, 1080)]     // 4K area → Full HD
    [InlineData(2560, 1440, VideoResolution.FullHd, 1920, 1080)]
    [InlineData(1280, 720, VideoResolution.FullHd, 1920, 1080)]      // 720p area → Full HD
    [InlineData(800, 600, VideoResolution.FullHd, 1440, 1080)]       // aspect ratio kept
    [InlineData(1000, 1000, VideoResolution.FullHd, 1080, 1080)]
    [InlineData(400, 300, VideoResolution.FullHd, 800, 600)]         // upscaling is capped at 2×
    [InlineData(200, 100, VideoResolution.FullHd, 400, 200)]
    [InlineData(1920, 1080, VideoResolution.Hd, 1280, 720)]
    [InlineData(640, 480, VideoResolution.Hd, 960, 720)]
    public void OutputSizeFitsTheChosenResolutionAndKeepsTheAspectRatio(int w, int h, VideoResolution resolution, int expectedW, int expectedH)
    {
        var size = VideoEncoding.OutputSize(new Size(w, h), resolution);
        Assert.Equal(new Size(expectedW, expectedH), size);
        Assert.True(size.Width % 2 == 0 && size.Height % 2 == 0);
    }

    [Fact]
    public void BitrateGrowsWithQualityAndStaysInSaneBounds()
    {
        var fullHd = new Size(1920, 1080);
        var standard = VideoEncoding.Bitrate(fullHd, 30, VideoQuality.Standard);
        var high = VideoEncoding.Bitrate(fullHd, 30, VideoQuality.High);
        var best = VideoEncoding.Bitrate(fullHd, 30, VideoQuality.Best);

        Assert.True(standard < high && high < best);
        Assert.InRange(high, 9_000_000u, 14_000_000u);      // ~11 Mbit/s for 1080p30: what screen recorders use for sharp text
        Assert.True(VideoEncoding.Bitrate(fullHd, 60, VideoQuality.High) > high);
        Assert.Equal(2_000_000u, VideoEncoding.Bitrate(new Size(100, 100), 30, VideoQuality.Standard));          // floor
        Assert.Equal(50_000_000u, VideoEncoding.Bitrate(new Size(7680, 4320), 60, VideoQuality.Best));           // ceiling
    }

    [Fact]
    public void FullHdHighIsTheDefault()
    {
        var settings = new SnapGit.Core.Settings.AppSettings();
        Assert.Equal(VideoResolution.FullHd, settings.VideoResolution);
        Assert.Equal(VideoQuality.High, settings.VideoQuality);
        Assert.Equal(30, settings.VideoFramesPerSecond);
    }
}

/// <summary>Real encodes: sharpness at each quality, the Full HD output size, and that 1080p30 keeps up.</summary>
[Collection("Heavy")]
public class VideoQualityTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapgit-vq-" + Guid.NewGuid().ToString("N"));

    public VideoQualityTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// Thin black/white stripes and lines of text that keep MOVING (3 px per frame): a static picture compresses to nothing at any bitrate,
    /// motion is what exposes a starved encoder as smear.
    /// </summary>
    private sealed class DetailGrabber : IScreenGrabber
    {
        private readonly Bitmap[] _frames;
        private int _next;

        // Drawn once up front: what is measured is the recorder, not GDI+ text rendering (a real BitBlt costs about as little as a Clone).
        public DetailGrabber(Size size)
        {
            _frames = new Bitmap[8];
            for (var i = 0; i < _frames.Length; i++) _frames[i] = Render(size, i * 3);
        }

        public Rectangle VirtualScreen => new(0, 0, 7680, 4320);
        public Rectangle MonitorUnderCursor => VirtualScreen;

        public Bitmap Grab(Rectangle bounds, bool includeCursor) => new(_frames[_next++ % _frames.Length]);

        private static Bitmap Render(Size size, int shift)
        {
            var bmp = new Bitmap(size.Width, size.Height);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            using var black = new SolidBrush(Color.Black);
            for (var x = -4; x < size.Width / 2 + 4; x += 4) g.FillRectangle(black, x + shift % 4, 0, 2, size.Height / 2);   // 2 px stripes, top half, drifting
            using var font = new Font("Consolas", 11);
            for (var line = 0; line < 20; line++)
                g.DrawString("The quick brown fox jumps over the lazy dog 0123456789", font, black, size.Width / 2 + 10 - shift % 200, size.Height / 2 + line * 20);
            return bmp;
        }

        public IReadOnlyList<WindowInfo> GetWindows(IntPtr excludeHandle = default) => [];
        public IReadOnlyList<WindowInfo> GetWindowChain(WindowInfo window, Point screenPoint) => [];
        public Bitmap? GrabWindow(IntPtr handle) => null;
    }
    /// <summary>A detailed but still scene (gradient, noise patches, small text): the same picture on every frame.</summary>
    private sealed class SceneGrabber : IScreenGrabber
    {
        public Rectangle VirtualScreen => new(0, 0, 7680, 4320);
        public Rectangle MonitorUnderCursor => VirtualScreen;

        public Bitmap Grab(Rectangle bounds, bool includeCursor) => Scene(bounds.Size);
        public IReadOnlyList<WindowInfo> GetWindows(IntPtr excludeHandle = default) => [];
        public IReadOnlyList<WindowInfo> GetWindowChain(WindowInfo window, Point screenPoint) => [];
        public Bitmap? GrabWindow(IntPtr handle) => null;

        public static Bitmap Scene(Size size)
        {
            var bmp = new Bitmap(size.Width, size.Height);
            using var g = Graphics.FromImage(bmp);
            using (var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, size.Width, size.Height), Color.FromArgb(30, 60, 120), Color.FromArgb(230, 200, 120), 25f))
                g.FillRectangle(gradient, 0, 0, size.Width, size.Height);
            // interface-like panels and shapes
            using (var panel = new SolidBrush(Color.FromArgb(245, 245, 247)))
                g.FillRectangle(panel, size.Width / 2, 40, size.Width / 2 - 60, size.Height / 2);
            using (var accent = new SolidBrush(Color.FromArgb(0, 120, 215)))
                for (var i = 0; i < 12; i++) g.FillEllipse(accent, size.Width / 2 + 30 + i * 70, 80, 48, 48);
            using (var pen = new Pen(Color.FromArgb(60, 60, 60), 1))
                for (var i = 0; i < 30; i++) g.DrawLine(pen, size.Width / 2 + 20, 160 + i * 14, size.Width - 100, 160 + i * 14);

            // one photo-like block with fine texture (the hard part)
            var random = new Random(1234);
            for (var py = 0; py < 300; py += 2)
                for (var px = 0; px < 480; px += 2)
                {
                    var v = (int)(128 + 100 * Math.Sin(px / 17.0) * Math.Cos(py / 23.0)) + random.Next(-30, 30);
                    using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(v, 0, 255), Math.Clamp(v + 20, 0, 255), Math.Clamp(255 - v, 0, 255)));
                    g.FillRectangle(brush, 60 + px, size.Height - 360 + py, 2, 2);
                }
            using var font = new Font("Segoe UI", 10);
            using var white = new SolidBrush(Color.White);
            using var dark = new SolidBrush(Color.FromArgb(20, 20, 20));
            for (var line = 0; line < 40; line++)
                g.DrawString("Small interface text must stay readable after encoding 0123456789", font, line % 2 == 0 ? white : dark, 40, 30 + line * 22);
            return bmp;
        }
    }

    /// <summary>Luma PSNR in dB between two same-size pictures (higher = closer to the original; ~35 dB+ is visually clean).</summary>
    private static double Psnr(Bitmap a, Bitmap b)
    {
        Assert.Equal(a.Size, b.Size);
        byte[] Pixels(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[data.Stride * data.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { bmp.UnlockBits(data); }
        }

        var pa = Pixels(a);
        var pb = Pixels(b);
        double sum = 0;
        for (var i = 0; i < pa.Length; i += 4)
        {
            var ya = 0.114 * pa[i] + 0.587 * pa[i + 1] + 0.299 * pa[i + 2];
            var yb = 0.114 * pb[i] + 0.587 * pb[i + 1] + 0.299 * pb[i + 2];
            sum += (ya - yb) * (ya - yb);
        }
        var mse = sum / (pa.Length / 4);
        return mse < 1e-9 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
    }

    private async Task<(string Path, VideoRecorder Recorder)?> RecordAsync(string name, IScreenGrabber grabber, Size area, VideoResolution resolution, VideoQuality quality, int fps, int milliseconds)
    {
        var path = Path.Combine(_dir, name + ".mp4");
        var recorder = new VideoRecorder(grabber);
        try { recorder.Start(new RecordingOptions(new Rectangle(0, 0, area.Width, area.Height), fps) { Resolution = resolution, Quality = quality }, path); }
        catch (System.ComponentModel.Win32Exception) { recorder.Dispose(); return null; }
        await Task.Delay(milliseconds);
        await recorder.StopAsync();
        return (path, recorder);
    }

    [Fact]
    public async Task FullHdOutputIs1920By1080EvenForASmallerArea()
    {
        var area = new Size(1280, 720);
        if (await RecordAsync("fhd", new DetailGrabber(area), area, VideoResolution.FullHd, VideoQuality.High, 30, 2500) is not var (path, recorder)) return;
        using var _ = recorder;

        Assert.Equal(new Size(1920, 1080), recorder.OutputSize);
        var props = await (await StorageFile.GetFileFromPathAsync(path)).Properties.GetVideoPropertiesAsync();
        Assert.Equal(1920u, props.Width);
        Assert.Equal(1080u, props.Height);
    }

    [Fact]
    public async Task FullHdRecordingOfMovingContentKeepsUpWith30FramesPerSecond()
    {
        var area = new Size(1920, 1080);
        if (await RecordAsync("fps", new DetailGrabber(area), area, VideoResolution.FullHd, VideoQuality.High, 30, 4000) is not var (path, recorder)) return;
        using var _ = recorder;

        var seconds = recorder.Elapsed.TotalSeconds;
        var rate = recorder.FramesCaptured / seconds;
        _output.WriteLine($"1080p30: {recorder.FramesCaptured} frames in {seconds:0.0} s = {rate:0.0} fps, dropped {recorder.FramesDropped}, {new FileInfo(path).Length / 1024} KB");
        Assert.True(rate >= 24, $"capturing 1080p must keep up: {rate:0.0} fps");
        Assert.True(recorder.FramesDropped <= recorder.FramesCaptured / 10, $"too many dropped frames: {recorder.FramesDropped}");
    }

    [Fact]
    public async Task UpscalingASmallerAreaToFullHdKeepsTheFramesPerSecond()
    {
        // scaling costs CPU in the capture loop: 1280x720 → 1920x1080 must not slow the recording down
        var area = new Size(1280, 720);
        if (await RecordAsync("up", new DetailGrabber(area), area, VideoResolution.FullHd, VideoQuality.High, 30, 3500) is not var (_, recorder)) return;
        using var _ = recorder;
        var rate = recorder.FramesCaptured / recorder.Elapsed.TotalSeconds;
        _output.WriteLine($"720p→1080p: {rate:0.0} fps, dropped {recorder.FramesDropped}");
        Assert.True(rate >= 24, $"scaling to Full HD must keep up: {rate:0.0} fps");
    }

    [Fact]
    public async Task ScreenLikeContentStaysCloseToTheOriginalAndMoreBitsNeverLookWorse()
    {
        var area = new Size(1920, 1080);
        using var original = SceneGrabber.Scene(area);
        var psnr = new Dictionary<VideoQuality, double>();
        var sizes = new Dictionary<VideoQuality, long>();
        foreach (var quality in new[] { VideoQuality.Standard, VideoQuality.High, VideoQuality.Best })
        {
            if (await RecordAsync("psnr-" + quality, new SceneGrabber(), area, VideoResolution.Original, quality, 30, 3000) is not var (path, recorder)) return;
            using var _ = recorder;
            using var frame = await VideoEditor.GetFrameAsync(path, TimeSpan.FromSeconds(2.0), 1920);
            psnr[quality] = Psnr(original, frame);
            sizes[quality] = new FileInfo(path).Length;
            _output.WriteLine($"{quality}: PSNR {psnr[quality]:0.0} dB, {sizes[quality] / 1024} KB");
        }

        // ≥ 38 dB is visually clean for interface content; High must get there (it is the default).
        Assert.True(psnr[VideoQuality.High] >= 38, $"default quality is too lossy: {psnr[VideoQuality.High]:0.0} dB");
        Assert.True(psnr[VideoQuality.Best] >= psnr[VideoQuality.High] - 1.0, "Best must not look worse than High");
        Assert.True(psnr[VideoQuality.High] >= psnr[VideoQuality.Standard] - 1.0, "High must not look worse than Standard");
    }
}