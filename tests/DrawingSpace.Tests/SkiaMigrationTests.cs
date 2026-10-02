using System.Reflection;
using DrawingSpace.Documents;
using DrawingSpace.Text;
using DrawingSpace.Text.Internal;
using DrawingSpace.Text.Internal.Utils;
using SkiaSharp;

namespace DrawingSpace.Tests;

public sealed class SkiaMigrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MissingFamilyStillProducesMeasuredOutlinedText(bool bold, bool italic)
    {
        using var engine = new RichTextLayoutEngine();
        var shape = new Shape
        {
            Text = "Visible office label", Width = 500, Height = 100,
            Style = new() { FontFamily = "DrawingSpace-missing-family-7628", Bold = bold, Italic = italic }
        };
        var layout = engine.Layout(shape);
        Assert.True(layout.MeasuredWidth > 0);
        Assert.True(layout.GlyphCount > 0);
        Assert.Contains(layout.GetOutlines(), outline => !string.IsNullOrWhiteSpace(outline.PathData));
    }

    [Fact]
    public void SharedFallbackRemainsUsableAcrossEngineDisposal()
    {
        var shape = new Shape { Text = "Retained fallback", Width = 500,
            Style = new() { FontFamily = "DrawingSpace-missing-family-7628" } };
        for (var i = 0; i < 8; i++)
        {
            using var engine = new RichTextLayoutEngine(capacity: 1);
            Assert.Contains(engine.Layout(shape).GetOutlines(), outline => !string.IsNullOrWhiteSpace(outline.PathData));
        }
    }

    [Fact]
    public void CallerOwnedTypefaceSurvivesEngineDisposal()
    {
        using var face = LoadTypeface();
        using (var engine = new RichTextLayoutEngine(_ => face))
            Assert.Contains(engine.Layout(new Shape { Text = "Borrowed typeface" }).GetOutlines(),
                outline => !string.IsNullOrWhiteSpace(outline.PathData));
        using var font = new SKFont(face, 20);
        Assert.True(font.MeasureText("Still usable") > 0);
        using var glyph = font.GetGlyphPath(font.GetGlyphs("A")[0]);
        Assert.NotNull(glyph);
        Assert.False(glyph.IsEmpty);
    }

    [Fact]
    public void OverhangUsesGlyphSliceOffsetRatherThanCodePointStart()
    {
        using var face = LoadTypeface();
        using var font = new SKFont(face, 20) { Subpixel = true, Edging = SKFontEdging.Antialias };
        var glyphs = font.GetGlyphs("fj");
        var storage = new ushort[glyphs.Length + 3];
        glyphs.CopyTo(storage, 2);
        var positions = new[] { new SKPoint(2, 0), new SKPoint(12, 0) };
        var run = new FontRun
        {
            Start = 123, Length = 2, Typeface = face, Style = new Style { FontSize = 20 },
            Glyphs = new Slice<ushort>(storage, 2, glyphs.Length),
            GlyphPositions = new Slice<SKPoint>(positions)
        };
        var widths = new float[glyphs.Length];
        var bounds = new SKRect[glyphs.Length];
        font.GetGlyphWidths(glyphs.AsSpan(), widths.AsSpan(), bounds.AsSpan());
        var expectedLeft = 0f;
        var expectedRight = 0f;
        for (var i = 0; i < glyphs.Length; i++)
        {
            expectedLeft = Math.Max(expectedLeft, -(positions[i].X + bounds[i].Left));
            expectedRight = Math.Max(expectedRight, positions[i].X + bounds[i].Right + 1);
        }
        var update = typeof(FontRun).GetMethod("UpdateOverhang", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(update);
        object[] args = [0f, true, true, 0f, 0f, 0f, 0f];
        update.Invoke(run, args);
        Assert.Equal(expectedLeft, (float)args[3]);
        Assert.Equal(expectedRight, (float)args[4]);
    }

    [Fact]
    public void EmbeddedFallbackIncludesHumanReadableLicense()
    {
        using var stream = typeof(RichTextLayoutEngine).Assembly.GetManifestResourceStream(
            "DrawingSpace.Text.Internal.Fonts.LICENSE.OpenSans.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var license = reader.ReadToEnd();
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", license);
        Assert.Contains("Copyright 2020 The Open Sans Project Authors", license);
    }

    private static SKTypeface LoadTypeface()
    {
        using var stream = typeof(RichTextLayoutEngine).Assembly.GetManifestResourceStream(
            "DrawingSpace.Text.Internal.Fonts.OpenSans-Regular.ttf")
            ?? throw new InvalidOperationException("Missing packaged test typeface.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        using var data = SKData.CreateCopy(memory.ToArray());
        return SKTypeface.FromData(data) ?? throw new InvalidOperationException("Invalid packaged typeface.");
    }
}
