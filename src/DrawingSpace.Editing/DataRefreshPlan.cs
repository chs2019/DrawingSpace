using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public sealed record DataRefreshIssue(string ShapeId, string Field, string Reason);

internal sealed record DataRefreshTarget(
    Shape Shape, string Name, string Text,
    Dictionary<string, string> Expected,
    ShapeDataBinding? ExpectedBinding,
    Dictionary<string, string> Values, ShapeDataBinding Binding, bool Changed);

/// <summary>A single-use, session/revision-bound preview. Targets are checked before any write.</summary>
public sealed class DataRefreshPlan
{
    internal EditorSession Owner { get; }
    internal DiagramDocument Document { get; }
    internal DiagramPage Page { get; }
    internal long Revision { get; }
    internal int PageShapeCount { get; }
    internal string[]? SelectedIds { get; }
    internal IReadOnlyList<DataRefreshTarget> Targets { get; }
    public int ChangedShapes { get; }
    public int TotalIssues { get; }
    public IReadOnlyList<DataRefreshIssue> Issues { get; }

    internal DataRefreshPlan(EditorSession owner, bool selectedOnly, List<DataRefreshTarget> targets,
        List<DataRefreshIssue> issues, int totalIssues)
    {
        Owner = owner; Document = owner.Document; Page = owner.Page;
        Revision = owner.Revision; PageShapeCount = owner.Page.Shapes.Count;
        SelectedIds = selectedOnly ? owner.Selection.ToArray() : null;
        Targets = targets.AsReadOnly(); ChangedShapes = targets.Count(t => t.Changed);
        Issues = issues.AsReadOnly(); TotalIssues = totalIssues;
    }
}
