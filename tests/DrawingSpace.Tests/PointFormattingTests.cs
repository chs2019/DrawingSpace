using System.Globalization;
using DrawingSpace.Core;

namespace DrawingSpace.Tests;

public sealed class PointFormattingTests
{
    [Theory]
    [InlineData(0, 0, "(0, 0)")]
    [InlineData(3, 4, "(3, 4)")]
    [InlineData(-1.25, 2.5, "(-1.25, 2.5)")]
    public void CoordinateFormattingDoesNotRecursivelyVisitNormalized(double x, double y, string expected)
    {
        var point = new PointD(x, y);
        Assert.Equal(expected, point.ToString());
        Assert.Equal(expected, $"{point}");
        Assert.True(point.ToString().Length < 100);
    }

    [Fact]
    public void ExplicitCultureAndFormatAreHonoredWithoutRecursion()
    {
        IFormattable point = new PointD(1.25, -2.5);
        Assert.Equal("(1.25, -2.50)", point.ToString("F2", CultureInfo.InvariantCulture));
        Assert.Equal("(1,25, -2,50)", point.ToString("F2", CultureInfo.GetCultureInfo("pl-PL")));
    }

    [Fact]
    public void NonFiniteDiagnosticCoordinatesCanBePrintedSafely()
    {
        Assert.Equal("(NaN, Infinity)", new PointD(double.NaN, double.PositiveInfinity).ToString());
        Assert.Equal("(-Infinity, 0)", new PointD(double.NegativeInfinity, 0).ToString());
    }
}
