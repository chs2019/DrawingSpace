using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Skia;
using DrawingSpace.Visio;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class ShapeBooleanInterchangeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VsdxRoundTripPreservesSubtractionHoleWithoutRelyingOnNativeMetadata(bool includeMetadata)
    {
        var outer = new Shape { X = 0, Y = 0, Width = 200, Height = 200 };
        var inner = new Shape { X = 50, Y = 50, Width = 100, Height = 100 };
        var result = ShapeBooleanGeometry.Compute(new[] { outer, inner }, ShapeBooleanOperation.Subtract);
        Assert.NotNull(result);
        var document = new DiagramDocument(); document.Pages[0].Shapes.Add(result);
        var written = VisioWriter.Write(document, new()
        {
            IncludeDrawingSpaceMetadata = includeMetadata,
            PreserveOriginalWhenUnmodified = false
        });
        Assert.False(written.OriginalBytesPreserved);
        var imported = VisioReader.Read(written.Bytes);
        DocumentCodec.Validate(imported.Document);
        var shape = Assert.Single(Assert.Single(imported.Document.Pages).Shapes);
        Assert.True(ShapeGeometry.Contains(shape, new PointD(20, 20), 0));
        Assert.False(ShapeGeometry.Contains(shape, new PointD(100, 100), 0));
        Assert.True(ShapeGeometry.Contains(shape, new PointD(180, 180), 0));
    }
}
