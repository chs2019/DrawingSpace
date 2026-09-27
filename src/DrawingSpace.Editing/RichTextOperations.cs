using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

/// <summary>UTF-16 range operations used by native text controls and collaboration. Edits never split surrogate pairs.</summary>
public static class RichTextOperations
{
    public static void Replace(Shape shape, int start, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ValidateRange(shape.Text, start, length);
        if ((long)shape.Text.Length - length + replacement.Length > 100000) throw new ArgumentOutOfRangeException(nameof(replacement), "Shape text is limited to 100,000 UTF-16 code units.");
        var end = start + length; var delta = replacement.Length - length;
        var output = new List<TextSpan>();
        foreach (var span in shape.TextSpans)
        {
            var spanEnd = span.Start + span.Length;
            if (spanEnd <= start) { output.Add(span.Clone()); continue; }
            if (span.Start >= end) { var shifted = span.Clone(); shifted.Start += delta; output.Add(shifted); continue; }
            if (span.Start < start) { var prefix = span.Clone(); prefix.Length = start - span.Start; output.Add(prefix); }
            if (spanEnd > end) { var suffix = span.Clone(); suffix.Start = start + replacement.Length; suffix.Length = spanEnd - end; output.Add(suffix); }
        }
        var inherited = shape.TextSpans.LastOrDefault(s => s.Start <= start && s.Start + s.Length > Math.Max(0, start - 1));
        if (replacement.Length > 0 && inherited is not null) { var inserted = inherited.Clone(); inserted.Start = start; inserted.Length = replacement.Length; output.Add(inserted); }
        shape.Text = shape.Text[..start] + replacement + shape.Text[end..];
        shape.TextSpans = output.Where(s => s.Length > 0).OrderBy(s => s.Start).ToList();
        var paragraphs = new List<ParagraphFormat>();
        foreach (var paragraph in shape.Paragraphs)
        {
            if (paragraph.Start > start && paragraph.Start < end) continue;
            var clone = paragraph.Clone(); if (clone.Start >= end && clone.Start > start) clone.Start += delta;
            paragraphs.Add(clone);
        }
        shape.Paragraphs = paragraphs.GroupBy(p => p.Start).Select(g => g.Last()).OrderBy(p => p.Start).ToList();
        MarkOverride(shape, "Text");
    }

    public static void ReplaceAll(Shape shape, string text)
    {
        var commonStart = 0;
        while (commonStart < shape.Text.Length && commonStart < text.Length && shape.Text[commonStart] == text[commonStart]) commonStart++;
        if (!IsBoundary(shape.Text, commonStart)) commonStart--;
        var commonEnd = 0;
        while (commonEnd < shape.Text.Length - commonStart && commonEnd < text.Length - commonStart && shape.Text[^(commonEnd + 1)] == text[^(commonEnd + 1)]) commonEnd++;
        if (!IsBoundary(shape.Text, shape.Text.Length - commonEnd)) commonEnd--;
        Replace(shape, commonStart, shape.Text.Length - commonStart - commonEnd, text.Substring(commonStart, text.Length - commonStart - commonEnd));
    }

    public static void Format(Shape shape, int start, int length, Action<TextSpan> update)
    {
        ValidateRange(shape.Text, start, length); ArgumentNullException.ThrowIfNull(update);
        if (length == 0) return;
        var span = new TextSpan { Start = start, Length = length }; update(span);
        // Later spans override earlier ranges property-by-property, allowing independent bold/color edits.
        shape.TextSpans.Add(span); MarkOverride(shape, "Text");
    }

    public static void FormatParagraph(Shape shape, int offset, Action<ParagraphFormat> update)
    {
        ValidateRange(shape.Text, offset, 0);
        var start = offset == 0 ? 0 : shape.Text.LastIndexOf('\n', offset - 1) + 1;
        var format = shape.Paragraphs.FirstOrDefault(p => p.Start == start);
        if (format is null) { format = new() { Start = start }; shape.Paragraphs.Add(format); }
        update(format); MarkOverride(shape, "Text");
    }

    public static TextSpan StyleAt(Shape shape, int offset)
    {
        var result = new TextSpan { FontFamily = shape.Style.FontFamily, FontSize = shape.Style.FontSize, Color = shape.Style.TextColor, Bold = shape.Style.Bold, Italic = shape.Style.Italic };
        foreach (var span in shape.TextSpans.Where(s => s.Start <= offset && offset < s.Start + s.Length))
        {
            result.FontFamily = span.FontFamily ?? result.FontFamily; result.FontSize = span.FontSize ?? result.FontSize;
            result.Color = span.Color ?? result.Color; result.Background = span.Background ?? result.Background;
            result.Bold = span.Bold ?? result.Bold; result.Italic = span.Italic ?? result.Italic;
            result.Underline = span.Underline ?? result.Underline; result.StrikeThrough = span.StrikeThrough ?? result.StrikeThrough;
            if (span.BaselineOffset != 0) result.BaselineOffset = span.BaselineOffset;
        }
        return result;
    }

    public static bool IsBoundary(string text, int offset) => offset >= 0 && offset <= text.Length && (offset == 0 || offset == text.Length || !char.IsHighSurrogate(text[offset - 1]) || !char.IsLowSurrogate(text[offset]));
    public static void ValidateRange(string text, int start, int length)
    {
        if (start < 0 || length < 0 || (long)start + length > text.Length || !IsBoundary(text, start) || !IsBoundary(text, start + length)) throw new ArgumentOutOfRangeException(nameof(start), "The range must be inside the text and on UTF-16 scalar boundaries.");
    }
    private static void MarkOverride(Shape shape, string name) { if (!shape.LocalOverrides.Contains(name, StringComparer.OrdinalIgnoreCase)) shape.LocalOverrides.Add(name); }
}
