using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Routing;

namespace DrawingSpace.Tests;

public sealed class ConnectorSegmentEditorTests
{
    private static PointD[] Route => [new(0, 0), new(100, 0), new(100, 100), new(200, 100)];
    private static void Orthogonal(IReadOnlyList<PointD> route)
    {
        for (var i = 1; i < route.Count; i++)
        {
            var a = route[i - 1]; var b = route[i];
            Assert.True(Math.Abs(a.X - b.X) < 1e-7 || Math.Abs(a.Y - b.Y) < 1e-7, $"Nonorthogonal leg {a} -> {b}");
            Assert.True(a.Distance(b) > 1e-7);
        }
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(0, -30)]
    [InlineData(1, 25)]
    [InlineData(1, -35)]
    [InlineData(2, 45)]
    [InlineData(2, -20)]
    public void DraggingAnyLegPreservesEndpointsAndOrthogonality(int index, double offset)
    {
        var source = Route; var editor = new ConnectorSegmentEditor(source, index);
        var result = editor.CreateRoute(offset);
        Assert.Equal(source[0], result[0]); Assert.Equal(source[^1], result[^1]); Orthogonal(result);
        Assert.Equal(Route, source);
    }

    [Fact]
    public void InteriorSegmentTranslatesOnlyItsTwoVertices()
    {
        var editor = new ConnectorSegmentEditor(Route, 1);
        Assert.Equal(new[] { new PointD(0, 0), new PointD(125, 0), new PointD(125, 100), new PointD(200, 100) }, editor.CreateRoute(25).ToArray());
    }

    [Fact]
    public void SingleLegCreatesTwoDoglegsAndRetainsEndpointExitDirections()
    {
        var editor = new ConnectorSegmentEditor([new(0, 0), new(100, 0)], 0);
        var route = editor.CreateRoute(40);
        Assert.Equal(new[] { new PointD(0, 0), new PointD(20, 0), new PointD(20, 40), new PointD(80, 40), new PointD(80, 0), new PointD(100, 0) }, route.ToArray());
        Assert.Equal(4, editor.CreateWaypoints(40).Count); Orthogonal(route);
    }

    [Fact]
    public void ReversedSingleLegUsesTheSameAnchoringRules()
    {
        var editor = new ConnectorSegmentEditor([new(100, 100), new(100, 0)], 0);
        var route = editor.CreateRoute(-30);
        Assert.Equal(new PointD(100, 100), route[0]); Assert.Equal(new PointD(100, 0), route[^1]);
        Assert.Contains(new PointD(70, 80), route); Assert.Contains(new PointD(70, 20), route); Orthogonal(route);
    }

    [Fact]
    public void SnapUsesTheSegmentsAbsoluteCoordinateNotThePointerTangentialMotion()
    {
        var editor = new ConnectorSegmentEditor([new(0, 13), new(100, 13)], 0);
        Assert.Equal(16, editor.Offset(new(50, 13), new(83, 29)));
        Assert.Equal(19, editor.Offset(new(50, 13), new(83, 29), 8));
        Assert.Equal(0, editor.Offset(new(50, 13), new(83, 13)));
    }

    [Fact]
    public void EachPreviewUsesTheOriginalRouteAndTheInputIsCopied()
    {
        var input = Route; var original = input.ToArray();
        var editor = new ConnectorSegmentEditor(input, 1); input[1] = new(1000, 1000);
        editor.CreateRoute(20); editor.CreateRoute(70);
        Assert.Equal(new PointD(125, 0), editor.CreateRoute(25)[1]);
        Assert.Equal(original, editor.CreateRoute(0).ToArray());
    }

    [Fact]
    public void DegenerateAndDiagonalSegmentsAreRejected()
    {
        Assert.False(ConnectorSegmentEditor.CanDrag(new(0, 0), new(0, 0)));
        Assert.False(ConnectorSegmentEditor.CanDrag(new(0, 0), new(100, 100)));
        Assert.Throws<ArgumentException>(() => new ConnectorSegmentEditor([new(0, 0), new(100, 100)], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectorSegmentEditor(Route, 3));
    }

    [Fact]
    public void NonFiniteAndOutOfBudgetEditsAreRejected()
    {
        var editor = new ConnectorSegmentEditor(Route, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.CreateRoute(double.NaN));
        Assert.Throws<InvalidOperationException>(() => editor.CreateRoute(2000000));
        Assert.Throws<ArgumentException>(() => new ConnectorSegmentEditor(Enumerable.Range(0, 4095).Select(i => new PointD(i, 0)).ToArray(), 0));
    }

    [Fact]
    public void WaypointTransactionPreservesGlueAndSupportsUndoRedo()
    {
        var source = new Shape { Id = "source", X = 0, Y = 0, Width = 100, Height = 60 };
        var target = new Shape { Id = "target", X = 300, Y = 0, Width = 100, Height = 60 };
        var edge = new Connector { Id = "edge", SourceId = source.Id, TargetId = target.Id, SourcePort = PortSide.East, TargetPort = PortSide.West, Kind = ConnectorKind.Orthogonal };
        var page = new DiagramPage { Shapes = [source, target], Connectors = [edge] };
        var session = new EditorSession(new DiagramDocument { Pages = [page] });
        var router = new OrthogonalRouter(); var originalRoute = router.Route(page, edge);
        var editor = new ConnectorSegmentEditor(originalRoute.Points, 0);
        session.SetWaypoints(edge.Id, editor.CreateWaypoints(80));
        var changedRoute = router.Route(session.Page, session.Page.Connectors[0]);
        Assert.Equal(originalRoute.Points[0], changedRoute.Points[0]);
        Assert.Equal(originalRoute.Points[^1], changedRoute.Points[^1]);
        Assert.Equal("source", session.Page.Connectors[0].SourceId); Assert.Equal("target", session.Page.Connectors[0].TargetId);
        Assert.NotEmpty(session.Page.Connectors[0].Waypoints);
        session.Undo(); Assert.Empty(session.Page.Connectors[0].Waypoints);
        session.Redo(); Assert.NotEmpty(session.Page.Connectors[0].Waypoints);
    }
}
