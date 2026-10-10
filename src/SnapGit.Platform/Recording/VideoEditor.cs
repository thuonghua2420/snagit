using SnapGit.Core.Recording;
using Windows.Foundation;
using Windows.Media.Editing;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace SnapGit.Platform.Recording;

public readonly record struct VideoInfo(TimeSpan Duration, System.Drawing.Size Size);

/// <summary>
/// Reads and renders videos with Windows' own media engines. The source file is never modified.
/// A render is a chain of passes, only those the plan needs:
/// cut + mute (MediaComposition) → crop / mirror / rotate (MediaTranscoder with the built-in transform effect, which MediaClip does not accept) →
/// captions (MediaComposition overlay, last, so the text is not rotated with the picture).
/// </summary>
public static class VideoEditor
{
    public static async Task<VideoInfo> ProbeAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var clip = await MediaClip.CreateFromFileAsync(file);
        var properties = await file.Properties.GetVideoPropertiesAsync();
        return new VideoInfo(clip.OriginalDuration, new System.Drawing.Size((int)properties.Width, (int)properties.Height));
    }

    /// <summary>One frame of the source at <paramref name="at"/>, scaled to at most <paramref name="maxWidth"/> pixels wide.</summary>
    public static async Task<System.Drawing.Bitmap> GetFrameAsync(string path, TimeSpan at, int maxWidth)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var clip = await MediaClip.CreateFromFileAsync(file);
        var properties = await file.Properties.GetVideoPropertiesAsync();

        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        var width = (int)Math.Min(Math.Max(properties.Width, 2u), (uint)Math.Max(2, maxWidth));
        var height = Math.Max(2, (int)Math.Round(width * (double)properties.Height / Math.Max(1u, properties.Width)));
        at = TimeSpan.FromTicks(Math.Clamp(at.Ticks, 0, Math.Max(0, clip.OriginalDuration.Ticks - TimeSpan.TicksPerMillisecond)));

        using var stream = await composition.GetThumbnailAsync(at, width, height, VideoFramePrecision.NearestFrame);
        using var managed = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream);
        using var loaded = new System.Drawing.Bitmap(managed);
        return new System.Drawing.Bitmap(loaded);
    }

    /// <summary>Renders <paramref name="plan"/> applied to <paramref name="source"/> as an MP4 at <paramref name="target"/>.</summary>
    public static async Task RenderAsync(string source, string target, VideoEditPlan plan, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (plan.Keep.Count == 0) throw new ArgumentException("Nothing is left to export.", nameof(plan));

        var info = await ProbeAsync(source);
        // The plan describes the picture it was made for; the file on disk is what counts.
        plan = plan with { SourceSize = info.Size, SourceDuration = info.Duration };
        var output = plan.OutputSize;
        var transform = plan.ChangesPicture;
        var captions = plan.Captions.Any(c => plan.OutputIntervals(c).Any());
        var cut = plan.Keep is not [{ } only] || only.Start > TimeSpan.Zero || only.End < info.Duration || plan.Mute;

        // Decide the passes first so progress can be split between them.
        var passes = new List<Func<string, string, IProgress<double>, Task>>();
        var sizeAfterTransform = transform ? output : info.Size;
        if (!transform)
        {
            passes.Add((input, to, p) => CompositionPassAsync(input, to, plan.Keep, plan.Mute, captions ? plan : null, output, p, cancel));
        }
        else
        {
            if (cut) passes.Add((input, to, p) => CompositionPassAsync(input, to, plan.Keep, plan.Mute, null, info.Size, p, cancel));
            passes.Add((input, to, p) => TransformPassAsync(input, to, plan, output, p, cancel));
            if (captions)
                passes.Add((input, to, p) => CompositionPassAsync(input, to, [new TimeRange(TimeSpan.Zero, TimeSpan.MaxValue)], mute: false, plan, output, p, cancel));
        }

        var temps = new List<string>();
        try
        {
            var current = source;
            for (var i = 0; i < passes.Count; i++)
            {
                var last = i == passes.Count - 1;
                var to = last ? target : TempFile();
                if (!last) temps.Add(to);

                var index = i;
                await passes[i](current, to, new Progress<double>(p => progress?.Report((index + p) / passes.Count)));
                current = to;
            }
            progress?.Report(1);
        }
        catch
        {
            try { File.Delete(target); } catch (IOException) { }
            throw;
        }
        finally
        {
            foreach (var temp in temps)
            {
                try { File.Delete(temp); } catch (IOException) { }
            }
        }
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "snapgit-render-" + Guid.NewGuid().ToString("N") + ".mp4");

    private static MediaEncodingProfile ProfileFor(System.Drawing.Size size)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width = (uint)size.Width;
        profile.Video.Height = (uint)size.Height;
        profile.Video.ProfileId = Windows.Media.MediaProperties.H264ProfileIds.High;
        profile.Video.Bitrate = SnapGit.Core.Recording.VideoEncoding.Bitrate(size, 30, SnapGit.Core.Recording.VideoQuality.High);
        return profile;
    }

    private static async Task<StorageFile> CreateTargetAsync(string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        await using (File.Create(target)) { } // the renderer needs an existing file
        return await StorageFile.GetFileFromPathAsync(target);
    }

    /// <summary>Cut to the kept ranges, optionally silence the audio, optionally overlay captions.</summary>
    private static async Task CompositionPassAsync(string input, string target, IReadOnlyList<TimeRange> keep, bool mute, VideoEditPlan? captionPlan,
        System.Drawing.Size frame, IProgress<double> progress, CancellationToken cancel)
    {
        var inputFile = await StorageFile.GetFileFromPathAsync(input);
        var composition = new MediaComposition();
        foreach (var range in keep)
        {
            var clip = await MediaClip.CreateFromFileAsync(inputFile);
            var duration = clip.OriginalDuration;
            var start = TimeSpan.FromTicks(Math.Clamp(range.Start.Ticks, 0, duration.Ticks));
            var end = TimeSpan.FromTicks(Math.Clamp(range.End.Ticks, start.Ticks, duration.Ticks));
            clip.TrimTimeFromStart = start;
            clip.TrimTimeFromEnd = duration - end;
            if (mute) clip.Volume = 0;
            composition.Clips.Add(clip);
        }

        var tempFiles = new List<string>();
        try
        {
            if (captionPlan is not null) await AddCaptionsAsync(composition, captionPlan, frame, tempFiles);

            var outputFile = await CreateTargetAsync(target);
            var operation = composition.RenderToFileAsync(outputFile, MediaTrimmingPreference.Precise, ProfileFor(frame));
            var result = await WindowsRuntimeSystemExtensions.AsTask(operation, cancel, new Progress<double>(p => progress.Report(p / 100.0)));
            if (result != TranscodeFailureReason.None) throw new InvalidOperationException($"Rendering failed: {result}");
        }
        finally
        {
            foreach (var temp in tempFiles)
            {
                try { File.Delete(temp); } catch (IOException) { }
            }
        }
    }

    /// <summary>Rotate → flip → crop with the system's transform effect (it applies them in exactly that order).</summary>
    private static async Task TransformPassAsync(string input, string target, VideoEditPlan plan, System.Drawing.Size output,
        IProgress<double> progress, CancellationToken cancel)
    {
        var crop = plan.EffectiveCrop;
        var effect = new VideoTransformEffectDefinition
        {
            CropRectangle = new Rect(crop.X, crop.Y, crop.Width, crop.Height),
            OutputSize = new Windows.Foundation.Size(output.Width, output.Height),
            Rotation = plan.Rotation switch
            {
                90 => MediaRotation.Clockwise90Degrees,
                180 => MediaRotation.Clockwise180Degrees,
                270 => MediaRotation.Clockwise270Degrees,
                _ => MediaRotation.None,
            },
            Mirror = (plan.FlipX ? MediaMirroringOptions.Horizontal : MediaMirroringOptions.None) | (plan.FlipY ? MediaMirroringOptions.Vertical : MediaMirroringOptions.None),
        };

        var inputFile = await StorageFile.GetFileFromPathAsync(input);
        var outputFile = await CreateTargetAsync(target);
        var transcoder = new MediaTranscoder();
        transcoder.AddVideoEffect(effect.ActivatableClassId, true, effect.Properties);

        var prepared = await transcoder.PrepareFileTranscodeAsync(inputFile, outputFile, ProfileFor(output));
        if (!prepared.CanTranscode) throw new InvalidOperationException($"Cannot transform the video: {prepared.FailureReason}.");
        await WindowsRuntimeSystemExtensions.AsTask(prepared.TranscodeAsync(), cancel, new Progress<double>(p => progress.Report(p / 100.0)));
    }

    private static async Task AddCaptionsAsync(MediaComposition composition, VideoEditPlan plan, System.Drawing.Size frame, List<string> tempFiles)
    {
        var layer = new MediaOverlayLayer();
        foreach (var caption in plan.Captions)
        {
            var intervals = plan.OutputIntervals(caption).ToList();
            if (intervals.Count == 0) continue;

            var png = Path.Combine(Path.GetTempPath(), "snapgit-caption-" + Guid.NewGuid().ToString("N") + ".png");
            using (var bitmap = CaptionRenderer.Render(frame, caption)) bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            tempFiles.Add(png);
            var imageFile = await StorageFile.GetFileFromPathAsync(png);

            foreach (var (start, end) in intervals)
            {
                var clip = await MediaClip.CreateFromImageFileAsync(imageFile, end - start);
                layer.Overlays.Add(new MediaOverlay(clip, new Rect(0, 0, frame.Width, frame.Height), 1.0) { Delay = start });
            }
        }
        if (layer.Overlays.Count > 0) composition.OverlayLayers.Add(layer);
    }
}
