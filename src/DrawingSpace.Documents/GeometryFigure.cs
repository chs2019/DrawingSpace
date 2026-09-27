namespace DrawingSpace.Documents;

public sealed class GeometryFigure
{
    public bool Filled { get; set; } = true;
    public bool Stroked { get; set; } = true;
    public bool EvenOdd { get; set; }
    public List<GeometrySegment> Segments { get; set; } = [];
    public GeometryFigure Clone() => new() { Filled = Filled, Stroked = Stroked, EvenOdd = EvenOdd, Segments = Segments.Select(s => s.Clone()).ToList() };
}
