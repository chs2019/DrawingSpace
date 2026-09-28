using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Skia;
using DrawingSpace.Visio;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class CompoundFillRuleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeMetadataPreservesBothFillRulesAndAllSubpathClosures(bool evenOdd)
    {
        var shape = Compound(evenOdd); var document = new DiagramDocument(); document.Pages[0].Shapes.Add(shape);
        var bytes = VisioWriter.Write(document).Bytes;
        var loaded = Assert.Single(VisioReader.Read(bytes).Document.Pages[0].Shapes);
        Assert.Equal(evenOdd, Assert.Single(loaded.Geometry).EvenOdd);
        Assert.Equal(!evenOdd, ShapeGeometry.Contains(loaded, new PointD(100, 100), 0));
        Assert.Equal(2, loaded.Geometry[0].Segments.Count(s => s.Verb == GeometryVerb.Close));
    }

    [Fact]
    public void UnannotatedVisioContoursUseAlternateFillingAndReportNonzeroExportRisk()
    {
        var document = new DiagramDocument(); document.Pages[0].Shapes.Add(Compound(false));
        var written = VisioWriter.Write(document, new() { IncludeDrawingSpaceMetadata = false });
        Assert.Contains(written.Diagnostics, d => d.Code == "NonZeroFillRule");
        var loaded = Assert.Single(VisioReader.Read(written.Bytes).Document.Pages[0].Shapes);
        Assert.True(Assert.Single(loaded.Geometry).EvenOdd);
        Assert.False(ShapeGeometry.Contains(loaded, new PointD(100, 100), 0));
    }

    private static Shape Compound(bool evenOdd)
    {
        var figure = new GeometryFigure { Filled = true, Stroked = true, EvenOdd = evenOdd };
        void Rect(double x, double y, double w, double h)
        {
            figure.Segments.AddRange(new GeometrySegment[]
            {
                new() { Verb = GeometryVerb.Move, End = new(x, y) },
                new() { Verb = GeometryVerb.Line, End = new(x + w, y) },
                new() { Verb = GeometryVerb.Line, End = new(x + w, y + h) },
                new() { Verb = GeometryVerb.Line, End = new(x, y + h) },
                new() { Verb = GeometryVerb.Close }
            });
        }
        Rect(0, 0, 1, 1); Rect(.25, .25, .5, .5);
        return new() { X = 0, Y = 0, Width = 200, Height = 200, Geometry = [figure] };
    }
}
