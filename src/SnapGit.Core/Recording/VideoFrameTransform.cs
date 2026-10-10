using System.Drawing.Drawing2D;

namespace SnapGit.Core.Recording;

/// <summary>Shows a decoded source frame the way the finished video will look: rotate → flip → crop, plus the captions visible at that time.</summary>
public static class VideoFrameTransform
{
    /// <param name="frame">A source frame (it may be smaller than the real video: everything scales with it).</param>
    /// <param name="includeCrop">False while the user is drawing the crop: the whole rotated + flipped picture is returned.</param>
    public static Bitmap Apply(Bitmap frame, VideoEditPlan plan, TimeSpan sourceTime, bool includeCrop = true, bool includeCaptions = true)
    {
        var source = plan.SourceSize.Width > 0 && plan.SourceSize.Height > 0 ? plan.SourceSize : frame.Size;
        var scale = (float)frame.Width / source.Width;
        var view = plan.ViewSize.IsEmpty ? source : plan.ViewSize;
        var crop = includeCrop ? plan.EffectiveCrop : new Rectangle(Point.Empty, view);

        var width = Math.Max(2, (int)Math.Round(crop.Width * scale));
        var height = Math.Max(2, (int)Math.Round(crop.Height * scale));
        var result = new Bitmap(width, height);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // Applied to the picture bottom-up: centre it, rotate, flip, move into the crop window, scale to the frame's resolution.
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(-crop.X, -crop.Y);
        g.TranslateTransform(view.Width / 2f, view.Height / 2f);
        g.ScaleTransform(plan.FlipX ? -1 : 1, plan.FlipY ? -1 : 1);
        g.RotateTransform(plan.Rotation);
        g.DrawImage(frame, new RectangleF(-source.Width / 2f, -source.Height / 2f, source.Width, source.Height));
        g.ResetTransform();

        if (includeCaptions && plan.IsKept(sourceTime))
        {
            foreach (var caption in plan.Captions)
            {
                if (sourceTime >= caption.Start && sourceTime < caption.End)
                    CaptionRenderer.Draw(g, new Size(width, height), caption);
            }
        }
        return result;
    }
}
