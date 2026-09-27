using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;

namespace DrawingSpace.Tests;

public sealed class GeometryTests
{
    [Theory]
    [InlineData(0.1)] [InlineData(0.5)] [InlineData(1)] [InlineData(4)] [InlineData(8)]
    public void ViewportRoundTrip(double zoom)
    {
        var viewport = new Viewport { Zoom = zoom, Pan = new(13, -99) };
        var point = new PointD(165, 382);
        Assert.True(point.Distance(viewport.ToWorld(viewport.ToScreen(point))) < 1e-8);
    }
    [Fact] public void ZoomKeepsAnchorStationary()
    {
        var viewport = new Viewport(); var anchor = new PointD(220, 330); var world = viewport.ToWorld(anchor);
        viewport.ZoomAt(3.7, anchor);
        Assert.True(viewport.ToScreen(world).Distance(anchor) < 1e-8);
    }
    [Fact] public void ViewportRejectsNonFiniteValues()
    {
        var viewport = new Viewport { Zoom = double.NaN, Pan = new(double.PositiveInfinity, 1) };
        Assert.Equal(1, viewport.Zoom); Assert.Equal(PointD.Zero, viewport.Pan);
    }
    [Fact] public void RotationRoundTrip()
    {
        var p = new PointD(20, 10); var center = new PointD(3, 4);
        Assert.True(p.Distance(p.Rotate(37, center).Rotate(-37, center)) < 1e-8);
    }
    [Fact] public void DegenerateSegmentDistance() => Assert.Equal(5, PointD.DistanceToSegment(new(3, 4), default, default));
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void ResizePreservesOppositeHandleAtAnyRotation(int handle)
    {
        var original = new Shape { X = 200, Y = 140, Width = 160, Height = 90, Rotation = 32 };
        var shape = original.Clone(); var old = ShapeTransforms.Handles(original); var opposite = (handle + 4) % 8;
        ShapeTransforms.Resize(shape, original, handle, old[handle], old[handle] + new PointD(22, 17), false);
        Assert.True(old[opposite].Distance(ShapeTransforms.Handles(shape)[opposite]) < 1e-7);
        Assert.True(shape.Width >= 16 && shape.Height >= 16);
    }
    [Fact] public void AspectResizeMaintainsRatio()
    {
        var original = new Shape { Width = 160, Height = 80 }; var shape = original.Clone();
        ShapeTransforms.Resize(shape, original, 4, new(160, 80), new(234, 110), true);
        Assert.Equal(2, shape.Width / shape.Height, 8);
    }
    [Fact] public void RotatedBoundsContainCorners()
    {
        var shape = new Shape { Width = 80, Height = 40, Rotation = 90 };
        Assert.Equal(40, shape.WorldBounds.Width, 8); Assert.Equal(80, shape.WorldBounds.Height, 8);
    }
}
