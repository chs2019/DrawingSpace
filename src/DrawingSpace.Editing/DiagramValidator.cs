using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;

namespace DrawingSpace.Editing;

public sealed record DiagramIssue(string ObjectId, string Severity, string Message);

public static class DiagramValidator
{
    public static IReadOnlyList<DiagramIssue> Check(DiagramPage page)
    {
        var issues = new List<DiagramIssue>();
        var router = new OrthogonalRouter();
        foreach (var shape in page.Shapes.Where(s => s.Kind is not ShapeKind.Text and not ShapeKind.Container and not ShapeKind.Annotation))
        {
            var incoming = page.Connectors.Count(c => c.TargetId == shape.Id);
            var outgoing = page.Connectors.Count(c => c.SourceId == shape.Id);
            if (incoming + outgoing == 0) issues.Add(new(shape.Id, "Warning", $"{shape.Name}: no connections."));
            if (shape.Kind == ShapeKind.Decision && outgoing < 2) issues.Add(new(shape.Id, "Warning", $"{shape.Name}: a decision should have at least two outgoing branches."));
            if (shape.Kind == ShapeKind.Decision && page.Connectors.Any(c => c.SourceId == shape.Id && string.IsNullOrWhiteSpace(c.Text))) issues.Add(new(shape.Id, "Info", $"{shape.Name}: label the decision branches."));
            if (!page.Bounds.Contains(shape.WorldBounds)) issues.Add(new(shape.Id, "Warning", $"{shape.Name}: extends outside the page."));
        }
        foreach (var edge in page.Connectors)
        {
            if (edge.SourceId is null || edge.TargetId is null) issues.Add(new(edge.Id, "Info", "Connector has an unglued endpoint."));
            if (edge.Kind == ConnectorKind.Orthogonal && !router.Route(page, edge).IsObstacleFree) issues.Add(new(edge.Id, "Warning", "Connector route may cross a shape; move the shapes farther apart."));
        }
        return issues;
    }
}
