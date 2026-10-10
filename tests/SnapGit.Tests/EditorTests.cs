using SnapGit.Editor;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;
using SnapGit.Editor.Tools;
using Xunit;

namespace SnapGit.Tests;

public class EditorTests
{
    private static EditorDocument NewDocument(int w = 100, int h = 80, Color? background = null)
    {
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.Clear(background ?? Color.White);
        return new EditorDocument(bmp);
    }

    private static RectangleAnnotation Rect(float x, float y, float w, float h, Color? stroke = null, Color? fill = null) => new()
    {
        Rect = new RectangleF(x, y, w, h),
        Style = new AnnotationStyle { Stroke = stroke ?? Color.Red, Fill = fill ?? Color.Transparent, StrokeWidth = 4 },
    };

    [Fact]
    public void AddUndoRedo()
    {
        using var doc = NewDocument();
        var rect = Rect(10, 10, 30, 30);

        doc.History.Execute(new AddAnnotationCommand(doc, rect));
        Assert.Single(doc.Annotations);
        Assert.True(doc.IsModified);

        doc.History.Undo();
        Assert.Empty(doc.Annotations);

        doc.History.Redo();
        Assert.Same(rect, doc.Annotations[0]);
    }

    [Fact]
    public void NewCommandClearsRedo()
    {
        using var doc = NewDocument();
        doc.History.Execute(new AddAnnotationCommand(doc, Rect(0, 0, 10, 10)));
        doc.History.Undo();
        Assert.True(doc.History.CanRedo);

        doc.History.Execute(new AddAnnotationCommand(doc, Rect(5, 5, 10, 10)));
        Assert.False(doc.History.CanRedo);
    }

    [Fact]
    public void RemoveRestoresOriginalZOrder()
    {
        using var doc = NewDocument();
        var a = Rect(0, 0, 10, 10);
        var b = Rect(5, 5, 10, 10);
        var c = Rect(9, 9, 10, 10);
        foreach (var x in new[] { a, b, c }) doc.History.Execute(new AddAnnotationCommand(doc, x));

        doc.History.Execute(new RemoveAnnotationCommand(doc, b));
        Assert.Equal([a, c], doc.Annotations);

        doc.History.Undo();
        Assert.Equal([a, b, c], doc.Annotations);
    }

    [Fact]
    public void ModifyUndoRestoresState()
    {
        using var doc = NewDocument();
        var rect = Rect(10, 10, 20, 20);
        doc.History.Execute(new AddAnnotationCommand(doc, rect));

        var before = rect.Clone();
        rect.Translate(15, 5);
        doc.History.Execute(new ModifyAnnotationCommand(rect, before));
        Assert.Equal(25f, rect.Rect.X);

        doc.History.Undo();
        Assert.Equal(10f, rect.Rect.X);
        Assert.Equal(10f, rect.Rect.Y);

        doc.History.Redo();
        Assert.Equal(25f, rect.Rect.X);
        Assert.Equal(15f, rect.Rect.Y);
    }

    [Fact]
    public void CropShiftsAnnotationsAndUndoRestores()
    {
        using var doc = NewDocument(100, 80);
        var rect = Rect(30, 20, 10, 10);
        doc.History.Execute(new AddAnnotationCommand(doc, rect));

        doc.History.Execute(new CropCommand(doc, new Rectangle(20, 10, 50, 40)));
        Assert.Equal(new Size(50, 40), doc.Image.Size);
        Assert.Equal(10f, rect.Rect.X);
        Assert.Equal(10f, rect.Rect.Y);

        doc.History.Undo();
        Assert.Equal(new Size(100, 80), doc.Image.Size);
        Assert.Equal(30f, rect.Rect.X);
    }

    [Fact]
    public void CropOutsideImageThrows()
    {
        using var doc = NewDocument(50, 50);
        Assert.Throws<ArgumentException>(() => new CropCommand(doc, new Rectangle(200, 200, 10, 10)));
    }

    [Fact]
    public void MarkSavedClearsModifiedFlag()
    {
        using var doc = NewDocument();
        doc.History.Execute(new AddAnnotationCommand(doc, Rect(0, 0, 10, 10)));
        Assert.True(doc.IsModified);

        doc.MarkSaved();
        Assert.False(doc.IsModified);

        doc.History.Undo();
        Assert.True(doc.IsModified); // differs from the saved state again
    }

    [Fact]
    public void RenderDrawsAnnotationsAndLeavesBaseUntouched()
    {
        using var doc = NewDocument(60, 60);
        doc.History.Execute(new AddAnnotationCommand(doc, Rect(10, 10, 40, 40, fill: Color.Blue)));

        using var rendered = doc.Render();
        Assert.Equal(Color.Blue.ToArgb(), rendered.GetPixel(30, 30).ToArgb());   // filled interior
        Assert.Equal(Color.White.ToArgb(), rendered.GetPixel(2, 2).ToArgb());    // untouched corner
        Assert.Equal(Color.White.ToArgb(), doc.Image.GetPixel(30, 30).ToArgb()); // base image not modified
    }

    [Fact]
    public void PixelateChangesOnlyItsRegion()
    {
        var bmp = new Bitmap(40, 40);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using var black = new SolidBrush(Color.Black);
            for (var x = 0; x < 40; x += 2) g.FillRectangle(black, x, 0, 1, 40); // fine stripes
        }

        using var doc = new EditorDocument(bmp);
        var before = doc.Image.GetPixel(10, 10);
        doc.History.Execute(new AddAnnotationCommand(doc, new BlurAnnotation { Rect = new RectangleF(0, 0, 20, 40), Strength = 10 }));

        using var rendered = doc.Render();
        Assert.NotEqual(before.ToArgb(), rendered.GetPixel(10, 10).ToArgb());                      // stripes averaged away
        Assert.Equal(doc.Image.GetPixel(30, 10).ToArgb(), rendered.GetPixel(30, 10).ToArgb());     // outside region intact
    }

    [Fact]
    public void HitTestOutlineVersusFill()
    {
        var outline = Rect(10, 10, 40, 40);                 // transparent fill: only the border is clickable
        var filled = Rect(10, 10, 40, 40, fill: Color.Blue);

        Assert.False(outline.HitTest(new PointF(30, 30), 3));
        Assert.True(outline.HitTest(new PointF(10, 30), 3));
        Assert.True(filled.HitTest(new PointF(30, 30), 3));
    }

    [Fact]
    public void ArrowHitTestFollowsTheLine()
    {
        var arrow = new ArrowAnnotation { Start = new PointF(0, 0), End = new PointF(100, 0), Style = new AnnotationStyle { StrokeWidth = 4 } };
        Assert.True(arrow.HitTest(new PointF(50, 3), 2));
        Assert.False(arrow.HitTest(new PointF(50, 20), 2));
    }

    [Fact]
    public void DocumentHitTestReturnsTopMost()
    {
        using var doc = NewDocument();
        var bottom = Rect(10, 10, 40, 40, fill: Color.Red);
        var top = Rect(20, 20, 40, 40, fill: Color.Blue);
        doc.History.Execute(new AddAnnotationCommand(doc, bottom));
        doc.History.Execute(new AddAnnotationCommand(doc, top));

        Assert.Same(top, doc.HitTest(new PointF(30, 30), 2));
        Assert.Same(bottom, doc.HitTest(new PointF(12, 12), 2));
        Assert.Null(doc.HitTest(new PointF(90, 70), 2));
    }

    [Fact]
    public void BoxHandleResizeClampsInsteadOfFlipping()
    {
        var rect = Rect(10, 10, 20, 20);
        rect.MoveHandle(4, new PointF(5, 5)); // drag bottom-right past the top-left corner
        Assert.True(rect.Rect.Width >= 1 && rect.Rect.Height >= 1);
        Assert.Equal(10f, rect.Rect.X);
    }

    [Fact]
    public void ShiftConstrainsToSquareAndAngles()
    {
        var box = new RectangleAnnotation();
        box.SetDrag(new PointF(0, 0), new PointF(50, 20), constrain: true);
        Assert.Equal(box.Rect.Width, box.Rect.Height);

        var line = new LineAnnotation();
        line.SetDrag(new PointF(0, 0), new PointF(100, 8), constrain: true);
        Assert.Equal(0f, line.End.Y, 3);
    }

    [Fact]
    public void ToolsCreateUndoableAnnotations()
    {
        using var doc = NewDocument(200, 200);
        var context = new EditorContext(doc);

        context.SetTool(ToolKind.Rectangle);
        context.Tool.MouseDown(context, new ToolMouseEventArgs(new PointF(10, 10), MouseButtons.Left, Keys.None));
        context.Tool.MouseMove(context, new ToolMouseEventArgs(new PointF(60, 50), MouseButtons.Left, Keys.None));
        context.Tool.MouseUp(context, new ToolMouseEventArgs(new PointF(60, 50), MouseButtons.Left, Keys.None));

        Assert.Single(doc.Annotations);
        Assert.IsType<RectangleAnnotation>(doc.Annotations[0]);
        Assert.Same(doc.Annotations[0], context.Selection);

        doc.History.Undo();
        Assert.Empty(doc.Annotations);
    }

    [Fact]
    public void TinyDragIsDiscarded()
    {
        using var doc = NewDocument();
        var context = new EditorContext(doc);
        context.SetTool(ToolKind.Ellipse);
        context.Tool.MouseDown(context, new ToolMouseEventArgs(new PointF(10, 10), MouseButtons.Left, Keys.None));
        context.Tool.MouseUp(context, new ToolMouseEventArgs(new PointF(11, 11), MouseButtons.Left, Keys.None));
        Assert.Empty(doc.Annotations);
    }

    [Fact]
    public void StepToolNumbersSequentially()
    {
        using var doc = NewDocument();
        var context = new EditorContext(doc);
        context.SetTool(ToolKind.Step);

        foreach (var x in new[] { 10f, 40f, 70f })
            context.Tool.MouseDown(context, new ToolMouseEventArgs(new PointF(x, 20), MouseButtons.Left, Keys.None));

        Assert.Equal([1, 2, 3], doc.Annotations.OfType<StepAnnotation>().Select(s => s.Number));
    }

    [Fact]
    public void SelectToolMovesAndUndoes()
    {
        using var doc = NewDocument();
        var rect = Rect(10, 10, 40, 40, fill: Color.Red);
        doc.History.Execute(new AddAnnotationCommand(doc, rect));
        var context = new EditorContext(doc);
        context.SetTool(ToolKind.Select);

        context.Tool.MouseDown(context, new ToolMouseEventArgs(new PointF(30, 30), MouseButtons.Left, Keys.None));
        context.Tool.MouseMove(context, new ToolMouseEventArgs(new PointF(40, 45), MouseButtons.Left, Keys.None));
        context.Tool.MouseUp(context, new ToolMouseEventArgs(new PointF(40, 45), MouseButtons.Left, Keys.None));

        Assert.Equal(20f, rect.Rect.X);
        Assert.Equal(25f, rect.Rect.Y);

        doc.History.Undo();
        Assert.Equal(10f, rect.Rect.X);
    }

    [Fact]
    public void StyleChangeAppliesToSelectionAndIsUndoable()
    {
        using var doc = NewDocument();
        var rect = Rect(10, 10, 40, 40);
        doc.History.Execute(new AddAnnotationCommand(doc, rect));
        var context = new EditorContext(doc) { Selection = rect };

        context.UpdateStyle(s => s with { StrokeWidth = 9 });
        Assert.Equal(9f, rect.Style.StrokeWidth);

        doc.History.Undo();
        Assert.Equal(4f, rect.Style.StrokeWidth);
    }

    [Fact]
    public void TextCommitAddsAndEmptyTextDiscards()
    {
        using var doc = NewDocument(300, 200);
        var context = new EditorContext(doc);
        using var g = Graphics.FromImage(doc.Image);

        var text = new TextAnnotation { Rect = new RectangleF(10, 10, 200, 30), Style = new AnnotationStyle { FontSize = 20 } };
        context.CommitText(new TextEditRequest(text, IsNew: true), "   ", g);
        Assert.Empty(doc.Annotations);

        context.CommitText(new TextEditRequest(text, IsNew: true), "Hello", g);
        Assert.Single(doc.Annotations);
        Assert.Equal("Hello", text.Text);

        // Editing an existing one to empty removes it (undoable).
        context.CommitText(new TextEditRequest(text, IsNew: false), "", g);
        Assert.Empty(doc.Annotations);
        doc.History.Undo();
        Assert.Single(doc.Annotations);
    }
}
