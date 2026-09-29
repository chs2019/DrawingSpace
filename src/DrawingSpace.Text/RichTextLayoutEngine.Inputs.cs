#nullable enable
using System;
using System.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Text;

public sealed partial class RichTextLayoutEngine
{
    private sealed record CacheEntry(string Key, LayoutInputs Inputs, ShapeTextLayout Layout);

    /// <summary>Immutable exact snapshot of every input read by ShapeTextLayout.Create.</summary>
    private sealed class LayoutInputs
    {
        private readonly string _text, _family, _color;
        private readonly ShapeKind _kind;
        private readonly double _width, _height, _rotation, _fontSize, _opacity, _headerHeight;
        private readonly bool _bold, _italic;
        private readonly RectD? _bounds;
        private readonly SpanInput[] _spans;
        private readonly ParagraphInput[] _paragraphs;
        public LayoutInputs(Shape shape)
        {
            _text = shape.Text; _kind = shape.Kind; _width = shape.Width; _height = shape.Height;
            _rotation = shape.TextRotation; _bounds = shape.TextBounds;
            _family = shape.Style.FontFamily; _fontSize = shape.Style.FontSize; _color = shape.Style.TextColor;
            _opacity = shape.Style.Opacity; _bold = shape.Style.Bold; _italic = shape.Style.Italic;
            _headerHeight = shape.Container?.HeaderHeight ?? 34;
            _spans = shape.TextSpans.Select(s => new SpanInput(s)).ToArray();
            _paragraphs = shape.Paragraphs.Select(p => new ParagraphInput(p)).ToArray();
        }
        public bool Matches(Shape shape)
        {
            if (_text != shape.Text || _kind != shape.Kind || _width != shape.Width || _height != shape.Height
                || _rotation != shape.TextRotation || _bounds != shape.TextBounds || _family != shape.Style.FontFamily
                || _fontSize != shape.Style.FontSize || _color != shape.Style.TextColor || _opacity != shape.Style.Opacity
                || _bold != shape.Style.Bold || _italic != shape.Style.Italic || _headerHeight != (shape.Container?.HeaderHeight ?? 34)
                || _spans.Length != shape.TextSpans.Count || _paragraphs.Length != shape.Paragraphs.Count) return false;
            for (var i = 0; i < _spans.Length; i++) if (_spans[i] != new SpanInput(shape.TextSpans[i])) return false;
            for (var i = 0; i < _paragraphs.Length; i++) if (_paragraphs[i] != new ParagraphInput(shape.Paragraphs[i])) return false;
            return true;
        }
    }
    private readonly record struct SpanInput(int Start, int Length, string? Family, double? Size,
        string? Color, string? Background, bool? Bold, bool? Italic, bool? Underline, bool? Strike, double Baseline)
    {
        public SpanInput(TextSpan s) : this(s.Start, s.Length, s.FontFamily, s.FontSize, s.Color,
            s.Background, s.Bold, s.Italic, s.Underline, s.StrikeThrough, s.BaselineOffset) { }
    }
    private readonly record struct ParagraphInput(int Start, ParagraphAlignment Alignment, ParagraphDirection Direction,
        double Spacing, double Before, double After, double Left, double Right, double First, bool Bullet)
    {
        public ParagraphInput(ParagraphFormat p) : this(p.Start, p.Alignment, p.Direction, p.LineSpacing,
            p.SpaceBefore, p.SpaceAfter, p.LeftIndent, p.RightIndent, p.FirstLineIndent, p.Bullet) { }
    }
}
