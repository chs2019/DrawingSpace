namespace DrawingSpace.Documents;

public sealed partial class Shape
{
    public ShapeDataBinding? DataBinding { get; set; }
    public List<ShapeDataGraphic> DataGraphics { get; set; } = [];
}
