using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

/// <summary>One isolated master instance, including its internal glue and nested groups.</summary>
public sealed record MasterBundle(
    string InstanceId,
    IReadOnlyList<Shape> Shapes,
    IReadOnlyList<Connector> Connectors,
    IReadOnlyList<DiagramGroup> Groups);
