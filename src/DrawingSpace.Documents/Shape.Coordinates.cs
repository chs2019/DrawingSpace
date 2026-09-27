namespace DrawingSpace.Documents;

public sealed partial class Shape
{
    /// <summary>Imported formulas operate in parent-local inches, not in flattened world pixels.</summary>
    public bool UsesVisioCoordinates { get; set; }
    public string? FormulaParentId { get; set; }
    /// <summary>The group's logical coordinate domain remains stable when its world transform changes.</summary>
    public double CoordinateWidth { get; set; }
    public double CoordinateHeight { get; set; }
}
