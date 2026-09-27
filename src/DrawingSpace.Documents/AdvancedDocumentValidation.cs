namespace DrawingSpace.Documents;

/// <summary>Validation for optional semantic extensions, kept separate from the original version-one contract.</summary>
public static class AdvancedDocumentValidation
{
    public static void Validate(DiagramDocument document)
    {
        var shapes = document.Pages.SelectMany(p => p.Shapes).Concat(document.Masters.SelectMany(m => m.Children.Prepend(m.Shape)));
        foreach (var shape in shapes)
        {
            if (shape.Hyperlinks is null || shape.Hyperlinks.Count > 4096 || shape.Hyperlinks.Any(h => h is null || h.Address is null || h.Description is null || h.SubAddress is null || h.Address.Length > 8192 || h.Description.Length > 8192 || h.SubAddress.Length > 8192)) throw new InvalidDataException("Invalid hyperlink metadata.");
            if (!double.IsFinite(shape.TextRotation) || Math.Abs(shape.TextRotation) > 360000) throw new InvalidDataException("Invalid text rotation.");
            if (shape.TextBounds is { } text && (!text.IsFinite || Math.Abs(text.X) > 10000 || Math.Abs(text.Y) > 10000 || text.Width is < 0 or > 10000 || text.Height is < 0 or > 10000)) throw new InvalidDataException("Invalid normalized text bounds.");
            if (!double.IsFinite(shape.CoordinateWidth) || !double.IsFinite(shape.CoordinateHeight) || shape.CoordinateWidth is < 0 or > 1000000 || shape.CoordinateHeight is < 0 or > 1000000) throw new InvalidDataException("Invalid logical coordinate domain.");
        }
        foreach (var page in document.Pages) ValidateCoordinates(page.Shapes, page.Groups);
        foreach (var master in document.Masters)
        {
            if (master.Groups is null || master.Connectors is null || master.Groups.Count > 10000 || master.Connectors.Count > 10000) throw new InvalidDataException("Invalid master collections.");
            var members = master.Children.Prepend(master.Shape).ToArray(); ValidateCoordinates(members, master.Groups);
            var ids = members.Select(s => s.Id).ToHashSet(); var edgeIds = new HashSet<string>();
            foreach (var edge in master.Connectors)
            {
                if (edge is null || string.IsNullOrWhiteSpace(edge.Id) || !edgeIds.Add(edge.Id) || ids.Contains(edge.Id) || edge.SourceId is not null && !ids.Contains(edge.SourceId) || edge.TargetId is not null && !ids.Contains(edge.TargetId)) throw new InvalidDataException("Invalid master connector identity or endpoint.");
                if (!edge.Start.IsFinite || !edge.End.IsFinite || edge.Waypoints is null || edge.Waypoints.Count > 4096 || edge.Waypoints.Any(p => !p.IsFinite) || !double.IsFinite(edge.Width) || edge.Width is < .1 or > 100) throw new InvalidDataException("Invalid master connector geometry.");
            }
        }
    }
    private static void ValidateCoordinates(IReadOnlyCollection<Shape> shapes, IReadOnlyCollection<DiagramGroup> groups)
    {
        var lookup = shapes.ToDictionary(s => s.Id);
        foreach (var shape in shapes)
        {
            var id = shape.FormulaParentId; var seen = new HashSet<string> { shape.Id };
            while (id is not null)
            {
                if (!seen.Add(id) || seen.Count > 64) throw new InvalidDataException("Cyclic or over-deep coordinate frames.");
                if (!lookup.TryGetValue(id, out var parent) || !parent.IsGroupAnchor) throw new InvalidDataException("The coordinate parent must be a group anchor on the same page.");
                id = parent.FormulaParentId;
            }
        }
        foreach (var group in groups)
            if (group is null || group.AnchorShapeId is { } anchor && (!lookup.TryGetValue(anchor, out var shape) || !shape.IsGroupAnchor)) throw new InvalidDataException("Invalid group anchor.");
    }
}
