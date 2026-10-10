namespace SnapGit.Core.Recording;

/// <summary>A span of the SOURCE video.</summary>
public readonly record struct TimeRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Length => End - Start;
}

public enum CaptionPosition
{
    Top,
    Center,
    Bottom,
}

/// <summary>Text burned into the picture between two SOURCE times. <see cref="SizePercent"/> is the font height as % of the output height.</summary>
public sealed record VideoCaption(string Text, TimeSpan Start, TimeSpan End, CaptionPosition Position = CaptionPosition.Bottom,
    float SizePercent = 7f, int ColorArgb = unchecked((int)0xFFFFFFFF));

/// <summary>
/// Everything the video editor can do, as immutable data: which parts of the source are kept, rotation, flips, crop, mute, captions.
/// Picture operations happen in the order rotate → flip → crop, and the crop is expressed in the picture the user SEES (after rotating and
/// flipping), which is also how the system's transform effect works. The editor keeps a stack of plans for undo.
/// </summary>
public sealed record VideoEditPlan
{
    /// <summary>Shorter pieces than this are dropped (a sliver would render as a visible glitch).</summary>
    public static readonly TimeSpan MinPiece = TimeSpan.FromMilliseconds(100);

    public TimeSpan SourceDuration { get; init; }
    public Size SourceSize { get; init; }

    /// <summary>Sorted, non-overlapping parts of the source that stay, in play order.</summary>
    public IReadOnlyList<TimeRange> Keep { get; init; } = [];

    /// <summary>0, 90, 180 or 270 degrees clockwise.</summary>
    public int Rotation { get; init; }

    /// <summary>Mirror left↔right / top↔bottom of the rotated picture.</summary>
    public bool FlipX { get; init; }
    public bool FlipY { get; init; }

    /// <summary>Crop in pixels of the rotated + flipped picture (<see cref="ViewSize"/>); null = all of it.</summary>
    public Rectangle? Crop { get; init; }

    public bool Mute { get; init; }
    public IReadOnlyList<VideoCaption> Captions { get; init; } = [];

    public static VideoEditPlan For(TimeSpan duration, Size sourceSize) =>
        new() { SourceDuration = duration, SourceSize = sourceSize, Keep = [new TimeRange(TimeSpan.Zero, duration)] };

    public TimeSpan KeptDuration => Keep.Aggregate(TimeSpan.Zero, (sum, range) => sum + range.Length);

    public bool IsUnchanged =>
        Keep is [var only] && only.Start == TimeSpan.Zero && only.End == SourceDuration &&
        !ChangesPicture && !Mute && Captions.Count == 0;

    // ------------------------------------------------------------------ time ranges

    /// <summary>Keeps only [start, end] of what is currently kept.</summary>
    public VideoEditPlan KeepOnly(TimeSpan start, TimeSpan end) => this with { Keep = Normalize(Intersect(Keep, new TimeRange(start, end))) };

    /// <summary>Removes [start, end]; what is left on both sides is joined.</summary>
    public VideoEditPlan Remove(TimeSpan start, TimeSpan end) => this with { Keep = Normalize(Subtract(Keep, new TimeRange(start, end))) };

    public bool IsKept(TimeSpan time) => Keep.Any(r => time >= r.Start && time < r.End);

    /// <summary>Where playback should resume when <paramref name="time"/> falls into a removed part (null = past the last kept part).</summary>
    public TimeSpan? NextKeptStart(TimeSpan time) => Keep.Where(r => r.End > time).Select(r => (TimeSpan?)TimeSpan.FromTicks(Math.Max(r.Start.Ticks, time.Ticks))).FirstOrDefault();

    /// <summary>Position in the finished video of a source time (a removed time maps to where the cut joins).</summary>
    public TimeSpan ToOutput(TimeSpan source)
    {
        var offset = TimeSpan.Zero;
        foreach (var range in Keep)
        {
            if (source < range.Start) return offset;
            if (source < range.End) return offset + (source - range.Start);
            offset += range.Length;
        }
        return offset;
    }

    /// <summary>The pieces of the output timeline during which <paramref name="caption"/> is visible (it may straddle a cut).</summary>
    public IEnumerable<(TimeSpan Start, TimeSpan End)> OutputIntervals(VideoCaption caption)
    {
        var offset = TimeSpan.Zero;
        foreach (var range in Keep)
        {
            var start = TimeSpan.FromTicks(Math.Max(range.Start.Ticks, caption.Start.Ticks));
            var end = TimeSpan.FromTicks(Math.Min(range.End.Ticks, caption.End.Ticks));
            if (end > start) yield return (offset + (start - range.Start), offset + (end - range.Start));
            offset += range.Length;
        }
    }

    // ------------------------------------------------------------------ picture

    /// <summary>Size of the picture after rotating (before any crop).</summary>
    public Size ViewSize => Rotation is 90 or 270 ? new Size(SourceSize.Height, SourceSize.Width) : SourceSize;

    /// <summary>The crop clamped to the picture and made even-sized (H.264 needs even dimensions).</summary>
    public Rectangle EffectiveCrop
    {
        get
        {
            var view = new Rectangle(Point.Empty, ViewSize);
            var crop = Crop is { } c ? Rectangle.Intersect(c, view) : view;
            if (crop.Width < 2 || crop.Height < 2) crop = view;
            return new Rectangle(crop.X & ~1, crop.Y & ~1, Math.Max(2, crop.Width & ~1), Math.Max(2, crop.Height & ~1));
        }
    }

    /// <summary>Size of the finished picture.</summary>
    public Size OutputSize => EffectiveCrop.Size;

    /// <summary>True when the picture itself changes (so the transform pass is needed).</summary>
    public bool ChangesPicture => Rotation != 0 || FlipX || FlipY || (Crop is not null && EffectiveCrop != new Rectangle(Point.Empty, ViewSize));

    /// <summary>Turns what the user sees a quarter clockwise; an existing crop turns with it.</summary>
    public VideoEditPlan RotateClockwise()
    {
        var view = ViewSize;
        return this with
        {
            Rotation = (Rotation + 90) % 360,
            // rotating a mirrored picture is the same as rotating first and mirroring the other axis
            FlipX = FlipY,
            FlipY = FlipX,
            Crop = Crop is { } c ? new Rectangle(view.Height - c.Y - c.Height, c.X, c.Height, c.Width) : null,
        };
    }

    public VideoEditPlan RotateCounterClockwise()
    {
        var view = ViewSize;
        return this with
        {
            Rotation = (Rotation + 270) % 360,
            FlipX = FlipY,
            FlipY = FlipX,
            Crop = Crop is { } c ? new Rectangle(c.Y, view.Width - c.X - c.Width, c.Height, c.Width) : null,
        };
    }

    /// <summary>Mirrors what the user sees left↔right.</summary>
    public VideoEditPlan FlipHorizontally() => this with
    {
        FlipX = !FlipX,
        Crop = Crop is { } c ? new Rectangle(ViewSize.Width - c.X - c.Width, c.Y, c.Width, c.Height) : null,
    };

    /// <summary>Mirrors what the user sees top↔bottom.</summary>
    public VideoEditPlan FlipVertically() => this with
    {
        FlipY = !FlipY,
        Crop = Crop is { } c ? new Rectangle(c.X, ViewSize.Height - c.Y - c.Height, c.Width, c.Height) : null,
    };

    public VideoEditPlan WithCaption(VideoCaption caption) => this with { Captions = [.. Captions, caption] };

    public VideoEditPlan WithoutCaption(VideoCaption caption) => this with { Captions = Captions.Where(c => !ReferenceEquals(c, caption)).ToList() };

    // ------------------------------------------------------------------ range arithmetic

    private static List<TimeRange> Intersect(IEnumerable<TimeRange> ranges, TimeRange with)
    {
        var result = new List<TimeRange>();
        foreach (var range in ranges)
        {
            var start = TimeSpan.FromTicks(Math.Max(range.Start.Ticks, with.Start.Ticks));
            var end = TimeSpan.FromTicks(Math.Min(range.End.Ticks, with.End.Ticks));
            if (end > start) result.Add(new TimeRange(start, end));
        }
        return result;
    }

    private static List<TimeRange> Subtract(IEnumerable<TimeRange> ranges, TimeRange cut)
    {
        var result = new List<TimeRange>();
        foreach (var range in ranges)
        {
            if (cut.End <= range.Start || cut.Start >= range.End) { result.Add(range); continue; }
            if (cut.Start > range.Start) result.Add(new TimeRange(range.Start, cut.Start));
            if (cut.End < range.End) result.Add(new TimeRange(cut.End, range.End));
        }
        return result;
    }

    private static List<TimeRange> Normalize(List<TimeRange> ranges) =>
        ranges.Where(r => r.Length >= MinPiece).OrderBy(r => r.Start).ToList();
}
