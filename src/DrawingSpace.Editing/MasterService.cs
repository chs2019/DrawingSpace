using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public static partial class MasterService
{
    private sealed record Property(string Name, Func<Shape, object?> Get, Action<Shape, Shape> Copy);
    private static readonly Property[] Properties =
    [
        new("Kind", s => s.Kind, (a,b) => a.Kind = b.Kind), new("Text", s => s.Text, (a,b) => { a.Text = b.Text; a.TextSpans = b.TextSpans.Select(t => t.Clone()).ToList(); a.Paragraphs = b.Paragraphs.Select(p => p.Clone()).ToList(); a.TextBounds = b.TextBounds; a.TextRotation = b.TextRotation; }),
        new("Width", s => s.Width, (a,b) => a.Width = b.Width), new("Height", s => s.Height, (a,b) => a.Height = b.Height),
        new("Style.Fill", s => s.Style.Fill, (a,b) => a.Style.Fill = b.Style.Fill), new("Style.Stroke", s => s.Style.Stroke, (a,b) => a.Style.Stroke = b.Style.Stroke),
        new("Style.StrokeWidth", s => s.Style.StrokeWidth, (a,b) => a.Style.StrokeWidth = b.Style.StrokeWidth), new("Style.TextColor", s => s.Style.TextColor, (a,b) => a.Style.TextColor = b.Style.TextColor),
        new("Style.FontFamily", s => s.Style.FontFamily, (a,b) => a.Style.FontFamily = b.Style.FontFamily), new("Style.FontSize", s => s.Style.FontSize, (a,b) => a.Style.FontSize = b.Style.FontSize),
        new("Style.Bold", s => s.Style.Bold, (a,b) => a.Style.Bold = b.Style.Bold), new("Style.Italic", s => s.Style.Italic, (a,b) => a.Style.Italic = b.Style.Italic),
        new("Style.Opacity", s => s.Style.Opacity, (a,b) => a.Style.Opacity = b.Style.Opacity), new("Style.Dashed", s => s.Style.Dashed, (a,b) => a.Style.Dashed = b.Style.Dashed)
    ];
    public static IReadOnlyList<string> InheritableProperties => Properties.Select(p => p.Name).Append("Geometry").Append("ConnectionPoints").ToArray();
    public static Shape? Template(DiagramDocument document, Shape instance)
    {
        var master = document.Masters.FirstOrDefault(m => m.Id == instance.MasterId);
        if (master is null) return null;
        return instance.MasterShapeId is null || instance.MasterShapeId == master.Shape.Id ? master.Shape : master.Children.FirstOrDefault(s => s.Id == instance.MasterShapeId);
    }
    public static void CaptureLocalOverrides(DiagramDocument before, DiagramDocument current)
    {
        if (current.Masters.Count == 0) return;
        var previous = before.Pages.SelectMany(p => p.Shapes).ToDictionary(s => s.Id);
        foreach (var instance in current.Pages.SelectMany(p => p.Shapes).Where(s => s.MasterId is not null))
        {
            if (!previous.TryGetValue(instance.Id, out var old) || old.MasterId != instance.MasterId) continue;
            foreach (var property in Properties)
                if ((property.Name == "Text" ? !ShapeResourceEquality.Text(old, instance) : !Equals(property.Get(old), property.Get(instance))) && !instance.LocalOverrides.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) instance.LocalOverrides.Add(property.Name);
        }
    }
    public static void Refresh(DiagramDocument document)
    {
        if (document.Masters.Count == 0) return;
        var masters = document.Masters.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var templates = document.Masters.SelectMany(m => m.Children.Prepend(m.Shape).Select(s => (Master: m.Id, Shape: s)))
            .ToDictionary(p => (p.Master, p.Shape.Id), p => p.Shape);
        foreach (var instance in document.Pages.SelectMany(p => p.Shapes))
        {
            if (instance.MasterId is not { } masterId || !masters.TryGetValue(masterId, out var master)) continue;
            var template = instance.MasterShapeId is { } shapeId ? templates.GetValueOrDefault((masterId, shapeId)) : master.Shape;
            if (template is null) continue;
            var center = instance.Bounds.Center;
            foreach (var property in Properties)
                if (!(instance.UsesVisioCoordinates && property.Name is "Width" or "Height")
                    && !instance.LocalOverrides.Contains(property.Name, StringComparer.OrdinalIgnoreCase)
                    && (property.Name == "Text" ? !ShapeResourceEquality.Text(instance, template) : !Equals(property.Get(instance), property.Get(template))))
                    property.Copy(instance, template);
            instance.X = center.X - instance.Width / 2; instance.Y = center.Y - instance.Height / 2;
            if (!instance.LocalOverrides.Contains("Geometry", StringComparer.OrdinalIgnoreCase) && !ShapeResourceEquality.Geometry(instance, template)) instance.Geometry = template.Geometry.Select(g => g.Clone()).ToList();
            if (!instance.LocalOverrides.Contains("ConnectionPoints", StringComparer.OrdinalIgnoreCase) && !ShapeResourceEquality.Ports(instance, template)) instance.ConnectionPoints = template.ConnectionPoints.Select(p => p.Clone()).ToList();
        }
    }
    public static DiagramMaster Create(Shape source, string name)
    {
        var template = source.Clone(true); template.X = 0; template.Y = 0; template.GroupId = null; template.ContainerId = null;
        template.MasterId = null; template.MasterShapeId = null; template.LocalOverrides.Clear(); template.Threads.Clear(); template.Comments.Clear();
        return new() { Name = name, Shape = template };
    }
    public static IReadOnlyList<Shape> Instantiate(DiagramMaster master, PointD position)
        => InstantiateBundle(master, position).Shapes;
}
