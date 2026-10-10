namespace SnapGit.Editor.Annotations;

/// <summary>Renders a stamp glyph to a bitmap (for pickers/menus).</summary>
public static class StampPreview
{
    public static Bitmap Render(StampKind kind, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var annotation = new StampAnnotation { Kind = kind, Rect = new RectangleF(1, 1, size - 2, size - 2), Style = new AnnotationStyle { Stroke = Color.White } };
        annotation.Draw(g, new RenderContext(bitmap));
        return bitmap;
    }
}
