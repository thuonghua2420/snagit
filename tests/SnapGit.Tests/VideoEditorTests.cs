using SnapGit.Core.Models;
using SnapGit.Core.Recording;
using SnapGit.Platform.Recording;
using Windows.Storage;
using Xunit;

namespace SnapGit.Tests;

public class VideoEditPlanTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static VideoEditPlan Plan(double seconds = 10, int w = 320, int h = 200) => VideoEditPlan.For(S(seconds), new Size(w, h));

    [Fact]
    public void KeepOnlyAndRemoveWorkOnWhatIsAlreadyKept()
    {
        var plan = Plan().Remove(S(2), S(4)).Remove(S(6), S(7));
        Assert.Equal([new TimeRange(S(0), S(2)), new TimeRange(S(4), S(6)), new TimeRange(S(7), S(10))], plan.Keep);
        Assert.Equal(S(7), plan.KeptDuration);

        var narrowed = plan.KeepOnly(S(1), S(8));
        Assert.Equal([new TimeRange(S(1), S(2)), new TimeRange(S(4), S(6)), new TimeRange(S(7), S(8))], narrowed.Keep);

        // a cut spanning several pieces joins what is left
        Assert.Equal([new TimeRange(S(0), S(1)), new TimeRange(S(9), S(10))], plan.Remove(S(1), S(9)).Keep);
    }

    [Fact]
    public void SliversShorterThanTheMinimumAreDropped()
    {
        Assert.Empty(Plan().Remove(S(0.05), S(9.95)).Keep);
    }

    [Fact]
    public void TimeMappingSkipsRemovedParts()
    {
        var plan = Plan().Remove(S(2), S(4));
        Assert.Equal(S(1), plan.ToOutput(S(1)));
        Assert.Equal(S(2), plan.ToOutput(S(3)));   // inside the cut: where the cut joins
        Assert.Equal(S(2), plan.ToOutput(S(4)));
        Assert.Equal(S(8), plan.ToOutput(S(10)));
        Assert.True(plan.IsKept(S(1)));
        Assert.False(plan.IsKept(S(3)));
        Assert.Equal(S(4), plan.NextKeptStart(S(3)));
        Assert.Equal(S(5), plan.NextKeptStart(S(5)));
        Assert.Null(plan.NextKeptStart(S(10)));
    }

    [Fact]
    public void ACaptionSpanningACutIsSplitOnTheOutputTimeline()
    {
        var plan = Plan().Remove(S(4), S(6));
        var intervals = plan.OutputIntervals(new VideoCaption("hi", S(3), S(8))).ToList();
        Assert.Equal([(S(3), S(4)), (S(4), S(6))], intervals);
        Assert.Empty(plan.OutputIntervals(new VideoCaption("gone", S(4.5), S(5.5))));
    }

    [Fact]
    public void OutputSizeFollowsRotationAndCropAndStaysEven()
    {
        var plan = Plan(1, 321, 201);
        Assert.Equal(new Size(320, 200), plan.OutputSize);
        Assert.Equal(new Size(200, 320), plan.RotateClockwise().OutputSize);
        Assert.Equal(new Size(100, 60), (plan with { Crop = new Rectangle(11, 5, 101, 61) }).OutputSize);
        // a crop outside the picture falls back to the whole picture
        Assert.Equal(new Size(320, 200), (plan with { Crop = new Rectangle(1000, 1000, 50, 50) }).OutputSize);
    }

    [Fact]
    public void RotatingAndFlippingKeepsTheCropOnTheSameContent()
    {
        var plan = Plan() with { Crop = new Rectangle(10, 20, 100, 50) };

        var turned = plan.RotateClockwise();
        Assert.Equal(90, turned.Rotation);
        Assert.Equal(new Rectangle(130, 10, 50, 100), turned.Crop);          // view is now 200 × 320
        Assert.Equal(plan.Crop, turned.RotateCounterClockwise().Crop);          // and back
        Assert.Equal(plan.Crop, plan.RotateClockwise().RotateClockwise().RotateClockwise().RotateClockwise().Crop);
        Assert.Equal(0, plan.RotateClockwise().RotateClockwise().RotateClockwise().RotateClockwise().Rotation);

        var flipped = plan.FlipHorizontally();
        Assert.True(flipped.FlipX);
        Assert.Equal(new Rectangle(210, 20, 100, 50), flipped.Crop);
        Assert.Equal(plan.Crop, flipped.FlipHorizontally().Crop);
        Assert.False(flipped.FlipHorizontally().FlipX);

        Assert.Equal(new Rectangle(10, 130, 100, 50), plan.FlipVertically().Crop);
    }

    [Fact]
    public void RotatingAFlippedPictureSwapsTheFlipAxes()
    {
        // rot90(flipX(img)) == flipY(rot90(img))
        var turned = Plan().FlipHorizontally().RotateClockwise();
        Assert.False(turned.FlipX);
        Assert.True(turned.FlipY);
        Assert.Equal(90, turned.Rotation);
    }

    [Fact]
    public void AFreshPlanIsUnchangedAndAnyEditIsNot()
    {
        var plan = Plan(5);
        Assert.True(plan.IsUnchanged);
        Assert.False(plan.Remove(S(1), S(2)).IsUnchanged);
        Assert.False(plan.RotateClockwise().IsUnchanged);
        Assert.False(plan.FlipHorizontally().IsUnchanged);
        Assert.False((plan with { Mute = true }).IsUnchanged);
        Assert.False(plan.WithCaption(new VideoCaption("x", S(0), S(1))).IsUnchanged);
        Assert.False((plan with { Crop = new Rectangle(0, 0, 100, 100) }).IsUnchanged);
        Assert.True((plan with { Crop = new Rectangle(0, 0, 320, 200) }).IsUnchanged);
    }
}

/// <summary>The editor preview (<see cref="VideoFrameTransform"/>) must show exactly what the render produces: same cases as the render tests below.</summary>
public class VideoPreviewMatchesExportTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static Bitmap Quadrants(int w = 320, int h = 200)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.FillRectangle(Brushes.Red, 0, 0, w / 2, h / 2);
        g.FillRectangle(Brushes.Lime, w / 2, 0, w / 2, h / 2);
        g.FillRectangle(Brushes.Blue, 0, h / 2, w / 2, h / 2);
        g.FillRectangle(Brushes.Yellow, w / 2, h / 2, w / 2, h / 2);
        return bmp;
    }

    private static string[] Quarters(Bitmap f)
    {
        string Name(int x, int y)
        {
            var c = f.GetPixel(x, y);
            return c.R > 150 && c.G > 150 && c.B < 120 ? "yellow" : c.R > 150 && c.G < 110 && c.B < 110 ? "red"
                : c.G > 150 && c.R < 110 && c.B < 110 ? "green" : c.B > 150 && c.R < 110 && c.G < 110 ? "blue" : "?";
        }
        int w = f.Width, h = f.Height;
        return [Name(w / 4, h / 4), Name(w * 3 / 4, h / 4), Name(w / 4, h * 3 / 4), Name(w * 3 / 4, h * 3 / 4)];
    }

    private static VideoEditPlan Plan() => VideoEditPlan.For(S(3), new Size(320, 200));

    public static IEnumerable<object[]> Cases()
    {
        var plan = Plan();
        yield return [plan, new Size(320, 200), new[] { "red", "green", "blue", "yellow" }];
        yield return [plan with { Crop = new Rectangle(160, 0, 160, 200) }, new Size(160, 200), new[] { "green", "green", "yellow", "yellow" }];
        yield return [plan.RotateClockwise(), new Size(200, 320), new[] { "blue", "red", "yellow", "green" }];
        yield return [plan.RotateClockwise().RotateClockwise(), new Size(320, 200), new[] { "yellow", "blue", "green", "red" }];
        yield return [plan.RotateCounterClockwise(), new Size(200, 320), new[] { "green", "yellow", "red", "blue" }];
        yield return [plan.FlipHorizontally(), new Size(320, 200), new[] { "green", "red", "yellow", "blue" }];
        yield return [plan.FlipVertically(), new Size(320, 200), new[] { "blue", "yellow", "red", "green" }];
        yield return [plan.RotateClockwise().FlipHorizontally(), new Size(200, 320), new[] { "red", "blue", "green", "yellow" }];
        yield return [plan.FlipHorizontally().RotateClockwise(), new Size(200, 320), new[] { "yellow", "green", "blue", "red" }];
        yield return [plan.FlipHorizontally() with { Crop = new Rectangle(0, 0, 160, 200) }, new Size(160, 200), new[] { "green", "green", "yellow", "yellow" }];
        yield return [plan.RotateClockwise() with { Crop = new Rectangle(0, 0, 200, 160) }, new Size(200, 160), new[] { "blue", "red", "blue", "red" }];
        yield return [(plan with { Crop = new Rectangle(0, 0, 160, 200) }).RotateClockwise(), new Size(200, 160), new[] { "blue", "red", "blue", "red" }];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PreviewShowsWhatTheExportProduces(VideoEditPlan plan, Size expectedSize, string[] expected)
    {
        using var source = Quadrants();
        using var preview = VideoFrameTransform.Apply(source, plan, S(1));
        Assert.Equal(expectedSize, preview.Size);
        Assert.Equal(expected, Quarters(preview));
    }

    [Fact]
    public void PreviewScalesWithASmallerDecodedFrame()
    {
        using var small = Quadrants(160, 100); // the editor decodes at a reduced size
        using var preview = VideoFrameTransform.Apply(small, Plan().RotateClockwise() with { Crop = new Rectangle(0, 0, 200, 160) }, S(1));
        Assert.Equal(new Size(100, 80), preview.Size);
        Assert.Equal(["blue", "red", "blue", "red"], Quarters(preview));
    }

    [Fact]
    public void CaptionsAreDrawnOnlyWhileTheyAreOnAndNotInCutParts()
    {
        var plan = Plan().WithCaption(new VideoCaption("Hello", S(1), S(2), CaptionPosition.Center, 20f)).Remove(S(1.4), S(1.6));
        int White(Bitmap b) { var n = 0; for (var y = 0; y < b.Height; y++) for (var x = 0; x < b.Width; x++) { var c = b.GetPixel(x, y); if (c.R > 230 && c.G > 230 && c.B > 230) n++; } return n; }

        using var source = Quadrants();
        using (var before = VideoFrameTransform.Apply(source, plan, S(0.5))) Assert.Equal(0, White(before));
        using (var during = VideoFrameTransform.Apply(source, plan, S(1.2))) Assert.True(White(during) > 50);
        using (var inCut = VideoFrameTransform.Apply(source, plan, S(1.5))) Assert.Equal(0, White(inCut));
        using (var after = VideoFrameTransform.Apply(source, plan, S(2.5))) Assert.Equal(0, White(after));
        using (var without = VideoFrameTransform.Apply(source, plan, S(1.2), includeCaptions: false)) Assert.Equal(0, White(without));
    }

    [Fact]
    public void WhileCroppingTheWholeRotatedPictureIsShown()
    {
        using var source = Quadrants();
        var plan = Plan().RotateClockwise() with { Crop = new Rectangle(0, 0, 200, 160) };
        using var cropping = VideoFrameTransform.Apply(source, plan, S(1), includeCrop: false);
        Assert.Equal(new Size(200, 320), cropping.Size);
    }
}

/// <summary>A 3 s clip whose four quadrants are red / green / blue / yellow, recorded once and shared by the render tests.</summary>
public sealed class QuadrantClip : IAsyncLifetime
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "snapgit-edit-" + Guid.NewGuid().ToString("N"));
    public string Path_ => System.IO.Path.Combine(Dir, "quadrants.mp4");
    public bool Available { get; private set; }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Dir);
        using var recorder = new VideoRecorder(new Grabber());
        try { recorder.Start(new RecordingOptions(new Rectangle(0, 0, 320, 200), FramesPerSecond: 10), Path_); }
        catch (System.ComponentModel.Win32Exception) { return; } // no desktop session
        await Task.Delay(3000);
        await recorder.StopAsync();
        Available = true;
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(Dir, recursive: true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private sealed class Grabber : IScreenGrabber
    {
        public Rectangle VirtualScreen => new(0, 0, 1920, 1080);
        public Rectangle MonitorUnderCursor => VirtualScreen;

        public Bitmap Grab(Rectangle bounds, bool includeCursor)
        {
            var bmp = new Bitmap(bounds.Width, bounds.Height);
            using var g = Graphics.FromImage(bmp);
            int w = bounds.Width / 2, h = bounds.Height / 2;
            g.FillRectangle(Brushes.Red, 0, 0, w, h);
            g.FillRectangle(Brushes.Lime, w, 0, w, h);
            g.FillRectangle(Brushes.Blue, 0, h, w, h);
            g.FillRectangle(Brushes.Yellow, w, h, w, h);
            return bmp;
        }

        public IReadOnlyList<WindowInfo> GetWindows(IntPtr excludeHandle = default) => [];
        public IReadOnlyList<WindowInfo> GetWindowChain(WindowInfo window, Point screenPoint) => [];
        public Bitmap? GrabWindow(IntPtr handle) => null;
    }
}

/// <summary>Real renders through Windows Media Foundation (needs a desktop session and the H.264 encoder).</summary>
[Collection("Heavy")]
public class VideoEditorTests : IClassFixture<QuadrantClip>
{
    private readonly QuadrantClip _clip;

    public VideoEditorTests(QuadrantClip clip) => _clip = clip;

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private async Task<VideoEditPlan> NewPlanAsync()
    {
        var info = await VideoEditor.ProbeAsync(_clip.Path_);
        return VideoEditPlan.For(info.Duration, info.Size);
    }

    private static async Task<Bitmap> FirstFrameAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, 400);
        using var stream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(thumbnail);
        using var loaded = new Bitmap(stream);
        return new Bitmap(loaded);
    }

    /// <summary>Colour at the centre of each quarter: [top-left, top-right, bottom-left, bottom-right].</summary>
    private static string[] Quarters(Bitmap frame)
    {
        string Name(Point p)
        {
            var c = frame.GetPixel(p.X, p.Y);
            return c.R > 150 && c.G > 150 && c.B < 120 ? "yellow"
                : c.R > 150 && c.G < 110 && c.B < 110 ? "red"
                : c.G > 150 && c.R < 110 && c.B < 110 ? "green"
                : c.B > 150 && c.R < 110 && c.G < 110 ? "blue" : $"?{c}";
        }
        int w = frame.Width, h = frame.Height;
        return [Name(new Point(w / 4, h / 4)), Name(new Point(w * 3 / 4, h / 4)), Name(new Point(w / 4, h * 3 / 4)), Name(new Point(w * 3 / 4, h * 3 / 4))];
    }

    private string Out(string name) => System.IO.Path.Combine(_clip.Dir, name);

    private static async Task<Size> SizeOfAsync(string path)
    {
        var props = await (await StorageFile.GetFileFromPathAsync(path)).Properties.GetVideoPropertiesAsync();
        return new Size((int)props.Width, (int)props.Height);
    }

    private static int WhitePixels(Bitmap frame)
    {
        var count = 0;
        for (var y = 0; y < frame.Height; y++)
            for (var x = 0; x < frame.Width; x++)
            {
                var c = frame.GetPixel(x, y);
                if (c.R > 200 && c.G > 200 && c.B > 200) count++;
            }
        return count;
    }

    [Fact]
    public async Task ProbeAndFrameGrabReadTheSource()
    {
        if (!_clip.Available) return;
        var info = await VideoEditor.ProbeAsync(_clip.Path_);
        Assert.Equal(new Size(320, 200), info.Size);
        Assert.InRange(info.Duration.TotalSeconds, 2.0, 4.5);

        using var frame = await VideoEditor.GetFrameAsync(_clip.Path_, S(1), 160);
        Assert.Equal(160, frame.Width);
        Assert.Equal(100, frame.Height);
        Assert.Equal(["red", "green", "blue", "yellow"], Quarters(frame));
    }

    [Fact]
    public async Task PlainRenderKeepsThePictureTheRightWayUp()
    {
        if (!_clip.Available) return;
        await VideoEditor.RenderAsync(_clip.Path_, Out("plain.mp4"), await NewPlanAsync());
        using var frame = await FirstFrameAsync(Out("plain.mp4"));
        Assert.Equal(["red", "green", "blue", "yellow"], Quarters(frame));
    }

    [Fact]
    public async Task CropKeepsOnlyTheChosenRegion()
    {
        if (!_clip.Available) return;
        var plan = await NewPlanAsync() with { Crop = new Rectangle(160, 0, 160, 200) }; // right half
        await VideoEditor.RenderAsync(_clip.Path_, Out("crop.mp4"), plan);

        Assert.Equal(new Size(160, 200), await SizeOfAsync(Out("crop.mp4")));
        using var frame = await FirstFrameAsync(Out("crop.mp4"));
        Assert.Equal(["green", "green", "yellow", "yellow"], Quarters(frame));
    }

    [Theory]
    [InlineData(90, new[] { "blue", "red", "yellow", "green" })]
    [InlineData(180, new[] { "yellow", "blue", "green", "red" })]
    [InlineData(270, new[] { "green", "yellow", "red", "blue" })]
    public async Task RotationTurnsThePictureClockwise(int degrees, string[] expected)
    {
        if (!_clip.Available) return;
        var plan = await NewPlanAsync();
        for (var i = 0; i < degrees / 90; i++) plan = plan.RotateClockwise();
        var target = Out($"rot{degrees}.mp4");
        await VideoEditor.RenderAsync(_clip.Path_, target, plan);

        Assert.Equal(degrees == 180 ? new Size(320, 200) : new Size(200, 320), await SizeOfAsync(target));
        using var frame = await FirstFrameAsync(target);
        Assert.Equal(expected, Quarters(frame));
    }

    [Fact]
    public async Task FlipsMirrorWhatTheUserSees()
    {
        if (!_clip.Available) return;
        var plan = await NewPlanAsync();

        await VideoEditor.RenderAsync(_clip.Path_, Out("fliph.mp4"), plan.FlipHorizontally());
        using (var frame = await FirstFrameAsync(Out("fliph.mp4"))) Assert.Equal(["green", "red", "yellow", "blue"], Quarters(frame));

        await VideoEditor.RenderAsync(_clip.Path_, Out("flipv.mp4"), plan.FlipVertically());
        using (var frame = await FirstFrameAsync(Out("flipv.mp4"))) Assert.Equal(["blue", "yellow", "red", "green"], Quarters(frame));

        // flips made AFTER a quarter turn are still left↔right as seen on screen
        await VideoEditor.RenderAsync(_clip.Path_, Out("rot-fliph.mp4"), plan.RotateClockwise().FlipHorizontally());
        using (var frame = await FirstFrameAsync(Out("rot-fliph.mp4"))) Assert.Equal(["red", "blue", "green", "yellow"], Quarters(frame));

        // …and so is a flip made BEFORE it (the axes are swapped internally)
        await VideoEditor.RenderAsync(_clip.Path_, Out("fliph-rot.mp4"), plan.FlipHorizontally().RotateClockwise());
        using (var frame = await FirstFrameAsync(Out("fliph-rot.mp4"))) Assert.Equal(["yellow", "green", "blue", "red"], Quarters(frame));
    }

    [Fact]
    public async Task CropIsAppliedToThePictureAsRotatedAndFlipped()
    {
        if (!_clip.Available) return;
        var plan = await NewPlanAsync();

        // flip, then keep the left half of what is on screen: the original right half, mirrored
        var flipped = plan.FlipHorizontally() with { Crop = new Rectangle(0, 0, 160, 200) };
        await VideoEditor.RenderAsync(_clip.Path_, Out("flip-crop.mp4"), flipped);
        using (var frame = await FirstFrameAsync(Out("flip-crop.mp4"))) Assert.Equal(["green", "green", "yellow", "yellow"], Quarters(frame));

        // rotate, then keep the top half of the rotated picture (view is 200 × 320)
        var turned = plan.RotateClockwise() with { Crop = new Rectangle(0, 0, 200, 160) };
        await VideoEditor.RenderAsync(_clip.Path_, Out("rot-crop.mp4"), turned);
        Assert.Equal(new Size(200, 160), await SizeOfAsync(Out("rot-crop.mp4")));
        using (var frame = await FirstFrameAsync(Out("rot-crop.mp4"))) Assert.Equal(["blue", "red", "blue", "red"], Quarters(frame));

        // a crop drawn first and carried through a rotation keeps the same content: the original left half is red over blue
        var carried = (plan with { Crop = new Rectangle(0, 0, 160, 200) }).RotateClockwise();
        await VideoEditor.RenderAsync(_clip.Path_, Out("carried.mp4"), carried);
        using (var frame = await FirstFrameAsync(Out("carried.mp4"))) Assert.Equal(["blue", "red", "blue", "red"], Quarters(frame));
    }

    [Fact]
    public async Task RemovingASectionShortensTheVideoAndMuteAndCaptionsRender()
    {
        if (!_clip.Available) return;
        var info = await VideoEditor.ProbeAsync(_clip.Path_);
        var plan = (await NewPlanAsync()).Remove(S(1), S(2)) with { Mute = true };
        plan = plan.WithCaption(new VideoCaption("Hello SnapGit", S(0), S(0.8), CaptionPosition.Center, 14f));

        var progress = new List<double>();
        await VideoEditor.RenderAsync(_clip.Path_, Out("cut.mp4"), plan, new Progress<double>(progress.Add));

        var cut = await VideoEditor.ProbeAsync(Out("cut.mp4"));
        Assert.InRange(cut.Duration.TotalSeconds, info.Duration.TotalSeconds - 1.4, info.Duration.TotalSeconds - 0.6);
        Assert.Equal(1.0, progress.Last());

        using var frame = await FirstFrameAsync(Out("cut.mp4"));
        Assert.True(WhitePixels(frame) > frame.Width * frame.Height / 200, "the caption text should be visible in the first frame");
    }

    [Fact]
    public async Task CutRotateAndCaptionTogetherRunAllPassesAndTheTextStaysUpright()
    {
        if (!_clip.Available) return;
        var info = await VideoEditor.ProbeAsync(_clip.Path_);
        var plan = (await NewPlanAsync()).Remove(S(1), S(2)).RotateClockwise()
            .WithCaption(new VideoCaption("Upright text", S(0), S(0.8), CaptionPosition.Bottom, 8f));

        var progress = new List<double>();
        await VideoEditor.RenderAsync(_clip.Path_, Out("all.mp4"), plan, new Progress<double>(progress.Add));

        Assert.Equal(new Size(200, 320), await SizeOfAsync(Out("all.mp4")));
        var result = await VideoEditor.ProbeAsync(Out("all.mp4"));
        Assert.InRange(result.Duration.TotalSeconds, info.Duration.TotalSeconds - 1.5, info.Duration.TotalSeconds - 0.5);
        Assert.True(progress.Zip(progress.Skip(1), (a, b) => b >= a - 1e-9).All(x => x), "progress never goes backwards");

        // (the shell's thumbnail picks its own moment, so ask for an exact time while the caption is up)
        using var frame = await VideoEditor.GetFrameAsync(Out("all.mp4"), S(0.4), 400);
        if (Environment.GetEnvironmentVariable("SNAPGIT_SHOTS") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            frame.Save(System.IO.Path.Combine(shots, "video-cut-rotate-caption.png"));
        }
        Assert.True(WhitePixels(frame) > frame.Width * frame.Height / 400, $"caption visible (white pixels {WhitePixels(frame)} of {frame.Width}x{frame.Height})");
        Assert.Equal(["blue", "red"], Quarters(frame).Take(2)); // still rotated a quarter turn
        // the text is drawn in the bottom third of the finished picture (not rotated into a side)
        var bottomThird = frame.Clone(new Rectangle(0, frame.Height * 2 / 3, frame.Width, frame.Height - frame.Height * 2 / 3), frame.PixelFormat);
        var side = frame.Clone(new Rectangle(0, 0, frame.Width / 5, frame.Height), frame.PixelFormat);
        Assert.True(WhitePixels(bottomThird) > WhitePixels(side), "white text belongs at the bottom, not along the left edge");
    }

    [Fact]
    public async Task PlaybackDeliversFramesAtARealisticRateAndStopsAtTheEnd()
    {
        if (!_clip.Available) return;
        using var playback = new VideoPlayback { MaxWidth = 320 };
        await playback.OpenAsync(_clip.Path_);
        Assert.InRange(playback.Duration.TotalSeconds, 2.0, 4.5);
        Assert.Equal(new Size(320, 200), playback.Size);

        var frames = new List<(TimeSpan At, string[] Quarters)>();
        var wall = new List<DateTime>();
        var ended = new TaskCompletionSource();
        playback.FrameReady += (bitmap, at) => { lock (frames) { frames.Add((at, Quarters(bitmap))); wall.Add(DateTime.UtcNow); } bitmap.Dispose(); };
        playback.Ended += () => ended.TrySetResult();

        // paused seek shows exactly one frame, upright
        playback.Seek(S(1));
        await Task.Delay(800);
        lock (frames) { Assert.Single(frames); Assert.Equal(["red", "green", "blue", "yellow"], frames[0].Quarters); frames.Clear(); }

        // rapid scrubbing is coalesced, and the LAST position wins
        for (var i = 0; i < 20; i++) playback.Seek(S(0.1 * i));
        await Task.Delay(1200);
        lock (frames) { Assert.InRange(frames.Count, 1, 20); Assert.Equal(S(1.9), frames[^1].At); frames.Clear(); }

        // play from 1.0 s for ~1 s
        playback.Seek(S(1));
        await Task.Delay(300);
        lock (frames) { frames.Clear(); wall.Clear(); }
        playback.Play();
        Assert.True(playback.IsPlaying);
        await Task.Delay(1700);
        var pausedAt = DateTime.UtcNow;
        playback.Pause();
        Assert.False(playback.IsPlaying);

        // (Pause leaves one full-size still behind: that frame is not part of the playing rate)
        List<(TimeSpan At, string[] Quarters)> played;
        List<DateTime> times;
        lock (frames)
        {
            var keep = Enumerable.Range(0, frames.Count).Where(i => wall[i] <= pausedAt).ToList();
            played = keep.Select(i => frames[i]).ToList();
            times = keep.Select(i => wall[i]).ToList();
        }
        Assert.True(played.Count >= 4, $"expected at least 4 frames in 1.7 s of playback, got {played.Count}");
        Assert.True(played.Zip(played.Skip(1), (a, b) => b.At >= a.At).All(x => x), "frames come in time order");
        Assert.All(played, f => Assert.Equal(["red", "green", "blue", "yellow"], f.Quarters));

        // the picture follows the clock in real time (not faster, not slower), whatever the start-up delay was
        var videoSpan = (played[^1].At - played[0].At).TotalSeconds;
        var wallSpan = (times[^1] - times[0]).TotalSeconds;
        Assert.True(wallSpan > 0.5, $"playback should have lasted more than half a second ({wallSpan:0.00} s)");
        Assert.InRange(videoSpan / wallSpan, 0.6, 1.5);

        // after Pause no more frames arrive
        await Task.Delay(300);
        int count; lock (frames) count = frames.Count;
        await Task.Delay(400);
        lock (frames) Assert.Equal(count, frames.Count);

        // play on to the end → Ended
        playback.Seek(playback.Duration - S(0.5));
        await Task.Delay(300);
        playback.Play();
        Assert.Same(ended.Task, await Task.WhenAny(ended.Task, Task.Delay(4000)));
        Assert.False(playback.IsPlaying);
    }

    [Fact]
    public async Task StillFramesAreDecodedAtTheVideosOwnResolutionWhateverTheCapIs()
    {
        // Regression: the preview decoded at 1280 px and the view stretched it up, so every paused picture was soft.
        if (!_clip.Available) return;
        using var playback = new VideoPlayback { MaxWidth = 160 }; // a cap far below the 320 px video
        await playback.OpenAsync(_clip.Path_);

        var frames = new List<(int Width, TimeSpan At, DateTime When)>();
        playback.FrameReady += (bitmap, at) => { lock (frames) frames.Add((bitmap.Width, at, DateTime.UtcNow)); bitmap.Dispose(); };

        playback.Seek(S(1));
        await Task.Delay(800);
        lock (frames) { Assert.Single(frames); Assert.Equal(320, frames[0].Width); frames.Clear(); }   // paused: full size

        playback.Play();
        await Task.Delay(1200);
        var pausedAt = DateTime.UtcNow;
        playback.Pause();
        await Task.Delay(800);

        lock (frames)
        {
            var playing = frames.Where(f => f.When < pausedAt).ToList();
            Assert.NotEmpty(playing);
            Assert.All(playing, f => Assert.True(f.Width <= 160, $"playing frames honour the cap ({f.Width})"));
            Assert.Equal(320, frames[^1].Width);                                    // …and Pause leaves a full-size picture behind
            Assert.True(frames[^1].When >= pausedAt);
        }
    }

    [Fact]
    public async Task AnEmptyPlanIsRejectedAndNoFileIsLeftBehind()
    {
        if (!_clip.Available) return;
        var plan = (await NewPlanAsync()).Remove(S(0), S(30));
        await Assert.ThrowsAsync<ArgumentException>(() => VideoEditor.RenderAsync(_clip.Path_, Out("none.mp4"), plan));
        Assert.False(File.Exists(Out("none.mp4")));
    }

    [Fact]
    public async Task CancellingARenderLeavesNothingBehind()
    {
        if (!_clip.Available) return;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var plan = (await NewPlanAsync()).RotateClockwise();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoEditor.RenderAsync(_clip.Path_, Out("cancelled.mp4"), plan, null, cts.Token));
        Assert.False(File.Exists(Out("cancelled.mp4")));
    }
}
