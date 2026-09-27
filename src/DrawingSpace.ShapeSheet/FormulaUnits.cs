namespace DrawingSpace.ShapeSheet;

public static class FormulaUnits
{
    private static readonly IReadOnlyDictionary<string, (double Scale, FormulaDimension Dimension)> Units =
        new Dictionary<string, (double, FormulaDimension)>(StringComparer.OrdinalIgnoreCase)
        {
            ["in"] = (1, new(Length: 1)), ["inch"] = (1, new(Length: 1)), ["inches"] = (1, new(Length: 1)),
            ["mm"] = (1 / 25.4, new(Length: 1)), ["cm"] = (1 / 2.54, new(Length: 1)), ["m"] = (100 / 2.54, new(Length: 1)),
            ["km"] = (100000 / 2.54, new(Length: 1)), ["ft"] = (12, new(Length: 1)), ["yd"] = (36, new(Length: 1)),
            ["mi"] = (63360, new(Length: 1)), ["pt"] = (1d / 72, new(Length: 1)), ["pica"] = (1d / 6, new(Length: 1)),
            ["px"] = (1d / 96, new(Length: 1)), ["rad"] = (1, new(Angle: 1)), ["deg"] = (Math.PI / 180, new(Angle: 1)),
            ["°"] = (Math.PI / 180, new(Angle: 1)), ["sec"] = (1, new(Time: 1)), ["s"] = (1, new(Time: 1)),
            ["min"] = (60, new(Time: 1)), ["hr"] = (3600, new(Time: 1)), ["day"] = (86400, new(Time: 1)),
            ["wk"] = (604800, new(Time: 1)), ["%"] = (.01, default)
        };
    public static bool IsUnit(string symbol) => Units.ContainsKey(symbol);
    public static FormulaValue Literal(double value, string unit)
        => Units.TryGetValue(unit, out var item) ? FormulaValue.Number(value * item.Scale, item.Dimension)
            : FormulaValue.Error("#UNIT!", "Unknown unit: " + unit);
    public static FormulaValue Convert(FormulaValue value, string unit)
    {
        if (!Units.TryGetValue(unit, out var item)) return FormulaValue.Error("#UNIT!", "Unknown unit: " + unit);
        if (!value.IsNumeric || (value.Dimension != default && value.Dimension != item.Dimension)) return FormulaValue.Error("#UNIT!", "Incompatible dimensions.");
        return FormulaValue.Number(value.Numeric / item.Scale);
    }
}
