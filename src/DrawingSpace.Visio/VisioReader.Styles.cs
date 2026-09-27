using System.Globalization;
using System.Xml.Linq;
using DrawingSpace.Documents;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Visio;

internal sealed partial class VisioReadContext
{
    private XElement EffectiveShape(XElement local, uint? inheritedMaster, out uint? masterId, out uint? templateId)
    {
        masterId = uint.TryParse(VisioXml.Attribute(local, "Master"), out var ownMaster) ? ownMaster : inheritedMaster;
        templateId = uint.TryParse(VisioXml.Attribute(local, "MasterShape"), out var shapeId) ? shapeId : null;
        XElement? inherited = null;
        if (masterId is { } id && _masterRoots.TryGetValue(id, out var root))
        {
            var requestedTemplate = templateId;
            inherited = requestedTemplate is null ? root : root.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "Shape" && VisioXml.Id(e) == requestedTemplate);
            if (inherited is not null) templateId ??= VisioXml.Id(inherited);
        }
        var combined = VisioXml.Merge(inherited, local);
        foreach (var family in new[] { "LineStyle", "FillStyle", "TextStyle" })
        {
            if (!uint.TryParse(VisioXml.Attribute(combined, family), out var styleId)) continue;
            var style = ResolveStyle(styleId, family, new HashSet<uint>());
            if (style is not null) combined = VisioXml.Merge(style, combined);
        }
        return combined;
    }

    private XElement? ResolveStyle(uint id, string family, HashSet<uint> active)
    {
        if (!active.Add(id) || active.Count > 64) throw new InvalidDataException("Cyclic or over-deep style inheritance.");
        if (!_styles.TryGetValue(id, out var source)) return null;
        var result = new XElement(VisioNamespaces.Main + "Shape");
        if (uint.TryParse(VisioXml.Attribute(source, family), out var parent) && parent != id)
            if (ResolveStyle(parent, family, active) is { } inherited) result = inherited;
        foreach (var cell in VisioXml.Children(source, "Cell"))
        {
            var name = VisioXml.Attribute(cell, "N");
            var keep = family == "LineStyle" ? name.StartsWith("Line", StringComparison.Ordinal) || name.EndsWith("Arrow", StringComparison.Ordinal) || name.EndsWith("ArrowSize", StringComparison.Ordinal)
                : family == "FillStyle" ? name.StartsWith("Fill", StringComparison.Ordinal) || name.StartsWith("Shdw", StringComparison.Ordinal)
                : name.StartsWith("Text", StringComparison.Ordinal) || name.StartsWith("Txt", StringComparison.Ordinal) || name.EndsWith("Margin", StringComparison.Ordinal) || name == "VerticalAlign";
            if (keep) { VisioXml.Cell(result, name)?.Remove(); result.Add(new XElement(cell)); }
        }
        if (family == "TextStyle")
            foreach (var section in VisioXml.Children(source, "Section").Where(s => VisioXml.Attribute(s, "N") is "Character" or "Paragraph" or "Tabs"))
            {
                var old = VisioXml.Children(result, "Section").FirstOrDefault(s => VisioXml.Attribute(s, "N") == VisioXml.Attribute(section, "N"));
                var merged = VisioXml.Merge(old, section); if (old is null) result.Add(merged); else old.ReplaceWith(merged);
            }
        return result;
    }

    private ShapeStyle ReadStyle(XElement effective)
    {
        var character = SectionRows(effective, "Character").FirstOrDefault(r => VisioXml.Id(r, "IX") == 0);
        var bits = (int)VisioXml.Number(character, "Style");
        var font = VisioXml.Value(character, "Font", "0");
        return new()
        {
            Fill = VisioXml.Number(effective, "FillPattern", 1) == 0 ? "#00FFFFFF" : ReadColor(VisioXml.Cell(effective, "FillForegnd"), "#FFFFFF"),
            Stroke = ReadColor(VisioXml.Cell(effective, "LineColor"), "#000000"),
            StrokeWidth = VisioXml.Number(effective, "LinePattern", 1) == 0 ? 0 : Math.Clamp(VisioXml.Number(effective, "LineWeight", .010416666666666666) * 96, 0, 100),
            TextColor = ReadColor(VisioXml.Cell(character, "Color"), "#000000"),
            FontFamily = uint.TryParse(font, out var id) ? _fonts.GetValueOrDefault(id, "Arial") : font.Trim('"'),
            FontSize = Math.Clamp(VisioXml.Number(character, "Size", .1388888888888889) * 96, 1, 1024),
            Bold = (bits & 1) != 0, Italic = (bits & 2) != 0,
            Dashed = VisioXml.Number(effective, "LinePattern", 1) > 1,
            Opacity = 1 - Math.Clamp(VisioXml.Number(effective, "FillForegndTrans", 0), 0, 1)
        };
    }

    private string ReadColor(XElement? cell, string fallback)
    {
        if (cell is null) return fallback;
        var value = VisioXml.Attribute(cell, "V");
        if (value.StartsWith('#')) return NormalizeColor(value, fallback);
        var formula = VisioXml.Attribute(cell, "F");
        if (formula.StartsWith("RGB", StringComparison.OrdinalIgnoreCase))
        {
            var evaluated = new FormulaEngine().Evaluate(formula);
            if (evaluated.IsNumeric) { var rgb = (int)evaluated.Numeric; return $"#{rgb & 255:X2}{(rgb >> 8) & 255:X2}{(rgb >> 16) & 255:X2}"; }
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            if (_colors.TryGetValue(index, out var color)) return color;
            return index switch { 0 => "#000000", 1 => "#FFFFFF", 2 => "#FF0000", 3 => "#00FF00", 4 => "#0000FF", 5 => "#FFFF00", 6 => "#FF00FF", 7 => "#00FFFF", 8 => "#800000", 9 => "#008000", 10 => "#000080", _ => fallback };
        }
        return fallback;
    }

    private static IEnumerable<XElement> SectionRows(XElement? owner, string sectionName)
        => VisioXml.Children(owner, "Section").Where(s => VisioXml.Attribute(s, "N") == sectionName).SelectMany(s => VisioXml.Children(s, "Row")).Where(r => VisioXml.Attribute(r, "Del") != "1");

    private void ReadLayers(XElement? sheet, DiagramPage page)
    {
        foreach (var row in SectionRows(sheet, "Layer"))
        {
            var id = "visio-layer-" + VisioXml.Id(row, "IX");
            page.Layers.Add(new()
            {
                Id = id, Name = VisioXml.Value(row, "Name", id), Visible = VisioXml.Boolean(row, "Visible", true),
                Locked = VisioXml.Boolean(row, "Lock"), Printable = VisioXml.Boolean(row, "Print", true)
            });
        }
    }

    private static string Layer(XElement effective, DiagramPage page)
    {
        var index = VisioXml.Value(effective, "LayerMember").Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var id = "visio-layer-" + index;
        return page.Layers.Any(l => l.Id == id) ? id : page.Layers[0].Id;
    }
}
