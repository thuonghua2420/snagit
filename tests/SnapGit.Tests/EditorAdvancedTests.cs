using SnapGit.Core.Library;
using SnapGit.Editor;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;
using SnapGit.Editor.Persistence;
using SnapGit.Editor.Tools;
using Xunit;

namespace SnapGit.Tests;

public class EditorAdvancedTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapgit-adv-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static EditorDocument NewDoc(int w = 200, int h = 150, Color? bg = null)
    {
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.Clear(bg ?? Color.White);
        return new EditorDocument(bmp);
    }

    private static RectangleAnnotation Box(float x, float y, float w = 30, float h = 30, Color? fill = null) => new()
    {
        Rect = new RectangleF(x, y, w, h),
        Style = new AnnotationStyle { Fill = fill ?? Color.Red, StrokeWidth = 0 },
    };

    private static ToolMouseEventArgs Mouse(float x, float y, Keys mods = Keys.None, MouseButtons button = MouseButtons.Left, int clicks = 1) =>
        new(new PointF(x, y), button, mods, clicks);

    // ---------------------------------------------------------------- persistence

    [Fact]
    public void EveryAnnotationTypeRoundTripsThroughJson()
    {
        using var layerBitmap = new Bitmap(4, 4);
        using (var g = Graphics.FromImage(layerBitmap)) g.Clear(Color.Lime);

        var style = new AnnotationStyle
        {
            Stroke = Color.FromArgb(200, 10, 20, 30), Fill = Color.FromArgb(128, 1, 2, 3), StrokeWidth = 7, Opacity = 0.6f,
            Shadow = true, Dash = LineDash.Dot, CornerRadius = 9, Head = ArrowHead.Circle, DoubleHeaded = true,
            FontFamily = "Consolas", FontSize = 33, Bold = true, Italic = true, Align = TextAlign.Right,
        };

        var originals = new List<Annotation>
        {
            new RectangleAnnotation { Rect = new RectangleF(1, 2, 30, 40), Style = style },
            new EllipseAnnotation { Rect = new RectangleF(5, 6, 7, 8) },
            new HighlightAnnotation { Rect = new RectangleF(9, 9, 20, 5) },
            new LineAnnotation { Start = new PointF(1, 1), End = new PointF(50, 60) },
            new ArrowAnnotation { Start = new PointF(10, 10), End = new PointF(90, 20), Bend = new PointF(50, 70), Style = style },
            new PenAnnotation { Points = { new PointF(1, 1), new PointF(2, 3), new PointF(5, 8) } },
            new StepAnnotation { Center = new PointF(40, 40), Number = 12, Diameter = 30 },
            new TextAnnotation { Rect = new RectangleF(3, 3, 120, 40), Text = "Xin chào — “quoted”\nline 2" },
            new CalloutAnnotation { Rect = new RectangleF(20, 20, 100, 40), Text = "Hi", Tail = new PointF(10, 90), BorderColor = Color.Purple },
            new StampAnnotation { Rect = new RectangleF(70, 70, 50, 50), Kind = StampKind.Heart },
            new BlurAnnotation { Rect = new RectangleF(0, 0, 40, 40), Mode = BlurMode.Blur, Strength = 14 },
            new SpotlightAnnotation { Rect = new RectangleF(30, 30, 60, 60), Ellipse = true },
            new MagnifyAnnotation { Rect = new RectangleF(30, 30, 60, 60), Zoom = 3.5f },
            new ImageLayerAnnotation { Rect = new RectangleF(60, 60, 4, 4), Image = new Bitmap(layerBitmap) },
        };

        var json = AnnotationJson.Serialize(originals);
        var loaded = AnnotationJson.Deserialize(json);

        Assert.Equal(originals.Count, loaded.Count);
        for (var i = 0; i < originals.Count; i++)
        {
            Assert.Equal(originals[i].GetType(), loaded[i].GetType());
            Assert.Equal(originals[i].Id, loaded[i].Id);
            Assert.Equal(originals[i].Bounds, loaded[i].Bounds);
        }

        // Colours lose their *names* on the way (Color.Transparent -> #00FFFFFF), so compare the serialised form:
        Assert.Equal(json, AnnotationJson.Serialize(loaded));
        Assert.Equal(style.Stroke.ToArgb(), loaded[0].Style.Stroke.ToArgb());
        Assert.Equal(style.Dash, loaded[0].Style.Dash);
        Assert.Equal(style.Align, loaded[0].Style.Align);
        Assert.Equal(new PointF(50, 70), ((ArrowAnnotation)loaded[4]).Bend);
        Assert.Equal([new PointF(1, 1), new PointF(2, 3), new PointF(5, 8)], ((PenAnnotation)loaded[5]).Points);
        Assert.Equal("Xin chào — “quoted”\nline 2", ((TextAnnotation)loaded[7]).Text);
        Assert.Equal(new PointF(10, 90), ((CalloutAnnotation)loaded[8]).Tail);
        Assert.Equal(StampKind.Heart, ((StampAnnotation)loaded[9]).Kind);
        Assert.Equal(14, ((BlurAnnotation)loaded[10]).Strength);
        Assert.True(((SpotlightAnnotation)loaded[11]).Ellipse);
        Assert.Equal(3.5f, ((MagnifyAnnotation)loaded[12]).Zoom);
        Assert.Equal(Color.Lime.ToArgb(), ((ImageLayerAnnotation)loaded[13]).Image!.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void JsonDoesNotContainComputedProperties()
    {
        var json = AnnotationJson.Serialize([Box(1, 1)]);
        Assert.DoesNotContain("handlePoints", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isDegenerate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"bounds\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewerDocumentVersionIsRejectedClearly()
    {
        Assert.Throws<NotSupportedException>(() => AnnotationJson.Deserialize("{\"version\":999,\"annotations\":[]}"));
        Assert.Empty(AnnotationJson.Deserialize(""));
    }

    [Fact]
    public void ProjectFileRoundTripKeepsAnnotationsEditableAndPreviewFlattened()
    {
        using var doc = NewDoc(120, 90, Color.White);
        doc.History.Execute(new AddAnnotationCommand(doc, Box(10, 10, 40, 40, Color.Blue)));
        var path = Path.Combine(_dir, "shot" + SnapProjectFile.Extension);

        ProjectStorage.Save(doc, path);
        Assert.False(doc.IsModified);
        Assert.False(File.Exists(path + ".tmp"));

        using var reopened = ProjectStorage.Load(path);
        Assert.Single(reopened.Annotations);
        Assert.Equal(Color.White.ToArgb(), reopened.Image.GetPixel(20, 20).ToArgb()); // base image untouched
        using var rendered = reopened.Render();
        Assert.Equal(Color.Blue.ToArgb(), rendered.GetPixel(20, 20).ToArgb());

        using var preview = SnapProjectFile.ReadPreview(path);
        Assert.Equal(Color.Blue.ToArgb(), preview.GetPixel(20, 20).ToArgb()); // preview includes annotations
        Assert.True(SnapProjectFile.IsProject(path));
    }

    // ---------------------------------------------------------------- selection & arrangement

    [Fact]
    public void MarqueeSelectsIntersectingAndCtrlClickToggles()
    {
        using var doc = NewDoc();
        var a = Box(10, 10);
        var b = Box(60, 10);
        var c = Box(150, 100);
        foreach (var x in new[] { a, b, c }) doc.History.Execute(new AddAnnotationCommand(doc, x));
        var ctx = new EditorContext(doc);
        ctx.SetTool(ToolKind.Select);

        // marquee over a and b
        ctx.Tool.MouseDown(ctx, Mouse(2, 2));
        ctx.Tool.MouseMove(ctx, Mouse(100, 50));
        ctx.Tool.MouseUp(ctx, Mouse(100, 50));
        Assert.Equal([a, b], ctx.Selected);

        // ctrl+click c adds it, ctrl+click a removes it
        ctx.Tool.MouseDown(ctx, Mouse(160, 110, Keys.Control));
        ctx.Tool.MouseUp(ctx, Mouse(160, 110, Keys.Control));
        Assert.Equal(3, ctx.Selected.Count);
        ctx.Tool.MouseDown(ctx, Mouse(20, 20, Keys.Control));
        ctx.Tool.MouseUp(ctx, Mouse(20, 20, Keys.Control));
        Assert.DoesNotContain(a, ctx.Selected);
    }

    [Fact]
    public void DraggingOneOfManyMovesAllAsOneUndoStep()
    {
        using var doc = NewDoc();
        var a = Box(10, 10);
        var b = Box(60, 10);
        doc.History.Execute(new AddAnnotationCommand(doc, a));
        doc.History.Execute(new AddAnnotationCommand(doc, b));
        var ctx = new EditorContext(doc);
        ctx.SetTool(ToolKind.Select);
        ctx.SetSelection([a, b]);

        ctx.Tool.MouseDown(ctx, Mouse(20, 20));
        ctx.Tool.MouseMove(ctx, Mouse(30, 45));
        ctx.Tool.MouseUp(ctx, Mouse(30, 45));
        Assert.Equal((20f, 35f), (a.Rect.X, a.Rect.Y));
        Assert.Equal((70f, 35f), (b.Rect.X, b.Rect.Y));

        doc.History.Undo(); // one step restores both
        Assert.Equal((10f, 10f), (a.Rect.X, a.Rect.Y));
        Assert.Equal((60f, 10f), (b.Rect.X, b.Rect.Y));
    }

    [Fact]
    public void ZOrderCommandsAndUndo()
    {
        using var doc = NewDoc();
        var a = Box(0, 0); var b = Box(1, 1); var c = Box(2, 2); var d = Box(3, 3);
        foreach (var x in new[] { a, b, c, d }) doc.History.Execute(new AddAnnotationCommand(doc, x));
        var ctx = new EditorContext(doc) { Selection = b };

        ctx.Arrange(1);
        Assert.Equal([a, c, b, d], doc.Annotations);
        ctx.Arrange(int.MaxValue);
        Assert.Equal([a, c, d, b], doc.Annotations);
        ctx.Arrange(int.MinValue);
        Assert.Equal([b, a, c, d], doc.Annotations);

        doc.History.Undo();
        Assert.Equal([a, c, d, b], doc.Annotations);
        doc.History.Undo();
        doc.History.Undo();
        Assert.Equal([a, b, c, d], doc.Annotations);
    }

    [Fact]
    public void DuplicateNudgeAndDeleteWork()
    {
        using var doc = NewDoc();
        var a = Box(10, 10);
        doc.History.Execute(new AddAnnotationCommand(doc, a));
        var ctx = new EditorContext(doc) { Selection = a };

        ctx.DuplicateSelection(20);
        Assert.Equal(2, doc.Annotations.Count);
        Assert.NotEqual(a.Id, doc.Annotations[1].Id);
        Assert.Equal(30f, doc.Annotations[1].Bounds.X);
        Assert.Same(doc.Annotations[1], ctx.Selection);

        ctx.Nudge(5, -5);
        Assert.Equal(35f, doc.Annotations[1].Bounds.X);
        doc.History.Undo();
        Assert.Equal(30f, doc.Annotations[1].Bounds.X);

        ctx.DeleteSelection();
        Assert.Single(doc.Annotations);
        doc.History.Undo();
        Assert.Equal(2, doc.Annotations.Count);
    }

    [Fact]
    public void StyleChangeAppliesToAllSelected()
    {
        using var doc = NewDoc();
        var a = Box(0, 0); var b = Box(50, 50);
        doc.History.Execute(new AddAnnotationCommand(doc, a));
        doc.History.Execute(new AddAnnotationCommand(doc, b));
        var ctx = new EditorContext(doc);
        ctx.SetSelection([a, b]);

        ctx.UpdateStyle(s => s with { Opacity = 0.5f });
        Assert.Equal(0.5f, a.Style.Opacity);
        Assert.Equal(0.5f, b.Style.Opacity);
        doc.History.Undo();
        Assert.Equal(1f, a.Style.Opacity);
        Assert.Equal(1f, b.Style.Opacity);
    }

    // ---------------------------------------------------------------- new annotations

    [Fact]
    public void CurvedArrowBendsAndStraightensAgain()
    {
        var arrow = new ArrowAnnotation { Start = new PointF(0, 0), End = new PointF(100, 0), Style = new AnnotationStyle { StrokeWidth = 4 } };
        Assert.Equal(3, arrow.HandlePoints.Count);
        Assert.False(arrow.HitTest(new PointF(50, 40), 2));

        arrow.MoveHandle(2, new PointF(50, 80)); // pull the middle handle down
        Assert.NotNull(arrow.Bend);
        Assert.True(arrow.HitTest(new PointF(50, 40), 2));      // the curve now passes near (50,40)
        Assert.False(arrow.HitTest(new PointF(50, 0), 2));

        arrow.MoveHandle(2, new PointF(50, 1)); // back on the line
        Assert.Null(arrow.Bend);
    }

    [Fact]
    public void ArrowHeadStylesAllRender()
    {
        foreach (var head in Enum.GetValues<ArrowHead>())
        foreach (var doubleHeaded in new[] { false, true })
        {
            using var doc = NewDoc(120, 60);
            var arrow = new ArrowAnnotation
            {
                Start = new PointF(10, 30), End = new PointF(110, 30), Bend = new PointF(60, 5),
                Style = new AnnotationStyle { Stroke = Color.Black, StrokeWidth = 5, Head = head, DoubleHeaded = doubleHeaded, Dash = LineDash.Dash },
            };
            doc.History.Execute(new AddAnnotationCommand(doc, arrow));
            using var rendered = doc.Render();
            var dark = Enumerable.Range(0, 120).SelectMany(x => Enumerable.Range(0, 60).Select(y => rendered.GetPixel(x, y))).Count(p => p.R < 80);
            Assert.True(dark > 20, $"{head}/{doubleHeaded} drew nothing");
        }
    }

    [Fact]
    public void CalloutBubbleCoversRectAndTail()
    {
        using var doc = NewDoc(240, 180);
        var callout = new CalloutAnnotation
        {
            Rect = new RectangleF(60, 20, 120, 50), Text = "Hello", Tail = new PointF(70, 140),
            Style = new AnnotationStyle { Stroke = Color.Black, Fill = Color.Yellow, StrokeWidth = 2, CornerRadius = 10 },
        };
        doc.History.Execute(new AddAnnotationCommand(doc, callout));

        using var rendered = doc.Render();
        Assert.Equal(Color.Yellow.ToArgb(), rendered.GetPixel(170, 30).ToArgb());   // bubble body (away from text)
        Assert.Equal(Color.White.ToArgb(), rendered.GetPixel(200, 120).ToArgb());   // nothing far away
        Assert.True(rendered.GetPixel(70, 120).ToArgb() != Color.White.ToArgb());   // tail reaches down
        Assert.True(callout.HitTest(new PointF(70, 130), 3));
        Assert.Equal(9, callout.HandlePoints.Count);

        callout.MoveHandle(8, new PointF(215, 45)); // tail out of the right side: must still produce a valid path
        using var again = doc.Render();
        Assert.True(again.GetPixel(195, 45).ToArgb() != Color.White.ToArgb(), $"pixel is {again.GetPixel(195, 45)}");
    }

    [Fact]
    public void EveryStampKindDrawsSomething()
    {
        foreach (var kind in Enum.GetValues<StampKind>())
        {
            using var doc = NewDoc(80, 80);
            doc.History.Execute(new AddAnnotationCommand(doc, new StampAnnotation { Kind = kind, Rect = new RectangleF(5, 5, 70, 70), Style = new AnnotationStyle { Stroke = Color.White } }));
            using var rendered = doc.Render();
            var changed = Enumerable.Range(0, 80).SelectMany(x => Enumerable.Range(0, 80).Select(y => rendered.GetPixel(x, y))).Count(p => p.ToArgb() != Color.White.ToArgb());
            Assert.True(changed > 200, $"{kind} drew only {changed} pixels");
        }
    }

    [Fact]
    public void SpotlightDimsOutsideOnly()
    {
        using var doc = NewDoc(100, 100);
        doc.History.Execute(new AddAnnotationCommand(doc, new SpotlightAnnotation { Rect = new RectangleF(30, 30, 40, 40), Style = new AnnotationStyle { Opacity = 1f } }));
        using var rendered = doc.Render();
        Assert.Equal(Color.White.ToArgb(), rendered.GetPixel(50, 50).ToArgb());
        Assert.True(rendered.GetPixel(5, 5).R < 100, $"outside pixel is {rendered.GetPixel(5, 5)}");
    }

    [Fact]
    public void MagnifyEnlargesTheRegionUnderIt()
    {
        var bmp = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Black, 48, 48, 4, 4); // tiny dot in the centre
        }
        using var doc = new EditorDocument(bmp);
        doc.History.Execute(new AddAnnotationCommand(doc, new MagnifyAnnotation { Rect = new RectangleF(30, 30, 40, 40), Zoom = 4f, Style = new AnnotationStyle { StrokeWidth = 0 } }));

        using var rendered = doc.Render();
        Assert.True(rendered.GetPixel(57, 50).R < 128, $"pixel is {rendered.GetPixel(57, 50)}");  // dot grew from 4px to 20px wide: x=57 is now (partly) inside it
        Assert.True(rendered.GetPixel(52, 50).R < 60);
        Assert.True(doc.Image.GetPixel(57, 50).R > 200); // original image unchanged (the dot is only 48..51 wide)
    }

    // ---------------------------------------------------------------- image operations & pixel tools

    [Fact]
    public void FlattenedOperationConsumesAnnotationsAndUndoRestoresThem()
    {
        using var doc = NewDoc(100, 50);
        var box = Box(10, 10, 20, 20, Color.Blue);
        doc.History.Execute(new AddAnnotationCommand(doc, box));

        doc.ApplyImageOperation("Rotate", b => SnapGit.Core.Imaging.ImageOps.RotateFlip(b, RotateFlipType.Rotate90FlipNone), flatten: true);
        Assert.Equal(new Size(50, 100), doc.Image.Size);
        Assert.Empty(doc.Annotations);
        Assert.Equal(Color.Blue.ToArgb(), doc.Image.GetPixel(50 - 1 - 15, 15).ToArgb()); // box rotated into the image

        doc.History.Undo();
        Assert.Equal(new Size(100, 50), doc.Image.Size);
        Assert.Same(box, Assert.Single(doc.Annotations));
    }

    [Fact]
    public void CanvasResizeShiftsAnnotations()
    {
        using var doc = NewDoc(100, 50);
        var box = Box(10, 10);
        doc.History.Execute(new AddAnnotationCommand(doc, box));

        // Grow the canvas by 20px on the left/top: image content moves +20,+20 => shift is the NEGATIVE offset.
        doc.ApplyImageOperation("Canvas", b => SnapGit.Core.Imaging.ImageOps.ResizeCanvas(b, 120, 70, new Point(20, 20), Color.White), flatten: false, shift: new Point(-20, -20));
        Assert.Equal(new Size(120, 70), doc.Image.Size);
        Assert.Equal(30f, box.Rect.X);

        doc.History.Undo();
        Assert.Equal(10f, box.Rect.X);
    }

    [Fact]
    public void FillToolFloodFillsTheBaseImageUndoably()
    {
        using var doc = NewDoc(60, 60);
        var ctx = new EditorContext(doc);
        ctx.SetTool(ToolKind.Fill);

        ctx.Tool.MouseDown(ctx, Mouse(10, 10));
        Assert.Equal(Color.FromArgb(229, 57, 53).ToArgb(), doc.Image.GetPixel(30, 30).ToArgb());
        doc.History.Undo();
        Assert.Equal(Color.White.ToArgb(), doc.Image.GetPixel(30, 30).ToArgb());
    }

    [Fact]
    public void EyedropperSetsColourAndReturnsToPreviousTool()
    {
        using var doc = NewDoc(60, 60, Color.FromArgb(10, 120, 200));
        var ctx = new EditorContext(doc);
        ctx.SetTool(ToolKind.Rectangle);
        ctx.SetTool(ToolKind.Eyedropper);

        ctx.Tool.MouseDown(ctx, Mouse(5, 5));
        Assert.Equal(ToolKind.Rectangle, ctx.ActiveTool);
        Assert.Equal(Color.FromArgb(10, 120, 200).ToArgb(), ctx.GetToolStyle(ToolKind.Rectangle).Stroke.ToArgb());

        ctx.SetTool(ToolKind.Eyedropper);
        ctx.Tool.MouseDown(ctx, Mouse(5, 5, Keys.Shift));
        Assert.Equal(Color.FromArgb(10, 120, 200).ToArgb(), ctx.GetToolStyle(ToolKind.Rectangle).Fill.ToArgb());
    }

    [Fact]
    public void CutOutToolRemovesBandOnEnter()
    {
        using var doc = NewDoc(100, 60);
        var ctx = new EditorContext(doc);
        ctx.SetTool(ToolKind.CutOut);

        ctx.Tool.MouseDown(ctx, Mouse(30, 10));
        ctx.Tool.MouseMove(ctx, Mouse(50, 12));   // mostly horizontal drag => vertical band (columns)
        ctx.Tool.MouseUp(ctx, Mouse(50, 12));
        Assert.True(ctx.Tool.KeyDown(ctx, Keys.Enter));

        Assert.Equal(new Size(80, 60), doc.Image.Size);
        doc.History.Undo();
        Assert.Equal(new Size(100, 60), doc.Image.Size);
    }

    [Fact]
    public void SmartMoveLiftsAnObjectIntoAMovableLayerAndPatchesTheHole()
    {
        var bmp = new Bitmap(120, 80);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Navy, 30, 20, 30, 20);
        }
        using var doc = new EditorDocument(bmp);
        var ctx = new EditorContext(doc);
        ctx.SetTool(ToolKind.SmartMove);

        ctx.Tool.MouseDown(ctx, Mouse(40, 30));
        ctx.Tool.MouseUp(ctx, Mouse(40, 30)); // click: auto-detect

        var layer = Assert.IsType<ImageLayerAnnotation>(Assert.Single(doc.Annotations));
        Assert.Equal(new RectangleF(30, 20, 30, 20), layer.Rect);
        Assert.Equal(ToolKind.Select, ctx.ActiveTool);
        Assert.Same(layer, ctx.Selection);
        Assert.Equal(Color.White.ToArgb(), doc.Image.GetPixel(40, 30).ToArgb());       // hole patched with the background
        Assert.Equal(Color.Navy.ToArgb(), layer.Image!.GetPixel(5, 5).ToArgb());

        layer.Translate(40, 10);
        using var rendered = doc.Render();
        Assert.Equal(Color.Navy.ToArgb(), rendered.GetPixel(80, 35).ToArgb());          // object now at the new place
        Assert.Equal(Color.White.ToArgb(), rendered.GetPixel(40, 30).ToArgb());         // old place is clean

        doc.History.Undo(); // one step: layer removed and image restored
        Assert.Empty(doc.Annotations);
        Assert.Equal(Color.Navy.ToArgb(), doc.Image.GetPixel(40, 30).ToArgb());
    }

    [Fact]
    public void ArrowKeysNudgeTheSelectionInTheSelectTool()
    {
        using var doc = NewDoc();
        var a = Box(10, 10);
        doc.History.Execute(new AddAnnotationCommand(doc, a));
        var ctx = new EditorContext(doc);
        ctx.SetTool(ToolKind.Select);
        ctx.Selection = a;

        Assert.True(ctx.Tool.KeyDown(ctx, Keys.Right));
        Assert.True(ctx.Tool.KeyDown(ctx, Keys.Down | Keys.Shift));
        Assert.Equal((11f, 20f), (a.Rect.X, a.Rect.Y));
    }

    [Fact]
    public void ImageLayerPasteIsCappedToTheCanvasAndSelected()
    {
        using var doc = NewDoc(100, 100);
        var ctx = new EditorContext(doc);
        using var big = new Bitmap(400, 200);

        var layer = ctx.AddImageLayer(big);
        Assert.True(layer.Rect.Width <= 100 && layer.Rect.Height <= 100);
        Assert.Same(layer, ctx.Selection);
    }
}
