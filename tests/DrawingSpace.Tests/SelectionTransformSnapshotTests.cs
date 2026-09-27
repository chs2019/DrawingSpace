using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;

namespace DrawingSpace.Tests;

public sealed class SelectionTransformSnapshotTests
{
    private static Shape Make(string id, double x = 20, double y = 40) => new()
    { Id = id, X = x, Y = y, Width = 120, Height = 60, Text = "" };

    private static void SameMatrix(MatrixD expected, MatrixD actual)
    {
        foreach (var p in new[] { PointD.Zero, new PointD(1, 0), new PointD(0, 1), new PointD(1, 1), new PointD(.23, .67) })
            Assert.InRange(expected.Map(p).Distance(actual.Map(p)), 0, 1e-7);
    }

    [Theory]
    [InlineData(0, 0, false, false)]
    [InlineData(37, .6, false, false)]
    [InlineData(-91, -.8, true, false)]
    [InlineData(123, .25, false, true)]
    [InlineData(180, 1.4, true, true)]
    public void AffineProjectionPreservesEveryNormalizedPoint(double rotation, double shear, bool flipX, bool flipY)
    {
        var a = Make("a"); a.Rotation = rotation; a.ShearX = shear; a.FlipX = flipX; a.FlipY = flipY;
        var b = Make("b", 250, 160);
        var page = new DiagramPage { Shapes = [a, b] };
        var originalA = a.WorldMatrix; var originalB = b.WorldMatrix;
        var snapshot = SelectionTransformSnapshot.Capture(page, [a, b]);
        var transform = MatrixD.Translation(55, -17) * MatrixD.Rotation(23) * MatrixD.Scale(1.7, .8);
        snapshot.Apply(transform);
        SameMatrix(transform * originalA, a.WorldMatrix);
        SameMatrix(transform * originalB, b.WorldMatrix);
    }

    [Fact]
    public void RepeatedPreviewsAreAbsoluteAndReturningToIdentityRestoresOriginalRepresentation()
    {
        var a = Make("a"); a.FlipX = true; a.ShearX = .4; a.Rotation = 42;
        var page = new DiagramPage { Shapes = [a] };
        var original = a.Clone(); var snapshot = SelectionTransformSnapshot.Capture(page, [a]);
        for (var i = 0; i < 80; i++) snapshot.Apply(MatrixD.Around(new PointD(150, 100), MatrixD.Rotation(i)));
        var final = MatrixD.Translation(17, 23) * MatrixD.Scale(1.25, .75);
        snapshot.Apply(final); SameMatrix(final * original.WorldMatrix, a.WorldMatrix);
        snapshot.Apply(MatrixD.Identity);
        Assert.Equal(original.X, a.X); Assert.Equal(original.Y, a.Y);
        Assert.Equal(original.Rotation, a.Rotation); Assert.Equal(original.FlipX, a.FlipX);
        Assert.Equal(original.FlipY, a.FlipY); Assert.Equal(original.ShearX, a.ShearX);
    }

    [Fact]
    public void GeometryImagesTextAndMasterIdentitiesAreNotCopiedOrReplaced()
    {
        var a = Make("a"); a.MasterId = "master"; a.MasterInstanceId = "instance";
        a.ImageData = [1, 2, 3]; a.Text = "Rich text";
        var image = a.ImageData; var geometry = a.Geometry; var spans = a.TextSpans; var cells = a.Cells;
        var snapshot = SelectionTransformSnapshot.Capture(new DiagramPage { Shapes = [a] }, [a]);
        snapshot.Apply(MatrixD.Scale(2, 2));
        Assert.Same(image, a.ImageData); Assert.Same(geometry, a.Geometry);
        Assert.Same(spans, a.TextSpans); Assert.Same(cells, a.Cells);
        Assert.Equal("master", a.MasterId); Assert.Equal("instance", a.MasterInstanceId); Assert.Equal("Rich text", a.Text);
    }

    [Fact]
    public void ContainerAndFormulaFrameDescendantsAreTransitiveAndDeduplicated()
    {
        var parent = Make("parent"); parent.Container = new();
        var child = Make("child"); child.ContainerId = parent.Id; child.FormulaParentId = parent.Id;
        var grandchild = Make("grandchild"); grandchild.FormulaParentId = child.Id;
        var page = new DiagramPage { Shapes = [parent, child, grandchild] };
        var snapshot = SelectionTransformSnapshot.Capture(page, [parent, child]);
        Assert.Equal(3, snapshot.ShapeCount);
        snapshot.Apply(MatrixD.Translation(30, 50));
        Assert.Equal(50, grandchild.X); Assert.Equal(90, child.Y);
    }

    [Fact]
    public void LockedDescendantRejectsCaptureInsteadOfPartiallyTransformingContainer()
    {
        var parent = Make("parent"); parent.Container = new();
        var child = Make("child"); child.ContainerId = parent.Id; child.Locked = true;
        var page = new DiagramPage { Shapes = [parent, child] };
        Assert.Throws<InvalidOperationException>(() => SelectionTransformSnapshot.Capture(page, [parent]));
        Assert.Equal(20, parent.X);
    }

    [Fact]
    public void MissingLaterObjectRejectsApplyBeforeWritingEarlierObjects()
    {
        var a = Make("a"); var b = Make("b");
        var page = new DiagramPage { Shapes = [a, b] };
        var snapshot = SelectionTransformSnapshot.Capture(page, [a, b]);
        page.Shapes.Remove(b);
        Assert.Throws<InvalidOperationException>(() => snapshot.Apply(MatrixD.Translation(100, 100)));
        Assert.Equal(20, a.X); Assert.Equal(40, a.Y);
    }

    [Fact]
    public void InvalidSmallDescendantDoesNotLeavePartialGeometryWrites()
    {
        var a = Make("a"); var b = Make("b"); b.Width = b.Height = 1;
        var page = new DiagramPage { Shapes = [a, b] };
        var snapshot = SelectionTransformSnapshot.Capture(page, [a, b]);
        Assert.Throws<InvalidOperationException>(() => snapshot.Apply(MatrixD.Scale(.5, .5)));
        Assert.Equal(120, a.Width); Assert.Equal(60, a.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SingularAndNonFiniteTransformsAreRejected(double scale)
    {
        var a = Make("a"); var snapshot = SelectionTransformSnapshot.Capture(new DiagramPage { Shapes = [a] }, [a]);
        Assert.Throws<ArgumentException>(() => snapshot.Apply(MatrixD.Scale(scale, 1)));
        Assert.Equal(120, a.Width);
    }

    [Fact]
    public void InternalConnectorRoutesAndLabelVectorsTransformOnce()
    {
        var a = Make("a"); var b = Make("b", 260, 100);
        var edge = new Connector { Id = "edge", SourceId = a.Id, TargetId = b.Id, Start = new(140, 70), End = new(260, 130), Waypoints = [new(200, 70), new(200, 130)], LabelOffset = new(5, 7) };
        var page = new DiagramPage { Shapes = [a, b], Connectors = [edge] };
        var snapshot = SelectionTransformSnapshot.Capture(page, [a, b], [edge]);
        Assert.Equal(1, snapshot.ConnectorCount);
        var transform = MatrixD.Translation(100, 50) * MatrixD.Rotation(90);
        snapshot.Apply(transform);
        Assert.InRange(edge.Waypoints[0].Distance(transform.Map(new PointD(200, 70))), 0, 1e-8);
        Assert.InRange(edge.LabelOffset.Distance(new PointD(-7, 5)), 0, 1e-8);
        Assert.Equal("a", edge.SourceId); Assert.Equal("b", edge.TargetId);
    }

    [Fact]
    public void ExplicitConnectorKeepsAnUnselectedAttachedEndpointPinned()
    {
        var a = Make("a"); var b = Make("b", 260, 100);
        var edge = new Connector { SourceId = a.Id, TargetId = b.Id, Start = new(140, 70), End = new(260, 130), Waypoints = [new(200, 70)] };
        var page = new DiagramPage { Shapes = [a, b], Connectors = [edge] };
        var snapshot = SelectionTransformSnapshot.Capture(page, [a], [edge]);
        snapshot.Apply(MatrixD.Translation(30, 40));
        Assert.Equal(new PointD(260, 130), edge.End); Assert.Equal(new PointD(170, 110), edge.Start);
        Assert.Equal(260, b.X); Assert.Equal(new PointD(230, 110), edge.Waypoints[0]);
    }

    [Fact]
    public void RotateSelectionIsOneUndoableTransaction()
    {
        var a = Make("a"); var b = Make("b", 260, 100);
        var session = new EditorSession(new DiagramDocument { Pages = [new DiagramPage { Shapes = [a, b] }] });
        session.SelectAll(); var before = DocumentCodec.Save(session.Document);
        session.RotateSelection(90); var after = DocumentCodec.Save(session.Document);
        Assert.NotEqual(before, after); Assert.Equal("Rotate selection", session.UndoName);
        session.Undo(); Assert.Equal(before, DocumentCodec.Save(session.Document));
        session.Redo(); Assert.Equal(after, DocumentCodec.Save(session.Document));
    }

    [Fact]
    public void ResizeSelectionAnchorsTheOppositeEdge()
    {
        var a = Make("a", 10, 20); a.Width = 100;
        var b = Make("b", 210, 20); b.Width = 100;
        var session = new EditorSession(new DiagramDocument { Pages = [new DiagramPage { Shapes = [a, b] }] });
        session.SelectAll(); session.ResizeSelection(3, new(310, 50), new(610, 50));
        Assert.Equal(10, a.X, 7); Assert.Equal(200, a.Width, 7);
        Assert.Equal(410, b.X, 7); Assert.Equal(60, a.Height, 7);
    }

    [Fact]
    public void CancelRestoresTheWholeInteractiveSelection()
    {
        var a = Make("a"); var b = Make("b", 260, 100);
        var session = new EditorSession(new DiagramDocument { Pages = [new DiagramPage { Shapes = [a, b] }] });
        session.SelectAll(); var before = DocumentCodec.Save(session.Document); var snapshot = session.CaptureSelectionTransform();
        session.Begin("Resize selection"); snapshot.Apply(MatrixD.Scale(1.3, .8)); session.Preview();
        snapshot.Apply(MatrixD.Scale(1.5, .9)); session.Preview(); session.Cancel();
        Assert.Equal(before, DocumentCodec.Save(session.Document)); Assert.False(session.CanUndo);
    }

    [Fact]
    public void NoOpRotationDoesNotCreateHistoryOrNormalizeReflections()
    {
        var a = Make("a"); a.FlipX = true; a.Rotation = 23;
        var session = new EditorSession(new DiagramDocument { Pages = [new DiagramPage { Shapes = [a] }] });
        session.SelectAll(); var before = DocumentCodec.Save(session.Document);
        session.RotateSelection(0);
        Assert.Equal(before, DocumentCodec.Save(session.Document)); Assert.False(session.CanUndo);
    }
}
