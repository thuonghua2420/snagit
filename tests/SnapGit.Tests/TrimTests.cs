using SnapGit.Core.Imaging;
using SnapGit.Core.Recording;
using SnapGit.Platform.Capture;
using SnapGit.Platform.Recording;
using Windows.Storage;
using Xunit;

namespace SnapGit.Tests;

[Collection("Heavy")]
public class TrimTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapgit-trim-" + Guid.NewGuid().ToString("N"));

    public TrimTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>A GIF whose frame i has a red square at x = 10 + 12*i, and whose delays are 100 + 20*i ms.</summary>
    private string MakeGif(int frames)
    {
        var list = new List<GifFrame>();
        for (var i = 0; i < frames; i++)
        {
            var bmp = new Bitmap(120, 40);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.FillRectangle(Brushes.Red, 10 + 12 * i, 10, 10, 10);
            }
            list.Add(new GifFrame(bmp, 100 + 20 * i));
        }
        var path = Path.Combine(_dir, "source.gif");
        GifEncoder.Write(path, list);
        foreach (var f in list) f.Image.Dispose();
        return path;
    }

    [Fact]
    public void ReadFramesReturnsEveryFrameCompleteWithItsDelay()
    {
        var path = MakeGif(6);
        var frames = GifTrimmer.ReadFrames(path);
        try
        {
            Assert.Equal(6, frames.Count);
            Assert.Equal([100, 120, 140, 160, 180, 200], frames.Select(f => f.DelayMs));
            for (var i = 0; i < 6; i++)
            {
                // the square is where this frame says it is, and NOT where earlier frames had it (frames are fully composited)
                Assert.Equal(Color.Red.ToArgb(), frames[i].Image.GetPixel(15 + 12 * i, 15).ToArgb());
                if (i > 0) Assert.Equal(Color.White.ToArgb(), frames[i].Image.GetPixel(15 + 12 * (i - 1), 15).ToArgb());
            }
        }
        finally
        {
            foreach (var f in frames) f.Image.Dispose();
        }
    }

    [Fact]
    public void TrimKeepsTheRequestedRangeAndTheirDelays()
    {
        var source = MakeGif(8);
        var target = Path.Combine(_dir, "trimmed.gif");

        var kept = GifTrimmer.Trim(source, target, first: 2, last: 5);
        Assert.Equal(4, kept);

        var frames = GifTrimmer.ReadFrames(target);
        try
        {
            Assert.Equal(4, frames.Count);
            Assert.Equal([140, 160, 180, 200], frames.Select(f => f.DelayMs));
            Assert.Equal(Color.Red.ToArgb(), frames[0].Image.GetPixel(15 + 12 * 2, 15).ToArgb());   // first kept = original frame 2
            Assert.Equal(Color.Red.ToArgb(), frames[3].Image.GetPixel(15 + 12 * 5, 15).ToArgb());   // last kept = original frame 5
            Assert.Equal(680, GifTrimmer.TotalMilliseconds(frames));
        }
        finally
        {
            foreach (var f in frames) f.Image.Dispose();
        }
    }

    [Fact]
    public void TrimClampsOutOfRangeIndexesAndKeepsTheSourceIntact()
    {
        var source = MakeGif(5);
        var target = Path.Combine(_dir, "all.gif");
        Assert.Equal(5, GifTrimmer.Trim(source, target, first: -3, last: 99));
        Assert.Equal(1, GifTrimmer.Trim(source, Path.Combine(_dir, "one.gif"), first: 4, last: 1)); // reversed: collapses to one frame

        var original = GifTrimmer.ReadFrames(source);
        Assert.Equal(5, original.Count);
        foreach (var f in original) f.Image.Dispose();
    }

    [Fact]
    public async Task VideoTrimProducesAShorterPlayableMp4()
    {
        var grabber = new ScreenGrabber();
        var source = Path.Combine(_dir, "source.mp4");
        var area = new Rectangle(grabber.VirtualScreen.X, grabber.VirtualScreen.Y, 240, 160);

        var recorder = new VideoRecorder(grabber);
        try { recorder.Start(new RecordingOptions(area, FramesPerSecond: 15), source); }
        catch (System.ComponentModel.Win32Exception) { return; } // no desktop session
        await Task.Delay(3200);
        await recorder.StopAsync();
        recorder.Dispose();

        var full = await VideoTrimmer.GetDurationAsync(source);
        Assert.InRange(full.TotalSeconds, 2.2, 5.0);

        var target = Path.Combine(_dir, "trimmed.mp4");
        await VideoTrimmer.TrimAsync(source, target, TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(2.2));

        var trimmed = await VideoTrimmer.GetDurationAsync(target);
        Assert.InRange(trimmed.TotalSeconds, 1.0, 1.9);   // asked for 1.4 s
        var file = await StorageFile.GetFileFromPathAsync(target);
        var video = await file.Properties.GetVideoPropertiesAsync();
        Assert.Equal(240u, video.Width);
        Assert.Equal(160u, video.Height);
    }

    [Fact]
    public async Task VideoTrimRejectsAnEmptyRange()
    {
        var grabber = new ScreenGrabber();
        var source = Path.Combine(_dir, "tiny.mp4");
        var recorder = new VideoRecorder(grabber);
        try { recorder.Start(new RecordingOptions(new Rectangle(grabber.VirtualScreen.X, grabber.VirtualScreen.Y, 160, 120), FramesPerSecond: 10), source); }
        catch (System.ComponentModel.Win32Exception) { return; }
        await Task.Delay(1500);
        await recorder.StopAsync();
        recorder.Dispose();

        await Assert.ThrowsAsync<ArgumentException>(() => VideoTrimmer.TrimAsync(source, Path.Combine(_dir, "x.mp4"), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
    }
}
