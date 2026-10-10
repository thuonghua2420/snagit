using SnapGit.Editor;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;
using SnapGit.Editor.Persistence;
using SnapGit.Editor.Tools;
using Xunit;

namespace SnapGit.Tests;

public class EditorOrganizeTests
{
    private static EditorDocument NewDoc(int w = 300, int h = 200)
    {
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        return new EditorDocument(bmp);
    }

    private static RectangleAnnotation Box(float x, float y, float w = 30, float h = 20, Color? fill = null) => new()
    {
        Rect = new RectangleF(x, y, w, h),
        Style = new AnnotationStyle { Fill = fill ?? Color.Red, StrokeWidth = 0 },
    };

    /// <summary>The "style" part of an annotation's JSON: compares looks without caring about Color names or ids.</summary>
    private static string StyleJson(Annotation annotation) =>
        System.Text.RegularExpressions.Regex.Match(AnnotationJson.Serialize([annotation]), "\"style\":\\{.*?\\}").Value;
    private static EditorContext With(EditorDocument doc, params Annotation[] items)
    {
        foreach (var item in items) doc.History.Execute(new AddAnnotationCommand(doc, item));
        return new EditorContext(doc);
    }

    // ------------------------------------------------------------ group

    [Fact]
    public void GroupMovesAsOneAndUngroupGivesTheMembersBackInPlace()
    {
        using var doc = NewDoc();
        var a = Box(10, 10); var b = Box(100, 50); var other = Box(200, 150);
        var ctx = With(doc, a, other, b);          // z-order: a, other, b
        ctx.SetSelection([a, b]);

        Assert.True(ctx.GroupSelection());
        var group = Assert.IsType<GroupAnnotation>(Assert.Single(ctx.Selected));
        Assert.Equal([other, group], doc.Annotations);               // group sits where the top-most member (b) was
        Assert.Equal(new RectangleF(10, 10, 120, 60), group.Bounds);

        group.Translate(5, 5);
        Assert.Equal(15f, a.Rect.X);
        Assert.Equal(105f, b.Rect.X);
        Assert.True(group.HitTest(new PointF(110, 60), 2));          // hits the child b
        Assert.False(group.HitTest(new PointF(60, 30), 2));          // gap between the children

        ctx.SetSelection([group]);
        Assert.True(ctx.UngroupSelection());
        Assert.Equal([other, a, b], doc.Annotations);                 // members back at the group's place, other untouched
        Assert.Equal(2, ctx.Selected.Count);
    }

    [Fact]
    public void GroupAndUngroupUndoRestoreTheExactOriginalOrder()
    {
        using var doc = NewDoc();
        var a = Box(10, 10); var b = Box(50, 10); var c = Box(90, 10); var d = Box(130, 10);
        var ctx = With(doc, a, b, c, d);
        ctx.SetSelection([b, d]);

        ctx.GroupSelection();
        Assert.Equal(3, doc.Annotations.Count);
        doc.History.Undo();
        Assert.Equal([a, b, c, d], doc.Annotations);
        doc.History.Redo();
        Assert.Equal(3, doc.Annotations.Count);
    }

    [Fact]
    public void UndoingAMoveOfAGroupRestoresTheSameChildObjects()
    {
        // Regression guard: other history entries still point at the original children, so a group's undo
        // must restore INTO them rather than swap in copies.
        using var doc = NewDoc();
        var a = Box(10, 10); var b = Box(100, 10);
        var ctx = With(doc, a, b);
        ctx.SetSelection([a, b]);
        ctx.GroupSelection();
        var group = (GroupAnnotation)ctx.Selection!;

        var before = group.Clone();
        group.Translate(40, 0);
        doc.History.Execute(new ModifyAnnotationCommand(group, before));
        doc.History.Undo();                                  // move undone
        Assert.Same(a, group.Children[0]);                   // still the very same instances
        Assert.Equal(10f, a.Rect.X);

        doc.History.Undo();                                  // group undone
        Assert.Equal([a, b], doc.Annotations);
        Assert.Equal(10f, a.Rect.X);
        Assert.Equal(100f, b.Rect.X);
    }

    [Fact]
    public void GroupingNeedsTwoAnnotations()
    {
        using var doc = NewDoc();
        var a = Box(10, 10);
        var ctx = With(doc, a);
        ctx.Selection = a;
        Assert.False(ctx.GroupSelection());
        Assert.False(ctx.UngroupSelection());
    }

    [Fact]
    public void GroupRoundTripsThroughJsonWithItsChildrenAndRendersTheSame()
    {
        using var doc = NewDoc();
        var ctx = With(doc, Box(10, 10, fill: Color.Blue), new EllipseAnnotation { Rect = new RectangleF(60, 20, 40, 40), Style = new AnnotationStyle { Fill = Color.Green, StrokeWidth = 0 } });
        ctx.SelectAll();
        ctx.GroupSelection();
        using var before = doc.Render();

        var json = AnnotationJson.Serialize(doc.Annotations);
        using var copy = NewDoc();
        copy.LoadAnnotations(AnnotationJson.Deserialize(json));
        using var after = copy.Render();

        var group = Assert.IsType<GroupAnnotation>(Assert.Single(copy.Annotations));
        Assert.Equal(2, group.Children.Count);
        for (var y = 0; y < 100; y += 3)
            for (var x = 0; x < 150; x += 3)
                Assert.Equal(before.GetPixel(x, y).ToArgb(), after.GetPixel(x, y).ToArgb());
    }

    // ------------------------------------------------------------ align / distribute

    [Fact]
    public void AlignMovesEverythingToTheCommonEdgeInOneUndoStep()
    {
        using var doc = NewDoc();
        var a = Box(10, 10, 30, 20); var b = Box(100, 60, 50, 40); var c = Box(200, 130, 10, 10);
        var ctx = With(doc, a, b, c);
        ctx.SelectAll();

        Assert.True(ctx.Align(AlignMode.Left));
        Assert.All(new[] { a, b, c }, x => Assert.Equal(10f, x.Rect.Left));

        Assert.True(ctx.Align(AlignMode.Bottom));
        Assert.All(new[] { a, b, c }, x => Assert.Equal(140f, x.Rect.Bottom));

        Assert.True(ctx.Align(AlignMode.Center));
        var centre = a.Rect.Left + a.Rect.Width / 2;
        Assert.All(new[] { b, c }, x => Assert.Equal(centre, x.Rect.Left + x.Rect.Width / 2, 3));

        doc.History.Undo(); // the centre alignment
        Assert.Equal(10f, b.Rect.Left);
        Assert.False(ctx.Align(AlignMode.Bottom)); // already aligned: nothing to do, no empty undo step
    }

    [Fact]
    public void AlignNeedsTwoAndDistributeNeedsThree()
    {
        using var doc = NewDoc();
        var a = Box(10, 10); var b = Box(100, 10);
        var ctx = With(doc, a, b);
        ctx.SelectAll();
        Assert.False(ctx.Distribute(horizontal: true));

        ctx.Selection = a;
        Assert.False(ctx.Align(AlignMode.Top));
    }

    [Fact]
    public void DistributeMakesTheGapsEqualAndKeepsTheOuterOnesFixed()
    {
        using var doc = NewDoc(400, 100);
        var a = Box(0, 10, 20, 20); var b = Box(40, 10, 40, 20); var c = Box(300, 10, 20, 20); var d = Box(380, 10, 20, 20);
        var ctx = With(doc, d, b, a, c);   // deliberately not in position order
        ctx.SelectAll();

        Assert.True(ctx.Distribute(horizontal: true));

        var ordered = new[] { a, b, c, d }.OrderBy(x => x.Rect.Left).ToList();
        Assert.Equal(0f, ordered[0].Rect.Left);                     // outer ones stay
        Assert.Equal(400f, ordered[^1].Rect.Right);
        var gaps = Enumerable.Range(0, 3).Select(i => ordered[i + 1].Rect.Left - ordered[i].Rect.Right).ToList();
        Assert.All(gaps, g => Assert.Equal(gaps[0], g, 3));
        Assert.Equal((400f - (20 + 40 + 20 + 20)) / 3, gaps[0], 3);
    }

    // ------------------------------------------------------------ clipboard (serialised objects)

    [Fact]
    public void CopyAndPasteObjectsKeepsTheirLookAndGivesNewIds()
    {
        using var source = NewDoc();
        var rect = new RectangleAnnotation { Rect = new RectangleF(10, 10, 60, 40), Style = new AnnotationStyle { Stroke = Color.Purple, StrokeWidth = 5, Fill = Color.Yellow, CornerRadius = 8 } };
        var text = new TextAnnotation { Rect = new RectangleF(20, 80, 150, 40), Text = "Xin chào", Style = new AnnotationStyle { Bold = true, FontSize = 30 } };
        var from = With(source, rect, text);
        from.SelectAll();
        var json = from.SerializeSelection();
        Assert.NotNull(json);

        using var target = NewDoc();
        var into = new EditorContext(target);
        var pasted = into.PasteSerialized(json!, offset: 20);

        Assert.Equal(2, pasted.Count);
        Assert.Equal(2, target.Annotations.Count);
        var pastedRect = Assert.IsType<RectangleAnnotation>(pasted[0]);
        Assert.Equal(30f, pastedRect.Rect.X);                       // offset applied
        Assert.Equal(StyleJson(source.Annotations[0]), StyleJson(pastedRect)); // same look; only id and position differ
        Assert.NotEqual(rect.Id, pastedRect.Id);
        Assert.Equal("Xin chào", ((TextAnnotation)pasted[1]).Text);
        Assert.Equal(2, into.Selected.Count);

        target.History.Undo();                                       // the whole paste is one step
        Assert.Empty(target.Annotations);
    }

    [Fact]
    public void PastingTheSameObjectsTwiceNeverCreatesDuplicateIds()
    {
        using var doc = NewDoc();
        var ctx = With(doc, Box(10, 10));
        ctx.SelectAll();
        var json = ctx.SerializeSelection()!;
        ctx.PasteSerialized(json);
        ctx.PasteSerialized(json);
        Assert.Equal(3, doc.Annotations.Select(a => a.Id).Distinct().Count());
        Assert.Null(new EditorContext(NewDoc()).SerializeSelection()); // nothing selected: nothing to copy
    }

    // ------------------------------------------------------------ gradient + text outline

    [Fact]
    public void GradientFillBlendsBetweenTheTwoColours()
    {
        using var doc = NewDoc(100, 100);
        var rect = new RectangleAnnotation
        {
            Rect = new RectangleF(0, 0, 100, 100),
            Style = new AnnotationStyle { Fill = Color.Black, GradientTo = Color.White, GradientAngle = 0, StrokeWidth = 0 },  // left→right
        };
        doc.History.Execute(new AddAnnotationCommand(doc, rect));
        using var rendered = doc.Render();

        var left = rendered.GetPixel(3, 50).R;
        var middle = rendered.GetPixel(50, 50).R;
        var right = rendered.GetPixel(96, 50).R;
        Assert.True(left < 40 && right > 215, $"left {left}, right {right}");
        Assert.InRange(middle, 90, 170);
    }

    [Fact]
    public void TextOutlineAddsAnOutlineColourAroundTheGlyphs()
    {
        using var plain = NewDoc(200, 80);
        using var outlined = NewDoc(200, 80);
        TextAnnotation Make(AnnotationStyle style) => new() { Rect = new RectangleF(10, 10, 180, 60), Text = "HI", Style = style with { FontSize = 40, Bold = true, Stroke = Color.Black } };
        plain.History.Execute(new AddAnnotationCommand(plain, Make(new AnnotationStyle())));
        outlined.History.Execute(new AddAnnotationCommand(outlined, Make(new AnnotationStyle { OutlineColor = Color.Red, OutlineWidth = 3 })));

        using var a = plain.Render();
        using var b = outlined.Render();
        int Reddish(Bitmap bmp) => Enumerable.Range(0, bmp.Width).SelectMany(x => Enumerable.Range(0, bmp.Height).Select(y => bmp.GetPixel(x, y))).Count(p => p.R > 200 && p.G < 90 && p.B < 90);

        { var dir = Environment.GetEnvironmentVariable("SNAPGIT_SHOTS"); if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); b.Save(Path.Combine(dir, "outline.png")); } }
        Assert.Equal(0, Reddish(a));
        Assert.True(Reddish(b) > 150, "no outline pixels found");
    }

    [Fact]
    public void NewStyleFieldsSurviveTheProjectFile()
    {
        var style = new AnnotationStyle { GradientTo = Color.FromArgb(200, 10, 20, 30), GradientAngle = 33, OutlineColor = Color.Orange, OutlineWidth = 2.5f };
        var loaded = AnnotationJson.Deserialize(AnnotationJson.Serialize([new RectangleAnnotation { Rect = new RectangleF(1, 2, 3, 4), Style = style }]));
        Assert.Equal(StyleJson(new RectangleAnnotation { Style = style }), StyleJson(loaded[0]));
        Assert.Equal(style.GradientTo.ToArgb(), loaded[0].Style.GradientTo.ToArgb());
        Assert.Equal(33f, loaded[0].Style.GradientAngle);
        Assert.Equal(Color.Orange.ToArgb(), loaded[0].Style.OutlineColor.ToArgb());
        Assert.Equal(2.5f, loaded[0].Style.OutlineWidth);
    }
}
