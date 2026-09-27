namespace DrawingSpace.Documents;

public sealed class ShapeStyle
{
    public string Fill { get; set; } = "#FFFFFF";
    public string Stroke { get; set; } = "#507EAA";
    public string TextColor { get; set; } = "#253858";
    public double StrokeWidth { get; set; } = 1.5;
    public double FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Open Sans";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Dashed { get; set; }
    public double Opacity { get; set; } = 1;
    public ShapeStyle Clone() => (ShapeStyle)MemberwiseClone();
}
