using System.Text;
using System.Xml.Linq;
using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

internal sealed partial class VisioReadContext
{
    private void ReadText(XElement effective, Shape shape)
    {
        var text = VisioXml.Child(effective, "Text"); if (text is null) return;
        var characters = SectionRows(effective, "Character").ToDictionary(r => VisioXml.Id(r, "IX"));
        var paragraphs = SectionRows(effective, "Paragraph").ToDictionary(r => VisioXml.Id(r, "IX"));
        var content = new StringBuilder(); uint character = 0, paragraph = 0; var spanStart = 0; var paragraphStart = 0;
        void CharacterRun()
        {
            if (content.Length <= spanStart) return;
            if (characters.TryGetValue(character, out var row))
            {
                var bits = (int)VisioXml.Number(row, "Style"); var font = VisioXml.Value(row, "Font", shape.Style.FontFamily);
                var family = uint.TryParse(font, out var id) ? _fonts.GetValueOrDefault(id, shape.Style.FontFamily) : font.Trim('"');
                var size = Math.Clamp(VisioXml.Number(row, "Size", shape.Style.FontSize / 96) * 96, 1, 1024);
                var color = ReadColor(VisioXml.Cell(row, "Color"), shape.Style.TextColor);
                var span = new TextSpan { Start = spanStart, Length = content.Length - spanStart };
                span.FontFamily = family != shape.Style.FontFamily ? family : null;
                span.FontSize = Math.Abs(size - shape.Style.FontSize) > 1e-8 ? size : null;
                span.Color = color != shape.Style.TextColor ? color : null;
                span.Bold = ((bits & 1) != 0) != shape.Style.Bold ? (bits & 1) != 0 : null;
                span.Italic = ((bits & 2) != 0) != shape.Style.Italic ? (bits & 2) != 0 : null;
                span.Underline = (bits & 4) != 0 ? true : null; span.StrikeThrough = VisioXml.Boolean(row, "Strikethru") ? true : null;
                var position = (int)VisioXml.Number(row, "Pos"); span.BaselineOffset = position == 1 ? size * .35 : position == 2 ? -size * .2 : 0;
                if (span.FontFamily is not null || span.FontSize is not null || span.Color is not null || span.Bold is not null || span.Italic is not null || span.Underline is not null || span.StrikeThrough is not null || span.BaselineOffset != 0) shape.TextSpans.Add(span);
            }
            spanStart = content.Length;
        }
        void ParagraphRun()
        {
            if (!paragraphs.TryGetValue(paragraph, out var row)) return;
            var spacing = VisioXml.Number(row, "SpLine", -1.24);
            var format = new ParagraphFormat
            {
                Start = paragraphStart,
                Alignment = (int)VisioXml.Number(row, "HorzAlign", 1) switch { 0 => ParagraphAlignment.Left, 2 => ParagraphAlignment.Right, 3 or 4 => ParagraphAlignment.Justify, _ => ParagraphAlignment.Center },
                Direction = VisioXml.Boolean(row, "Flags") ? ParagraphDirection.RightToLeft : ParagraphDirection.Auto,
                LineSpacing = Math.Clamp(spacing < 0 ? -spacing : spacing > 0 ? spacing * 96 / shape.Style.FontSize : 1, .3, 10),
                LeftIndent = VisioXml.Number(row, "IndLeft") * 96, RightIndent = VisioXml.Number(row, "IndRight") * 96,
                FirstLineIndent = VisioXml.Number(row, "IndFirst") * 96, SpaceBefore = VisioXml.Number(row, "SpBefore") * 96, SpaceAfter = VisioXml.Number(row, "SpAfter") * 96,
                Bullet = VisioXml.Number(row, "Bullet") != 0
            };
            shape.Paragraphs.RemoveAll(p => p.Start == paragraphStart); shape.Paragraphs.Add(format);
        }
        foreach (var node in text.Nodes())
        {
            if (node is XText value) content.Append(value.Value);
            else if (node is XElement element)
            {
                if (element.Name.LocalName == "cp") { CharacterRun(); character = VisioXml.Id(element, "IX"); }
                else if (element.Name.LocalName == "pp") { ParagraphRun(); paragraphStart = content.Length; paragraph = VisioXml.Id(element, "IX"); }
                else if (element.Name.LocalName == "fld") content.Append(element.Value);
                else if (element.Name.LocalName is not "tp") content.Append(element.Value);
            }
            if (content.Length > 100000) throw new InvalidDataException("Visio shape text exceeds the editing budget.");
        }
        CharacterRun(); ParagraphRun(); shape.Text = content.ToString();
    }
}
