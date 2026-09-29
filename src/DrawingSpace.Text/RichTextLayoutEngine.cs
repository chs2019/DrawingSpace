#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Text.Internal;
using SkiaSharp;

namespace DrawingSpace.Text;

/// <summary>Bounded reusable rich-text layout cache. A caller-provided font resolver owns its returned typefaces.</summary>
public sealed partial class RichTextLayoutEngine : IDisposable
{
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _lru = new();
    public long CacheMisses { get; private set; }
    private readonly ResolverFontMapper _mapper;
    private bool _disposed;
    public int Capacity { get; }

    public RichTextLayoutEngine(Func<ShapeStyle, SKTypeface?>? typefaceResolver = null, int capacity = 128)
    {
        if (capacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity; _mapper = new(typefaceResolver);
    }

    public ShapeTextLayout Layout(Shape shape)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(shape);
        if (_cache.TryGetValue(shape.Id, out var cached))
        {
            if (cached.Value.Inputs.Matches(shape))
            { _lru.Remove(cached); _lru.AddFirst(cached); return cached.Value.Layout; }
            _cache.Remove(shape.Id); _lru.Remove(cached); cached.Value.Layout.Dispose();
        }
        var inputs = new LayoutInputs(shape);
        var layout = ShapeTextLayout.Create(shape, _mapper);
        CacheMisses++;
        var node = _lru.AddFirst(new CacheEntry(shape.Id, inputs, layout)); _cache.Add(shape.Id, node);
        while (_cache.Count > Capacity && _lru.Last is { } last)
        { _lru.RemoveLast(); _cache.Remove(last.Value.Key); last.Value.Layout.Dispose(); }
        return layout;
    }

    public void Clear()
    {
        foreach (var entry in _lru) entry.Layout.Dispose();
        _lru.Clear(); _cache.Clear();
    }
    public void Dispose() { if (_disposed) return; Clear(); _mapper.Dispose(); _disposed = true; }

    private sealed class ResolverFontMapper(Func<ShapeStyle, SKTypeface?>? resolve) : FontMapper, IDisposable
    {
        private readonly Dictionary<(string Family, int Weight, bool Italic), SKTypeface> _owned = [];
        public override SKTypeface TypefaceFromStyle(IStyle style, bool ignoreFontVariants)
        {
            var shapeStyle = new ShapeStyle { FontFamily = style.FontFamily, FontSize = style.FontSize, Bold = style.FontWeight >= 600, Italic = style.FontItalic };
            if (resolve?.Invoke(shapeStyle) is { } face) return face;
            var key = (style.FontFamily, style.FontWeight, style.FontItalic);
            if (_owned.TryGetValue(key, out var cached)) return cached;
            return _owned[key] = SKTypeface.FromFamilyName(style.FontFamily, new SKFontStyle(style.FontWeight, (int)style.FontWidth, style.FontItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)) ?? SKTypeface.Default;
        }
        public void Dispose()
        {
            foreach (var face in _owned.Values.Distinct()) if (!ReferenceEquals(face, SKTypeface.Default)) face.Dispose();
            _owned.Clear();
        }
    }
}

/// <summary>A shaped layout borrowed until its entry is replaced, evicted, cleared or its engine is disposed.</summary>
public sealed class ShapeTextLayout : IDisposable
{
    internal sealed record Paragraph(TextBlock Block, float X, float Y, int Utf16Start, string Text, bool Bullet);
    private readonly List<Paragraph> _paragraphs;
    private bool _disposed;
    public RectD TextBounds { get; }
    public double MeasuredHeight { get; }
    public double MeasuredWidth { get; }
    public int GlyphCount => _paragraphs.Sum(p => p.Block.FontRuns.Sum(r => r.Glyphs.Length));
    public int LineCount => _paragraphs.Sum(p => p.Block.Lines.Count);
    public double Rotation { get; }
    private ShapeTextLayout(List<Paragraph> paragraphs, RectD bounds, double height, double width, double rotation)
    { _paragraphs = paragraphs; TextBounds = bounds; MeasuredHeight = height; MeasuredWidth = width; Rotation = rotation; }

    internal static ShapeTextLayout Create(Shape shape, FontMapper mapper)
    {
        var isHeading = shape.Kind is ShapeKind.Container;
        var bounds = shape.TextBounds is { } textBox
            ? new RectD(textBox.X * shape.Width, textBox.Y * shape.Height, textBox.Width * shape.Width, textBox.Height * shape.Height)
            : shape.Kind == ShapeKind.Decision ? new RectD(shape.Width * .225, shape.Height * .16, shape.Width * .55, shape.Height * .68)
            : new RectD(8, isHeading ? 4 : 0, Math.Max(1, shape.Width - 16), isHeading ? Math.Min(shape.Height, shape.Container?.HeaderHeight ?? 34) - 8 : shape.Height);
        var paragraphs = new List<Paragraph>(); var y = 0f; var measuredWidth = 0f;
        var starts = new List<int> { 0 };
        for (var i = 0; i < shape.Text.Length; i++) if (shape.Text[i] == '\n') starts.Add(i + 1);
        try
        {
            for (var index = 0; index < starts.Count; index++)
            {
                var start = starts[index]; var end = index + 1 < starts.Count ? starts[index + 1] - 1 : shape.Text.Length;
                var format = shape.Paragraphs.Where(p => p.Start <= start).OrderBy(p => p.Start).LastOrDefault()
                    ?? new ParagraphFormat { Alignment = shape.Kind is ShapeKind.Text or ShapeKind.Container ? ParagraphAlignment.Left : ParagraphAlignment.Center };
                var indent = (float)Math.Max(-bounds.Width + 1, format.LeftIndent + (format.Bullet ? shape.Style.FontSize * 1.2 : 0));
                var width = (float)Math.Clamp(bounds.Width - indent - format.RightIndent, 1, 100000);
                var block = new TextBlock()
                {
                    FontMapper = mapper, MaxWidth = width, RenderWidth = width,
                    Alignment = format.Alignment switch { ParagraphAlignment.Left or ParagraphAlignment.Justify => TextAlignment.Left, ParagraphAlignment.Right => TextAlignment.Right, _ => TextAlignment.Center },
                    BaseDirection = format.Direction switch { ParagraphDirection.LeftToRight => TextDirection.LTR, ParagraphDirection.RightToLeft => TextDirection.RTL, _ => TextDirection.Auto }
                };
                var boundaries = shape.TextSpans.SelectMany(s => new[] { s.Start, s.Start + s.Length }).Where(p => p > start && p < end).Append(start).Append(end).Distinct().Order().ToArray();
                for (var run = 0; run < boundaries.Length - 1; run++)
                {
                    var offset = boundaries[run]; var style = CharacterStyle(shape, offset, format.LineSpacing);
                    block.AddText(shape.Text[offset..boundaries[run + 1]], style);
                }
                if (end == start) block.AddText("\u200B", CharacterStyle(shape, start, format.LineSpacing));
                block.Layout();
                if (format.FirstLineIndent != 0 && block.Lines.Count > 0)
                    foreach (var run in block.Lines[0].Runs) { run.MoveGlyphs((float)format.FirstLineIndent, 0); run.XCoord += (float)format.FirstLineIndent; }
                if (format.Alignment == ParagraphAlignment.Justify) Justify(block, width);
                y += (float)format.SpaceBefore;
                paragraphs.Add(new(block, (float)bounds.X + indent, y, start, shape.Text[start..end], format.Bullet));
                y += Math.Max(block.MeasuredHeight, (float)(shape.Style.FontSize * format.LineSpacing)) + (float)format.SpaceAfter;
                measuredWidth = Math.Max(measuredWidth, block.MeasuredWidth + indent + (float)format.RightIndent);
            }
            var top = isHeading ? bounds.Y : bounds.Y + Math.Max(0, (bounds.Height - y) / 2);
            paragraphs = paragraphs.Select(p => p with { Y = p.Y + (float)top }).ToList();
            return new(paragraphs, bounds, y, measuredWidth, shape.TextRotation);
        }
        catch { foreach (var paragraph in paragraphs) paragraph.Block.Clear(); throw; }
    }

    private static void Justify(TextBlock block, float available)
    {
        foreach (var line in block.Lines.Take(Math.Max(0, block.Lines.Count - 1)))
        {
            var stops = new List<float>();
            foreach (var run in line.Runs.Where(r => r.RunKind == FontRunKind.Normal))
                for (var index = run.Start; index < run.End; index++)
                    if (run.CodePoints[index - run.Start] is 32 or 9)
                    {
                        var a = run.GetXCoordOfCodePointIndex(index); var b = run.GetXCoordOfCodePointIndex(index + 1);
                        var stop = Math.Max(a, b); if (stop < line.Width - .01f) stops.Add(stop);
                    }
            if (stops.Count == 0 || available <= line.Width) continue;
            stops.Sort(); var increment = (available - line.Width) / stops.Count;
            float Shift(float position) => stops.Count(s => s <= position + .001f) * increment;
            foreach (var run in line.Runs)
            {
                var x = run.XCoord; var moved = Shift(x);
                for (var i = 0; i < run.GlyphPositions.Length; i++) run.GlyphPositions[i].X += Shift(run.GlyphPositions[i].X);
                for (var i = 0; i < run.RelativeCodePointXCoords.Length; i++) run.RelativeCodePointXCoords[i] += Shift(x + run.RelativeCodePointXCoords[i]) - moved;
                run.Width += Shift(x + run.Width) - moved; run.XCoord += moved; run.MoveGlyphs(0, 0);
            }
            line.Width = available;
        }
    }

    private static Style CharacterStyle(Shape shape, int offset, double lineHeight)
    {
        string family = shape.Style.FontFamily, color = shape.Style.TextColor; string? background = null;
        double size = shape.Style.FontSize, baseline = 0; bool bold = shape.Style.Bold, italic = shape.Style.Italic, underline = false, strike = false;
        foreach (var span in shape.TextSpans.Where(s => s.Start <= offset && offset < s.Start + s.Length))
        {
            family = span.FontFamily ?? family; color = span.Color ?? color; background = span.Background ?? background;
            size = span.FontSize ?? size; bold = span.Bold ?? bold; italic = span.Italic ?? italic; underline = span.Underline ?? underline; strike = span.StrikeThrough ?? strike;
            if (span.BaselineOffset != 0) baseline = span.BaselineOffset;
        }
        var parsed = SKColor.Parse(color); parsed = parsed.WithAlpha((byte)Math.Clamp(Math.Round(parsed.Alpha * shape.Style.Opacity), 0, 255));
        var style = new Style
        {
            FontFamily = family, FontSize = (float)size, FontWeight = bold ? 700 : 400, FontItalic = italic,
            LineHeight = (float)lineHeight, TextColor = parsed,
            Underline = underline ? UnderlineStyle.Solid : UnderlineStyle.None,
            StrikeThrough = strike ? StrikeThroughStyle.Solid : StrikeThroughStyle.None,
            FontVariant = baseline > 0 ? FontVariant.SuperScript : baseline < 0 ? FontVariant.SubScript : FontVariant.Normal
        };
        if (background is not null) style.BackgroundColor = SKColor.Parse(background);
        return style;
    }

    public void Paint(SKCanvas canvas, PointD origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(canvas);
        canvas.Save(); canvas.Translate((float)origin.X, (float)origin.Y);
        if (Rotation != 0) canvas.RotateDegrees((float)Rotation, (float)TextBounds.Center.X, (float)TextBounds.Center.Y);
        canvas.ClipRect(new SKRect((float)TextBounds.Left, (float)TextBounds.Top, (float)TextBounds.Right, (float)TextBounds.Bottom));
        foreach (var paragraph in _paragraphs)
        {
            paragraph.Block.Paint(canvas, new SKPoint(paragraph.X, paragraph.Y));
            if (paragraph.Bullet && paragraph.Block.FontRuns.FirstOrDefault() is { } run)
            {
                using var paint = new SKPaint { IsAntialias = true, Color = run.Style.TextColor };
                canvas.DrawCircle(paragraph.X - run.Style.FontSize * .65f, paragraph.Y + run.Line.YCoord + run.Line.BaseLine - run.Style.FontSize * .3f, Math.Max(1, run.Style.FontSize * .12f), paint);
            }
        }
        canvas.Restore();
    }

    public int HitTest(PointD localPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_paragraphs.Count == 0) return 0;
        localPoint = MatrixD.Around(TextBounds.Center, MatrixD.Rotation(-Rotation)).Map(localPoint);
        var paragraph = _paragraphs.OrderBy(p => Distance(localPoint.Y, p.Y, p.Y + p.Block.MeasuredHeight)).First();
        var line = paragraph.Block.Lines.OrderBy(l => Distance(localPoint.Y - paragraph.Y, l.YCoord, l.YCoord + l.Height)).FirstOrDefault();
        if (line is null) return paragraph.Utf16Start;
        var bestIndex = line.Start; var distance = double.MaxValue;
        var valid = paragraph.Block.CaretIndicies.ToHashSet();
        foreach (var run in line.Runs)
            for (var index = run.Start; index <= run.End; index++)
            {
                if (!valid.Contains(index)) continue;
                var delta = Math.Abs(localPoint.X - paragraph.X - run.GetXCoordOfCodePointIndex(index));
                if (delta < distance) { distance = delta; bestIndex = index; }
            }
        var utf16 = 0; var scalar = 0;
        while (utf16 < paragraph.Text.Length && scalar++ < bestIndex) utf16 += char.IsHighSurrogate(paragraph.Text[utf16]) && utf16 + 1 < paragraph.Text.Length && char.IsLowSurrogate(paragraph.Text[utf16 + 1]) ? 2 : 1;
        return paragraph.Utf16Start + utf16;
    }

    public IEnumerable<GlyphOutline> GetOutlines()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var paragraph in _paragraphs)
            foreach (var run in paragraph.Block.FontRuns.Where(r => r.RunKind != FontRunKind.TrailingWhitespace))
            {
                var variantScale = run.Style.FontVariant == FontVariant.Normal ? 1f : .65f;
                var vertical = run.Style.FontVariant == FontVariant.SuperScript ? -run.Style.FontSize * .35f : run.Style.FontVariant == FontVariant.SubScript ? run.Style.FontSize * .1f : 0;
                using var font = new SKFont(run.Typeface, run.Style.FontSize * variantScale);
                using var combined = new SKPath();
                for (var i = 0; i < run.Glyphs.Length; i++)
                {
                    using var glyph = font.GetGlyphPath(run.Glyphs[i]); if (glyph is null) continue;
                    glyph.Transform(SKMatrix.CreateTranslation(paragraph.X + run.GlyphPositions[i].X, paragraph.Y + run.GlyphPositions[i].Y + vertical)); combined.AddPath(glyph);
                }
                yield return new(combined.ToSvgPathData(), run.Style.TextColor, run.Style.FontFamily, run.Style.FontSize);
            }
    }
    private static double Distance(double value, double minimum, double maximum) => value < minimum ? minimum - value : value > maximum ? value - maximum : 0;
    public void Dispose() { if (_disposed) return; foreach (var paragraph in _paragraphs) paragraph.Block.Clear(); _paragraphs.Clear(); _disposed = true; }
}

public sealed record GlyphOutline(string PathData, SKColor Color, string FontFamily, float FontSize);
