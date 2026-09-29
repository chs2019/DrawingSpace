using System.Globalization;
using DrawingSpace.Core;

namespace DrawingSpace.Documents;

public enum DataGraphicKind { ColorByValue, DataBar, IconSet, TextCallout }

/// <summary>Immutable presentation rule. Bounds are normalized in the owner's local frame.</summary>
public sealed record ShapeDataGraphic
{
    public DataGraphicKind Kind { get; init; }
    public string Field { get; init; } = "";
    public string Label { get; init; } = "";
    public RectD Bounds { get; init; } = new(0, 1.08, 1, .3);
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;
    public bool LowerIsBetter { get; init; }
    public string LowColor { get; init; } = "#D13438";
    public string MiddleColor { get; init; } = "#F2B134";
    public string HighColor { get; init; } = "#107C10";
    public string TextColor { get; init; } = "#253858";
    public double FontSize { get; init; } = 12;

    public bool TryFraction(Shape shape, out double fraction)
    {
        fraction = 0;
        if (!shape.Data.TryGetValue(Field, out var text)
            || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value) || !double.IsFinite(Minimum)
            || !double.IsFinite(Maximum) || Maximum <= Minimum)
            return false;
        fraction = Math.Clamp((value - Minimum) / (Maximum - Minimum), 0, 1);
        return true;
    }

    public int Band(double fraction)
    {
        var score = LowerIsBetter ? 1 - fraction : fraction;
        return score < 1d / 3 ? 0 : score < 2d / 3 ? 1 : 2;
    }

    public string Color(double fraction) => Band(fraction) switch
    {
        0 => LowColor, 1 => MiddleColor, _ => HighColor
    };

    public RectD LocalBounds(Shape shape) => new(
        Bounds.X * shape.Width, Bounds.Y * shape.Height,
        Bounds.Width * shape.Width, Bounds.Height * shape.Height);
}
