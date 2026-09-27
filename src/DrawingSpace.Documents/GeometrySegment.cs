using DrawingSpace.Core;

namespace DrawingSpace.Documents;

public enum GeometryVerb { Move, Line, Quadratic, Cubic, Arc, Close }
/// <summary>Coordinates are normalized to the shape bounds, and therefore survive resizing.</summary>
public sealed class GeometrySegment
{
    public GeometryVerb Verb { get; set; }
    public PointD End { get; set; }
    public PointD Control1 { get; set; }
    public PointD Control2 { get; set; }
    public PointD Radius { get; set; }
    public double Rotation { get; set; }
    public bool LargeArc { get; set; }
    public bool Clockwise { get; set; }
    public GeometrySegment Clone() => (GeometrySegment)MemberwiseClone();
}
