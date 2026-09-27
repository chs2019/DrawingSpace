namespace DrawingSpace.Documents;

/// <summary>Formula and cached XML value are retained independently, including unsupported formulas.</summary>
public sealed class ShapeCell
{
    public string Formula { get; set; } = "";
    public string Value { get; set; } = "0";
    public string Unit { get; set; } = "";
    public bool Inherited { get; set; }
    public ShapeCell Clone() => (ShapeCell)MemberwiseClone();
}
