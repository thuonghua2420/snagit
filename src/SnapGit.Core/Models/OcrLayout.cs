namespace SnapGit.Core.Models;

/// <summary>
/// Post-processing for OCR results. The Windows engine often returns the pieces of one visual row ("Line", "001",
/// "token-7") as separate segments when they are spaced apart; this puts them back together, in reading order.
/// </summary>
public static class OcrLayout
{
    /// <param name="maxGapInLineHeights">Pieces further apart than this many line heights stay separate (real columns).</param>
    public static IReadOnlyList<OcrLine> MergeRows(IReadOnlyList<OcrLine> segments, double maxGapInLineHeights = 2.5)
    {
        // Two segments are "neighbours" when they sit on the same baseline band and are close enough.
        // Rows are the connected components of that relation, so the result never depends on input order
        // (a short word between two others bridges them).
        var parent = Enumerable.Range(0, segments.Count).ToArray();

        int Find(int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        for (var i = 0; i < segments.Count; i++)
            for (var j = i + 1; j < segments.Count; j++)
                if (AreNeighbours(segments[i].Bounds, segments[j].Bounds, maxGapInLineHeights))
                    parent[Find(i)] = Find(j);

        return Enumerable.Range(0, segments.Count)
            .GroupBy(Find)
            .Select(group =>
            {
                var ordered = group.Select(i => segments[i]).OrderBy(s => s.Bounds.Left).ToList();
                var bounds = ordered.Select(s => s.Bounds).Aggregate(RectangleF.Union);
                var text = string.Join(" ", ordered.Select(s => s.Text.Trim()).Where(t => t.Length > 0));
                return new OcrLine(text, bounds);
            })
            .OrderBy(l => l.Bounds.Top)
            .ThenBy(l => l.Bounds.Left)
            .ToList();
    }

    private static bool AreNeighbours(RectangleF a, RectangleF b, double maxGapInLineHeights)
    {
        var overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        var smaller = Math.Min(a.Height, b.Height);
        if (smaller <= 0 || overlap < smaller * 0.6) return false; // not on the same baseline band

        var gap = Math.Max(b.Left - a.Right, a.Left - b.Right);
        return gap <= Math.Max(a.Height, b.Height) * maxGapInLineHeights;
    }
}
