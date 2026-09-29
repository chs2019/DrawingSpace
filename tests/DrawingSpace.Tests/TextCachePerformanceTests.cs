using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Text;

namespace DrawingSpace.Tests;

public sealed class TextCachePerformanceTests
{
    private static Shape Shape() => new()
    {
        Text = "Cache text", TextSpans = [new() { Start = 0, Length = 5, Bold = true }],
        Paragraphs = [new() { Start = 0, Alignment = ParagraphAlignment.Left }]
    };

    [Theory]
    [InlineData("text")] [InlineData("family")] [InlineData("size")] [InlineData("color")]
    [InlineData("opacity")] [InlineData("bold")] [InlineData("italic")] [InlineData("width")]
    [InlineData("height")] [InlineData("rotation")] [InlineData("bounds")] [InlineData("kind")]
    [InlineData("span")] [InlineData("paragraph")] [InlineData("header")]
    public void EveryMutableLayoutInputInvalidates(string input)
    {
        using var engine = new RichTextLayoutEngine(); var shape = Shape(); var first = engine.Layout(shape);
        switch (input)
        {
            case "text": shape.Text += "!"; break;
            case "family": shape.Style.FontFamily = "serif"; break;
            case "size": shape.Style.FontSize++; break;
            case "color": shape.Style.TextColor = "#FF0000"; break;
            case "opacity": shape.Style.Opacity = .5; break;
            case "bold": shape.Style.Bold = true; break;
            case "italic": shape.Style.Italic = true; break;
            case "width": shape.Width++; break;
            case "height": shape.Height++; break;
            case "rotation": shape.TextRotation = 30; break;
            case "bounds": shape.TextBounds = new RectD(0, 0, .5, 1); break;
            case "kind": shape.Kind = ShapeKind.Container; break;
            case "span": shape.TextSpans[0].Underline = true; break;
            case "paragraph": shape.Paragraphs[0].RightIndent = 7; break;
            case "header": shape.Container = new() { HeaderHeight = 50 }; break;
        }
        Assert.NotSame(first, engine.Layout(shape)); Assert.Equal(2, engine.CacheMisses);
    }
    [Fact] public void TranslationFillStrokeAndDataDoNotInvalidateText()
    {
        using var engine = new RichTextLayoutEngine(); var shape = Shape(); var layout = engine.Layout(shape);
        shape.X += 100; shape.Y += 100; shape.Rotation = 35; shape.ShearX = .3;
        shape.Style.Fill = "#FF0000"; shape.Style.StrokeWidth = 5; shape.Data["V"] = "50";
        Assert.Same(layout, engine.Layout(shape)); Assert.Equal(1, engine.CacheMisses);
    }
    [Fact] public void SnapshotDoesNotAliasMutableTextSpansOrParagraphs()
    {
        using var engine = new RichTextLayoutEngine(); var shape = Shape(); var a = engine.Layout(shape);
        var clone = shape.Clone(); Assert.Same(a, engine.Layout(clone));
        clone.TextSpans[0].Color = "#0000FF"; Assert.NotSame(a, engine.Layout(clone));
        Assert.NotSame(a, engine.Layout(shape)); Assert.Equal(3, engine.CacheMisses);
    }
    [Fact] public void LruEvictionAndClearAreBounded()
    {
        using var engine = new RichTextLayoutEngine(capacity: 2);
        var a = Shape(); var b = Shape(); var c = Shape();
        var first = engine.Layout(a); engine.Layout(b); Assert.Same(first, engine.Layout(a));
        engine.Layout(c); Assert.Same(first, engine.Layout(a));
        engine.Layout(b); Assert.Equal(4, engine.CacheMisses);
        engine.Clear(); Assert.NotSame(first, engine.Layout(a));
    }
    [Fact] public void WarmCacheHitDoesNotAllocateManagedMemory()
    {
        using var engine = new RichTextLayoutEngine(); var shape = Shape();
        for (var i = 0; i < 2000; i++) engine.Layout(shape);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) engine.Layout(shape);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
