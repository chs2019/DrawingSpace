using System.Globalization;
using System.Xml.Linq;
using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

internal sealed partial class VisioWriteContext
{
    private static (string Rgb, double Alpha) ColorParts(string color)
    {
        if (color.Length == 9) return ("#" + color[3..], byte.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d);
        return (color, 1);
    }
    private void WriteStyle(XElement owner, ShapeStyle style)
    {
        var fill = ColorParts(style.Fill); var stroke = ColorParts(style.Stroke);
        VisioXml.SetCell(owner, "FillForegnd", fill.Rgb); VisioXml.SetCell(owner, "FillPattern", fill.Alpha == 0 ? 0 : 1, "");
        VisioXml.SetCell(owner, "FillForegndTrans", 1 - fill.Alpha * style.Opacity, "");
        VisioXml.SetCell(owner, "LineColor", stroke.Rgb); VisioXml.SetCell(owner, "LineColorTrans", 1 - stroke.Alpha * style.Opacity, "");
        VisioXml.SetCell(owner, "LinePattern", style.StrokeWidth == 0 ? 0 : style.Dashed ? 2 : 1, ""); VisioXml.SetCell(owner, "LineWeight", style.StrokeWidth / 96, null, "IN");
    }

    private void WriteText(XElement owner, Shape shape)
    {
        VisioXml.Child(owner, "Text")?.Remove();
        foreach (var section in VisioXml.Children(owner, "Section").Where(s => VisioXml.Attribute(s, "N") is "Character" or "Paragraph").ToArray()) section.Remove();
        var characters = EnsureSection(owner, "Character"); var paragraphs = EnsureSection(owner, "Paragraph");
        var baseStyle = new TextSpan { FontFamily = shape.Style.FontFamily, FontSize = shape.Style.FontSize, Color = shape.Style.TextColor, Bold = shape.Style.Bold, Italic = shape.Style.Italic };
        uint nextCharacter = 0;
        uint Character(TextSpan style)
        {
            var index = nextCharacter++; var row = EnsureRow(characters, null, index);
            VisioXml.SetCell(row, "Font", _fontIds.GetValueOrDefault(style.FontFamily ?? shape.Style.FontFamily));
            VisioXml.SetCell(row, "Size", (style.FontSize ?? shape.Style.FontSize) / 96, "", "IN");
            VisioXml.SetCell(row, "Color", ColorParts(style.Color ?? shape.Style.TextColor).Rgb, "");
            VisioXml.SetCell(row, "Style", (style.Bold == true ? 1 : 0) | (style.Italic == true ? 2 : 0) | (style.Underline == true ? 4 : 0), "");
            VisioXml.SetCell(row, "Strikethru", style.StrikeThrough == true ? 1 : 0, ""); VisioXml.SetCell(row, "Pos", style.BaselineOffset > 0 ? 1 : style.BaselineOffset < 0 ? 2 : 0, "");
            return index;
        }
        Character(baseStyle);
        var paragraphIndices = new Dictionary<int, uint>();
        var formats = shape.Paragraphs.Prepend(new ParagraphFormat()).GroupBy(p => p.Start).Select(g => g.Last()).OrderBy(p => p.Start).ToArray();
        foreach (var format in formats)
        {
            var index = (uint)paragraphIndices.Count; paragraphIndices[format.Start] = index; var row = EnsureRow(paragraphs, null, index);
            VisioXml.SetCell(row, "HorzAlign", (int)format.Alignment); VisioXml.SetCell(row, "SpLine", -format.LineSpacing);
            VisioXml.SetCell(row, "SpBefore", format.SpaceBefore / 96); VisioXml.SetCell(row, "SpAfter", format.SpaceAfter / 96);
            VisioXml.SetCell(row, "IndLeft", format.LeftIndent / 96); VisioXml.SetCell(row, "IndRight", format.RightIndent / 96); VisioXml.SetCell(row, "IndFirst", format.FirstLineIndent / 96);
            VisioXml.SetCell(row, "Bullet", format.Bullet ? 1 : 0); VisioXml.SetCell(row, "Flags", format.Direction == ParagraphDirection.RightToLeft ? 1 : 0);
        }
        var text = new XElement(_v + "Text", new XAttribute(XNamespace.Xml + "space", "preserve"));
        var boundaries = shape.TextSpans.SelectMany(s => new[] { s.Start, s.Start + s.Length }).Concat(paragraphIndices.Keys).Append(0).Append(shape.Text.Length).Distinct().Order().ToArray();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var start = boundaries[index]; var end = boundaries[index + 1];
            if (paragraphIndices.TryGetValue(start, out var paragraph)) text.Add(new XElement(_v + "pp", new XAttribute("IX", paragraph)));
            var style = MergeTextStyle(shape, start); var character = style == baseStyle ? 0 : Character(style);
            text.Add(new XElement(_v + "cp", new XAttribute("IX", character))); text.Add(new XText(shape.Text[start..end]));
        }
        if (shape.Text.Length == 0) { text.Add(new XElement(_v + "cp", new XAttribute("IX", 0))); text.Add(new XElement(_v + "pp", new XAttribute("IX", 0))); }
        owner.Add(text);
    }

    private static TextSpan MergeTextStyle(Shape shape, int offset)
    {
        var style = new TextSpan { FontFamily = shape.Style.FontFamily, FontSize = shape.Style.FontSize, Color = shape.Style.TextColor, Bold = shape.Style.Bold, Italic = shape.Style.Italic };
        foreach (var span in shape.TextSpans.Where(s => s.Start <= offset && offset < s.Start + s.Length))
        {
            style.FontFamily = span.FontFamily ?? style.FontFamily; style.FontSize = span.FontSize ?? style.FontSize; style.Color = span.Color ?? style.Color;
            style.Bold = span.Bold ?? style.Bold; style.Italic = span.Italic ?? style.Italic; style.Underline = span.Underline ?? style.Underline; style.StrikeThrough = span.StrikeThrough ?? style.StrikeThrough;
            if (span.BaselineOffset != 0) style.BaselineOffset = span.BaselineOffset;
        }
        return style;
    }
}
