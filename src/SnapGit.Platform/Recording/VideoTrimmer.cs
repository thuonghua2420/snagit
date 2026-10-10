using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace SnapGit.Platform.Recording;

/// <summary>Cuts a video to a time range with Windows' own media composition engine (re-encodes the result as MP4).</summary>
public static class VideoTrimmer
{
    public static async Task<TimeSpan> GetDurationAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var clip = await MediaClip.CreateFromFileAsync(file);
        return clip.OriginalDuration;
    }

    /// <summary>Writes the part of <paramref name="source"/> between <paramref name="start"/> and <paramref name="end"/> to <paramref name="target"/>.</summary>
    public static async Task TrimAsync(string source, string target, TimeSpan start, TimeSpan end)
    {
        var inputFile = await StorageFile.GetFileFromPathAsync(source);
        var clip = await MediaClip.CreateFromFileAsync(inputFile);
        var duration = clip.OriginalDuration;

        start = TimeSpan.FromTicks(Math.Clamp(start.Ticks, 0, duration.Ticks));
        end = TimeSpan.FromTicks(Math.Clamp(end.Ticks, start.Ticks, duration.Ticks));
        if (end - start < TimeSpan.FromMilliseconds(100)) throw new ArgumentException("The selected range is too short.");

        clip.TrimTimeFromStart = start;
        clip.TrimTimeFromEnd = duration - end;

        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        await using (File.Create(target)) { } // the renderer needs an existing file
        var outputFile = await StorageFile.GetFileFromPathAsync(target);

        // Same size/frame rate as the source, so a trim never changes the picture.
        var properties = await inputFile.Properties.GetVideoPropertiesAsync();
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        if (properties.Width > 0 && properties.Height > 0)
        {
            profile.Video.Width = properties.Width;
            profile.Video.Height = properties.Height;
            profile.Video.Bitrate = (uint)Math.Clamp((long)properties.Width * properties.Height * 5, 1_500_000, 25_000_000);
        }

        var result = await composition.RenderToFileAsync(outputFile, MediaTrimmingPreference.Precise, profile);
        if (result != TranscodeFailureReason.None)
        {
            try { File.Delete(target); } catch (IOException) { }
            throw new InvalidOperationException($"Trimming failed: {result}");
        }
    }
}
