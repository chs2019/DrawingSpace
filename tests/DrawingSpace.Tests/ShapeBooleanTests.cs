using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Routing;
using DrawingSpace.Skia;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class ShapeBooleanTests
{
    private static Shape Rect(double x, double y, double w = 100, double h = 100) => new() { X = x, Y = y, Width = w, Height = h, Text = "Primary" };

    [Theory]
    [InlineData(ShapeBooleanOperation.Union, true, true, true)]
    [InlineData(ShapeBooleanOperation.Intersect, false, true, false)]
    [InlineData(ShapeBooleanOperation.Subtract, true, false, false)]
    [InlineData(ShapeBooleanOperation.Combine, true, false, true)]
    public void OperationsHaveTheExpectedFilledAreas(ShapeBooleanOperation operation, bool left, bool middle, bool right)
    {
        var a = Rect(0, 0); var b = Rect(50, 0);
        var result = ShapeBooleanGeometry.Compute(new[] { a, b }, operation);
        Assert.NotNull(result);
        using var area = ShapeBooleanGeometry.CreateArea(result);
        Assert.Equal(left, area.Contains(25, 50));
        Assert.Equal(middle, area.Contains(75, 50));
        Assert.Equal(right, area.Contains(125, 50));
        Assert.Equal(0, a.X); Assert.Equal(100, a.Width); Assert.Empty(a.Geometry);
        Assert.Equal(a.Text, result.Text); Assert.NotSame(a.Style, result.Style);
        var document = new DiagramDocument(); document.Pages[0].Shapes.Add(result); DocumentCodec.Validate(document);
    }

    [Fact]
    public void SubtractionRetainsARealHoleAcrossJsonAndVisioRoundTrips()
    {
        var result = ShapeBooleanGeometry.Compute(new[] { Rect(0, 0, 200, 200), Rect(50, 50) }, ShapeBooleanOperation.Subtract)!;
        Assert.Single(result.Geometry);
        Assert.True(ShapeGeometry.Contains(result, new PointD(20, 20), 0));
        Assert.False(ShapeGeometry.Contains(result, new PointD(100, 100), 0));
        var doc = new DiagramDocument(); doc.Pages[0].Shapes.Add(result);
        var loaded = DocumentCodec.Load(DocumentCodec.Save(doc)).Pages[0].Shapes[0];
        Assert.False(ShapeGeometry.Contains(loaded, new PointD(100, 100), 0));
    }

    [Fact]
    public void OperandOrderDeterminesSubtractionAndInheritedText()
    {
        var a = Rect(0, 0); var b = Rect(50, 0); b.Text = "Second"; b.Style.Fill = "#FF0000";
        var result = ShapeBooleanGeometry.Compute(new[] { b, a }, ShapeBooleanOperation.Subtract)!;
        Assert.Equal("Second", result.Text); Assert.Equal("#FF0000", result.Style.Fill);
        using var area = ShapeBooleanGeometry.CreateArea(result);
        Assert.True(area.Contains(125, 50)); Assert.False(area.Contains(25, 50));
    }

    [Fact]
    public void MultipleCuttersAreAllSubtractedFromThePrimaryOperand()
    {
        var result = ShapeBooleanGeometry.Compute(new[] { Rect(0, 0, 300, 100), Rect(30, 0, 50, 100), Rect(200, 0, 50, 100) }, ShapeBooleanOperation.Subtract)!;
        using var area = ShapeBooleanGeometry.CreateArea(result);
        Assert.True(area.Contains(10, 50)); Assert.False(area.Contains(50, 50));
        Assert.True(area.Contains(150, 50)); Assert.False(area.Contains(225, 50)); Assert.True(area.Contains(280, 50));
    }

    [Fact]
    public void EmptyIntersectionAndIdenticalXorReturnNoResultWithoutModifyingInputs()
    {
        var a = Rect(0, 0); var b = Rect(200, 0);
        Assert.Null(ShapeBooleanGeometry.Compute(new[] { a, b }, ShapeBooleanOperation.Intersect));
        Assert.Null(ShapeBooleanGeometry.Compute(new[] { a, a.Clone(true) }, ShapeBooleanOperation.Combine));
        Assert.Empty(a.Geometry); Assert.Empty(b.Geometry);
    }

    [Theory]
    [InlineData(0, 0, false, false)]
    [InlineData(37, .4, true, false)]
    [InlineData(-70, -.7, false, true)]
    public void PathCopiesPreserveAffineEllipseGeometry(double rotation, double shear, bool flipX, bool flipY)
    {
        var source = Rect(100, 80, 160, 100); source.Kind = ShapeKind.Ellipse;
        source.Rotation = rotation; source.ShearX = shear; source.FlipX = flipX; source.FlipY = flipY;
        var copy = ShapeBooleanGeometry.CreatePathCopy(source)!;
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Contains(copy.Geometry[0].Segments, s => s.Verb == GeometryVerb.Quadratic);
        Assert.Equal(0, copy.Rotation); Assert.Equal(0, copy.ShearX);
        using var expected = ShapeBooleanGeometry.CreateArea(source); using var actual = ShapeBooleanGeometry.CreateArea(copy);
        foreach (var local in new[] { new PointD(.5, .5), new(.3, .3), new(.7, .7), new(.01, .01), new(.95, .95), new(-.1, .5), new(1.1, .5) })
        {
            var p = source.WorldMatrix.Map(local);
            Assert.Equal(expected.Contains((float)p.X, (float)p.Y), actual.Contains((float)p.X, (float)p.Y));
        }
    }

    [Fact]
    public void IndependentFilledFiguresAreUnitedRatherThanAccidentallyXored()
    {
        var source = Rect(0, 0);
        source.Geometry = new List<GeometryFigure> { BoxFigure(0, 0, .75, 1), BoxFigure(.25, 0, .75, 1) };
        foreach (var f in source.Geometry) f.EvenOdd = true;
        using var area = ShapeBooleanGeometry.CreateArea(source);
        Assert.True(area.Contains(10, 50)); Assert.True(area.Contains(50, 50)); Assert.True(area.Contains(90, 50));
    }

    [Fact]
    public void StrokeOnlyDetailsDoNotBecomeFilledBooleanOperands()
    {
        var source = Rect(0, 0); var figure = BoxFigure(0, 0, 1, 1); figure.Filled = false; source.Geometry = [figure];
        Assert.Throws<InvalidOperationException>(() => ShapeBooleanGeometry.CreateArea(source));
        Assert.Throws<InvalidOperationException>(() => ShapeBooleanGeometry.CreateArea(new Shape { Kind = ShapeKind.Text }));
        Assert.Throws<InvalidOperationException>(() => ShapeBooleanGeometry.CreateArea(new Shape { ImageData = new byte[] { 1 } }));
    }

    [Fact]
    public void OperandAndOutputBudgetsFailBeforeMutation()
    {
        var a = Rect(0, 0);
        Assert.Throws<ArgumentException>(() => ShapeBooleanGeometry.Compute(Enumerable.Repeat(a, 129).ToArray(), ShapeBooleanOperation.Union));
        Assert.Throws<InvalidOperationException>(() => ShapeBooleanGeometry.Compute(new[] { a, Rect(200000, 0) }, ShapeBooleanOperation.Union));
        Assert.Empty(a.Geometry);
    }

    [Fact]
    public void ReplacementIsOneUndoableTransactionAndKeepsPrimaryIdentity()
    {
        var session = new EditorSession(); var a = Rect(0, 0); var b = Rect(50, 0);
        session.AddShape(a); session.AddShape(b); session.SelectAll();
        var before = DocumentCodec.Save(session.Document);
        var result = ShapeBooleanGeometry.Compute(new[] { a, b }, ShapeBooleanOperation.Union)!;
        var inserted = session.ReplaceShapesWithGeometry(new[] { a.Id, b.Id }, result, "Union shapes");
        Assert.Equal(a.Id, inserted.Id); Assert.Single(session.Page.Shapes); Assert.True(session.Selection.Contains(a.Id));
        Assert.Equal("Union shapes", session.UndoName);
        var after = DocumentCodec.Save(session.Document);
        session.Undo(); Assert.Equal(before, DocumentCodec.Save(session.Document));
        session.Redo(); Assert.Equal(after, DocumentCodec.Save(session.Document));
    }

    [Fact]
    public void ReplacingConnectedShapesPreservesPortsDirectionsAndEdgeMetadata()
    {
        var session = new EditorSession(); var a = Rect(0, 0); var b = Rect(160, 0);
        a.ConnectionPoints.Add(new() { Id = "port", Name = "Exit", Position = new(.3, .4), Direction = new(1, 0) });
        session.AddShape(a); session.AddShape(b);
        var edge = session.Connect(a.Id, b.Id, PortSide.East, PortSide.West);
        edge.SourcePointId = "port"; edge.Text = "Flow"; edge.LabelOffset = new(8, 9); edge.Waypoints = [new(130, 40)];
        session.Notify(ChangeKind.Document);
        var source = ConnectionEndpoints.Resolve(a, edge.SourcePointId, edge.SourcePort, edge.Start, b.Bounds.Center);
        var target = ConnectionEndpoints.Resolve(b, edge.TargetPointId, edge.TargetPort, edge.End, a.Bounds.Center);
        var result = ShapeBooleanGeometry.Compute(new[] { a, b }, ShapeBooleanOperation.Union)!;
        session.ReplaceShapesWithGeometry(new[] { a.Id, b.Id }, result);
        var replacement = session.Page.Shapes[0]; var retained = session.Page.Connectors[0];
        Assert.Equal(replacement.Id, retained.SourceId); Assert.Equal(replacement.Id, retained.TargetId);
        Assert.Equal("Flow", retained.Text); Assert.Equal(new PointD(8, 9), retained.LabelOffset); Assert.Single(retained.Waypoints);
        var newSource = ConnectionEndpoints.Resolve(replacement, retained.SourcePointId, retained.SourcePort, retained.Start, replacement.Bounds.Center);
        var newTarget = ConnectionEndpoints.Resolve(replacement, retained.TargetPointId, retained.TargetPort, retained.End, replacement.Bounds.Center);
        Assert.True(source.Position.Distance(newSource.Position) < 1e-5);
        Assert.True(target.Position.Distance(newTarget.Position) < 1e-5);
        Assert.True(source.Direction.Distance(newSource.Direction) < 1e-5);
        DocumentCodec.Validate(session.Document);
    }

    [Fact]
    public void LockedOrLiveOperandsAndReferencedShapesAreRejectedWithoutChangingTheDocument()
    {
        var session = new EditorSession(); var a = Rect(0, 0); var b = Rect(50, 0);
        session.AddShape(a); session.AddShape(b);
        a.Locked = true; session.Notify(ChangeKind.Document);
        var before = DocumentCodec.Save(session.Document);
        Assert.Throws<InvalidOperationException>(() => session.GetGeometryOperands(new[] { a.Id, b.Id }));
        Assert.Equal(before, DocumentCodec.Save(session.Document));
        a.Locked = false; a.Cells["Width"] = new() { Formula = "GUARD(1 in)" };
        Assert.Throws<InvalidOperationException>(() => session.GetGeometryOperands(new[] { a.Id, b.Id }));
        a.Cells.Clear(); var consumer = Rect(300, 0); consumer.Cells["User.Test"] = new() { Formula = "Sheet." + b.Id + "!Width" };
        session.Page.Shapes.Add(consumer);
        Assert.Throws<InvalidOperationException>(() => session.GetGeometryOperands(new[] { a.Id, b.Id }));
    }

    [Fact]
    public void InvalidReplacementRollsBackAndDoesNotLeaveAnActiveTransaction()
    {
        var session = new EditorSession(); var a = Rect(0, 0); var b = Rect(50, 0); session.AddShape(a); session.AddShape(b);
        var before = DocumentCodec.Save(session.Document);
        var result = ShapeBooleanGeometry.Compute(new[] { a, b }, ShapeBooleanOperation.Union)!; result.Style.Fill = "invalid";
        Assert.Throws<InvalidDataException>(() => session.ReplaceShapesWithGeometry(new[] { a.Id, b.Id }, result));
        Assert.False(session.IsInteracting); Assert.Equal(before, DocumentCodec.Save(session.Document));
    }

    [Fact]
    public void CopyingALiveShapeDoesNotModifyItsFormulaOrMasterBinding()
    {
        var source = Rect(0, 0); source.Cells["Width"] = new() { Formula = "GUARD(1 in)" }; source.MasterId = "original-master";
        var copy = ShapeBooleanGeometry.CreatePathCopy(source)!;
        Assert.Empty(copy.Cells); Assert.Null(copy.MasterId); Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("GUARD(1 in)", source.Cells["Width"].Formula); Assert.Equal("original-master", source.MasterId);
    }

    private static GeometryFigure BoxFigure(double x, double y, double w, double h) => new()
    {
        Filled = true, Stroked = true,
        Segments = [new() { Verb = GeometryVerb.Move, End = new(x, y) }, new() { Verb = GeometryVerb.Line, End = new(x + w, y) },
            new() { Verb = GeometryVerb.Line, End = new(x + w, y + h) }, new() { Verb = GeometryVerb.Line, End = new(x, y + h) }, new() { Verb = GeometryVerb.Close }]
    };
}
