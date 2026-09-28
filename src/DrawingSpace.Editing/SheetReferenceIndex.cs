using System.Globalization;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

/// <summary>Immutable identity/name indexes owned by one ShapeSheet evaluation scope.</summary>
internal sealed class SheetReferenceIndex
{
    private readonly Dictionary<string, Shape> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Shape> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Instance, string Alias), Shape> _instances = [];
    private readonly Dictionary<string, Shape?> _templates = new(StringComparer.Ordinal);

    public SheetReferenceIndex(DiagramDocument document, DiagramPage page)
    {
        var masters = document.Masters.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var templates = document.Masters.SelectMany(m => m.Children.Prepend(m.Shape).Select(s => (Master: m.Id, Shape: s)))
            .ToDictionary(p => (p.Master, p.Shape.Id), p => p.Shape);
        foreach (var shape in page.Shapes)
        {
            _ids.TryAdd(shape.Id, shape);
            Shape? template = null;
            if (shape.MasterId is { } master && masters.TryGetValue(master, out var definition))
                template = shape.MasterShapeId is { } id ? templates.GetValueOrDefault((master, id)) : definition.Shape;
            _templates[shape.Id] = template;
            Alias(_aliases, shape.Id, shape); Alias(_aliases, shape.VisioId?.ToString(CultureInfo.InvariantCulture), shape);
            _aliases.TryAdd(shape.Name, shape);
            if (shape.MasterInstanceId is not { } instance) continue;
            void Local(string? name, bool sheetAlias)
            {
                if (name is null) return;
                _instances.TryAdd((instance, name.ToUpperInvariant()), shape);
                if (sheetAlias) _instances.TryAdd((instance, "SHEET." + name.ToUpperInvariant()), shape);
            }
            Local(shape.MasterShapeId, true);
            if (template is not null)
            {
                Local(template.Id, true); Local(template.VisioId?.ToString(CultureInfo.InvariantCulture), true); Local(template.Name, false);
            }
        }
    }

    public Shape? Template(Shape shape) => _templates.GetValueOrDefault(shape.Id);
    public Shape? Resolve(Shape shape, string sheet)
    {
        if (sheet.Equals("ParentShape", StringComparison.OrdinalIgnoreCase))
            return shape.FormulaParentId is { } parent ? _ids.GetValueOrDefault(parent)
                : shape.ContainerId is { } container ? _ids.GetValueOrDefault(container) : null;
        var id = sheet.StartsWith("Sheet.", StringComparison.OrdinalIgnoreCase) ? sheet[6..] : sheet;
        // Explicit runtime identities cannot accidentally bind to a similarly named template.
        if (_ids.TryGetValue(id, out var actual)) return actual;
        if (shape.MasterInstanceId is { } instance && _instances.TryGetValue((instance, sheet.ToUpperInvariant()), out var local)) return local;
        return _aliases.GetValueOrDefault(sheet);
    }

    private static void Alias(Dictionary<string, Shape> aliases, string? id, Shape shape)
    {
        if (id is null) return;
        aliases.TryAdd(id, shape); aliases.TryAdd("Sheet." + id, shape);
    }
}
