using System.Globalization;
using System.Xml.Linq;
using DrawingSpace.Documents;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Visio;

internal static class VisioXml
{
    internal static IEnumerable<XElement> Children(XContainer? parent, string name) => parent?.Elements().Where(e => e.Name.LocalName == name) ?? [];
    internal static XElement? Child(XContainer? parent, string name) => Children(parent, name).FirstOrDefault();
    internal static string Attribute(XElement? element, string name, string fallback = "") => (string?)element?.Attribute(name) ?? fallback;
    internal static uint Id(XElement element, string name = "ID", uint fallback = 0) => uint.TryParse(Attribute(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    internal static XElement? Cell(XElement? owner, string name) => Children(owner, "Cell").FirstOrDefault(c => Attribute(c, "N").Equals(name, StringComparison.OrdinalIgnoreCase));
    internal static string Value(XElement? owner, string name, string fallback = "") => Attribute(Cell(owner, name), "V", fallback);
    internal static double Number(XElement? owner, string name, double fallback = 0)
    {
        var cell = Cell(owner, name); if (cell is null) return fallback;
        if (double.TryParse(Attribute(cell, "V"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)) return value;
        var formula = Attribute(cell, "F");
        if (formula.Length > 0 && formula is not "Inh" and not "No Formula")
        {
            var result = new FormulaEngine().Evaluate(formula);
            if (result.IsNumeric) return result.Numeric;
        }
        return fallback;
    }
    internal static bool Boolean(XElement? owner, string name, bool fallback = false) => Number(owner, name, fallback ? 1 : 0) != 0;
    internal static string Format(double number) => number.ToString("G17", CultureInfo.InvariantCulture);

    internal static Dictionary<string, ShapeCell> ReadCells(XElement? owner)
    {
        var result = new Dictionary<string, ShapeCell>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, XElement cell)
        {
            if (name.Length > 256 || result.Count >= 4096) throw new InvalidDataException("A ShapeSheet exceeds the supported cell budget.");
            result[name] = new() { Formula = Attribute(cell, "F"), Value = Attribute(cell, "V", "0"), Unit = Attribute(cell, "U"), Inherited = Attribute(cell, "F") == "Inh" };
        }
        foreach (var cell in Children(owner, "Cell")) Add(Attribute(cell, "N"), cell);
        foreach (var section in Children(owner, "Section"))
        {
            var name = Attribute(section, "N"); var sectionIndex = Id(section, "IX");
            foreach (var cell in Children(section, "Cell")) Add(name + (name == "Geometry" ? (sectionIndex + 1).ToString(CultureInfo.InvariantCulture) : "") + "." + Attribute(cell, "N"), cell);
            foreach (var row in Children(section, "Row"))
            {
                var rowIndex = Id(row, "IX"); var rowName = Attribute(row, "N", rowIndex.ToString(CultureInfo.InvariantCulture));
                foreach (var cell in Children(row, "Cell"))
                {
                    var cellName = Attribute(cell, "N");
                    var fullName = name switch
                    {
                        "Geometry" => $"Geometry{sectionIndex + 1}.{cellName}{rowIndex}",
                        "Connection" => $"Connections.{cellName}{rowIndex + 1}",
                        "Character" => "Char." + cellName + (rowIndex == 0 ? "" : $"[{rowIndex + 1}]"),
                        "Paragraph" => "Para." + cellName + (rowIndex == 0 ? "" : $"[{rowIndex + 1}]"),
                        "User" => "User." + rowName + (cellName == "Value" ? "" : "." + cellName),
                        "Property" => "Prop." + rowName + (cellName == "Value" ? "" : "." + cellName),
                        _ => name + "." + rowName + "." + cellName
                    };
                    Add(fullName, cell);
                }
            }
        }
        return result;
    }

    internal static XElement Merge(XElement? inherited, XElement local)
    {
        if (inherited is null) return new(local);
        var result = new XElement(inherited); result.Name = local.Name;
        foreach (var attribute in local.Attributes()) result.SetAttributeValue(attribute.Name, attribute.Value);
        foreach (var element in local.Elements())
        {
            var name = element.Name.LocalName;
            if (name is "Cell" or "Section" or "Row")
            {
                var key = name == "Cell" ? Attribute(element, "N") : name == "Section" ? Attribute(element, "N") + ":" + Attribute(element, "IX", "0") : Attribute(element, "N", "#" + Attribute(element, "IX", "0"));
                var existing = result.Elements().FirstOrDefault(e => e.Name.LocalName == name && (name == "Cell" ? Attribute(e, "N") : name == "Section" ? Attribute(e, "N") + ":" + Attribute(e, "IX", "0") : Attribute(e, "N", "#" + Attribute(e, "IX", "0"))) == key);
                if (Attribute(element, "Del") == "1") { existing?.Remove(); continue; }
                if (name == "Cell" && Attribute(element, "F") == "Inh" && existing is not null) continue;
                var merged = name == "Cell" ? new XElement(element) : Merge(existing, element);
                if (existing is null) result.Add(merged); else existing.ReplaceWith(merged);
            }
            else
            {
                foreach (var existing in result.Elements().Where(e => e.Name.LocalName == name).ToArray()) existing.Remove();
                result.Add(new XElement(element));
            }
        }
        return result;
    }

    internal static void SetCell(XElement owner, string name, double value, string? formula = null, string? unit = null) => SetCell(owner, name, Format(value), formula, unit);
    internal static void SetCell(XElement owner, string name, string value, string? formula = null, string? unit = null)
    {
        var cell = Cell(owner, name);
        if (cell is null) { cell = new XElement(VisioNamespaces.Main + "Cell", new XAttribute("N", name)); owner.AddFirst(cell); }
        cell.SetAttributeValue("V", value); cell.SetAttributeValue("E", null);
        if (formula is not null) cell.SetAttributeValue("F", formula.Length == 0 ? null : formula);
        if (unit is not null) cell.SetAttributeValue("U", unit.Length == 0 ? null : unit);
    }
}
