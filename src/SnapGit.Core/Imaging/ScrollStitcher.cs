using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SnapGit.Core.Imaging;

public enum ScrollDirection
{
    Vertical,
    Horizontal,
}

public enum StitchOutcome
{
    /// <summary>New content was found and appended.</summary>
    Appended,

    /// <summary>The frame is (almost) identical to the previous one: the page did not scroll (end reached).</summary>
    NoChange,

    /// <summary>The frame could not be aligned with the previous one (animation, jump, too much scroll).</summary>
    NoMatch,
}

/// <summary>
/// Builds one tall (or wide) image from successive screenshots of a scrolling area.
/// Works on row signatures, tolerates a scrollbar/cursor column on the right, and keeps sticky headers/footers
/// (rows identical in consecutive frames) only once at the top/bottom.
/// Pure and deterministic — no screen access, so it is unit-tested.
/// </summary>
public sealed class ScrollStitcher : IDisposable
{
    private readonly ScrollDirection _direction;
    private readonly List<Bitmap> _strips = new();
    private Bitmap? _header;
    private Bitmap? _last;            // previous frame, normalised to vertical orientation
    private int[]? _lastSignature;
    private int _headerRows = -1;     // -1 = not detected yet
    private int _footerRows;
    private Bitmap? _footer;

    public ScrollStitcher(ScrollDirection direction = ScrollDirection.Vertical) => _direction = direction;

    /// <summary>Right-hand columns ignored when comparing rows (vertical scrollbar, mouse cursor).</summary>
    public int IgnoreTrailingPixels { get; set; } = 24;

    /// <summary>
    /// Fraction of overlapping rows that must match to accept an alignment. Deliberately not 100 %: a blinking caret,
    /// a hover highlight or a fading effect changes a few rows between two frames. A WRONG alignment of real content
    /// matches almost nothing, so this stays safe.
    /// </summary>
    public double MinMatch { get; set; } = 0.85;

    /// <summary>Two frames at least this similar (unshifted) mean the page did not scroll.</summary>
    public double NoChangeMatch { get; set; } = 0.90;

    /// <summary>Smallest overlap (in rows) accepted between two frames, as a fraction of the frame height.</summary>
    public double MinOverlapFraction { get; set; } = 0.12;

    // Each row is hashed in this many horizontal segments, and rows match when at most MaxBadSegments differ.
    // That makes alignment immune to 1-2 columns that change by themselves between frames: the window border
    // flipping with activation, a scrollbar, a blinking caret.
    private const int Segments = 24;
    private const int MaxBadSegments = 3;

    /// <summary>Fewer informative (non-background) segments than this in the overlap = the overlap proves nothing.</summary>
    private const int MinInformativeSegments = 24;

    public int FrameCount { get; private set; }

    /// <summary>How many rows the content moved in the last accepted frame (used to tune the scroll step).</summary>
    public int LastShift { get; private set; }

    /// <summary>Adds the next screenshot. The very first frame is always accepted.</summary>
    public StitchOutcome TryAdd(Bitmap frame)
    {
        var normalized = _direction == ScrollDirection.Horizontal ? Rotate(frame, RotateFlipType.Rotate90FlipNone) : new Bitmap(frame);
        var signature = Signature(normalized);

        if (_last is null)
        {
            _strips.Add(normalized);
            _last = new Bitmap(normalized);
            _lastSignature = signature;
            FrameCount = 1;
            return StitchOutcome.Appended;
        }

        if (normalized.Size != _last.Size)
        {
            normalized.Dispose();
            return StitchOutcome.NoMatch;
        }

        var height = normalized.Height;

        // First pair: detect sticky header/footer rows (identical at the same position in both frames).
        if (_headerRows < 0)
        {
            DetectFixedRows(_lastSignature!, signature, out var header, out var footer);
            _headerRows = header;
            _footerRows = footer;

            if (header > 0 || footer > 0)
            {
                // Re-cut the first strip: it must hold only the middle part.
                var first = _strips[0];
                _header = header > 0 ? Crop(first, 0, header) : null;
                var middle = Crop(first, header, height - header - footer);
                first.Dispose();
                _strips[0] = middle;
            }
        }

        var midStart = Math.Max(0, _headerRows);
        var midHeight = height - midStart - _footerRows;
        if (midHeight < 8)
        {
            normalized.Dispose();
            return StitchOutcome.NoMatch;
        }

        var prevMid = _lastSignature!.AsSpan(midStart * Segments, midHeight * Segments);
        var nextMid = signature.AsSpan(midStart * Segments, midHeight * Segments);

        if (MatchFraction(prevMid, nextMid) >= NoChangeMatch)
        {
            normalized.Dispose();
            return StitchOutcome.NoChange;
        }

        var shift = FindShift(prevMid, nextMid, ModeHash(_lastSignature!));
        if (shift <= 0)
        {
            normalized.Dispose();
            return StitchOutcome.NoMatch;
        }

        // The last `shift` rows of the new frame's middle part are new.
        _strips.Add(Crop(normalized, midStart + midHeight - shift, shift));
        _last.Dispose();
        _last = normalized;
        _lastSignature = signature;
        LastShift = shift;
        FrameCount++;
        return StitchOutcome.Appended;
    }

    /// <summary>Assembles the final image (caller owns it). Throws if no frame was added.</summary>
    public Bitmap Build()
    {
        if (_last is null) throw new InvalidOperationException("No frames were added.");

        _footer?.Dispose();
        _footer = _footerRows > 0 ? Crop(_last, _last.Height - _footerRows, _footerRows) : null;

        var width = _last.Width;
        var total = (_header?.Height ?? 0) + _strips.Sum(s => s.Height) + (_footer?.Height ?? 0);
        var result = new Bitmap(width, total, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            var y = 0;
            if (_header is not null) { g.DrawImageUnscaled(_header, 0, y); y += _header.Height; }
            foreach (var strip in _strips) { g.DrawImageUnscaled(strip, 0, y); y += strip.Height; }
            if (_footer is not null) g.DrawImageUnscaled(_footer, 0, y);
        }

        if (_direction == ScrollDirection.Horizontal)
        {
            result.RotateFlip(RotateFlipType.Rotate270FlipNone);
        }
        return result;
    }

    public void Dispose()
    {
        foreach (var strip in _strips) strip.Dispose();
        _strips.Clear();
        _header?.Dispose();
        _footer?.Dispose();
        _last?.Dispose();
    }

    // ------------------------------------------------------------------ algorithm

    /// <summary>
    /// Finds how many rows the content moved up between <paramref name="prev"/> and <paramref name="next"/>:
    /// prev[s..] == next[0..m-s]. Returns 0 when nothing matches well enough.
    /// </summary>
    private int FindShift(ReadOnlySpan<int> prev, ReadOnlySpan<int> next, int background)
    {
        var m = prev.Length / Segments;
        var minOverlap = Math.Max(8, (int)(m * MinOverlapFraction));

        var bestShift = 0;
        var bestScore = 0.0;
        for (var shift = 1; shift <= m - minOverlap; shift++)
        {
            var overlap = m - shift;
            var score = AlignmentScore(prev.Slice(shift * Segments, overlap * Segments), next[..(overlap * Segments)], background);
            // Prefer the better score; on ties prefer the larger overlap (smaller shift is checked first, so use >).
            if (score > bestScore + 1e-9)
            {
                bestScore = score;
                bestShift = shift;
                if (score >= 0.9999) break; // perfect alignment: the first perfect one is the true shift
            }
        }
        return bestScore >= MinMatch ? bestShift : 0;
    }

    /// <summary>
    /// How well two row blocks line up, judged ONLY on segments that carry content (at least one side is not plain
    /// background). Blank margins match at every offset and would make wrong alignments look good; real content only
    /// matches at the true offset. Returns 0 when there is too little content to decide.
    /// </summary>
    private static double AlignmentScore(ReadOnlySpan<int> a, ReadOnlySpan<int> b, int background)
    {
        var count = Math.Min(a.Length, b.Length);
        int informative = 0, same = 0;
        for (var i = 0; i < count; i++)
        {
            if (a[i] == background && b[i] == background) continue;
            informative++;
            if (a[i] == b[i]) same++;
        }
        return informative < MinInformativeSegments ? 0 : (double)same / informative;
    }

    /// <summary>The most frequent segment hash of a frame = its background (usually the blank page colour).</summary>
    private static int ModeHash(int[] signature)
    {
        var counts = new Dictionary<int, int>();
        foreach (var hash in signature) counts[hash] = counts.GetValueOrDefault(hash) + 1;
        return counts.MaxBy(kv => kv.Value).Key;
    }

    /// <summary>Fraction of rows that match (a row matches when at most <see cref="MaxBadSegments"/> of its segments differ).</summary>
    private static double MatchFraction(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        var rows = Math.Min(a.Length, b.Length) / Segments;
        if (rows == 0) return 0;
        var same = 0;
        for (var row = 0; row < rows; row++)
            if (RowMatches(a, row, b, row)) same++;
        return (double)same / rows;
    }

    private static bool RowMatches(ReadOnlySpan<int> a, int rowA, ReadOnlySpan<int> b, int rowB)
    {
        var bad = 0;
        for (var s = 0; s < Segments; s++)
            if (a[rowA * Segments + s] != b[rowB * Segments + s] && ++bad > MaxBadSegments) return false;
        return true;
    }

    private static void DetectFixedRows(int[] a, int[] b, out int header, out int footer)
    {
        var rows = a.Length / Segments;
        var limit = rows * 4 / 10;
        header = 0;
        while (header < limit && RowMatches(a, header, b, header)) header++;
        footer = 0;
        while (footer < limit && RowMatches(a, rows - 1 - footer, b, rows - 1 - footer)) footer++;

        // A page that did not move at all would be "all header": that is the NoChange case, not a header.
        if (header + footer >= rows * 8 / 10) { header = 0; footer = 0; }
    }

    private int[] Signature(Bitmap bitmap)
    {
        var buffer = PixelBuffer.From(bitmap);
        var usable = Math.Max(1, buffer.Width - IgnoreTrailingPixels);
        var signature = new int[buffer.Height * Segments]; // Segments hashes per row, left to right

        for (var y = 0; y < buffer.Height; y++)
        {
            for (var s = 0; s < Segments; s++)
            {
                int from = s * usable / Segments, to = (s + 1) * usable / Segments;
                unchecked
                {
                    var hash = 17;
                    for (var x = from; x < to; x++)
                        hash = hash * 31 + (buffer.Pixels[y * buffer.Width + x] & 0x00FFFFFF);
                    signature[y * Segments + s] = hash;
                }
            }
        }
        return signature;
    }

    private static Bitmap Crop(Bitmap source, int top, int height) =>
        source.Clone(new Rectangle(0, top, source.Width, height), PixelFormat.Format32bppArgb);

    private static Bitmap Rotate(Bitmap source, RotateFlipType type)
    {
        var copy = new Bitmap(source);
        copy.RotateFlip(type);
        return copy;
    }
}
