using System.Globalization;

namespace DrawingSpace.ShapeSheet;

public enum FormulaValueKind { Number, Text, Boolean, Error }

/// <summary>Dimensions are expressed in powers of inches, radians and seconds.</summary>
public readonly record struct FormulaDimension(int Length = 0, int Angle = 0, int Time = 0)
{
    public static FormulaDimension operator +(FormulaDimension a, FormulaDimension b) => new(a.Length + b.Length, a.Angle + b.Angle, a.Time + b.Time);
    public static FormulaDimension operator -(FormulaDimension a, FormulaDimension b) => new(a.Length - b.Length, a.Angle - b.Angle, a.Time - b.Time);
    public FormulaDimension Power(int power) => new(Length * power, Angle * power, Time * power);
}

public readonly record struct FormulaValue(FormulaValueKind Kind, double Numeric, string String, FormulaDimension Dimension)
{
    public bool IsError => Kind == FormulaValueKind.Error;
    public bool IsNumeric => Kind is FormulaValueKind.Number or FormulaValueKind.Boolean;
    public bool IsTrue => IsNumeric ? Numeric != 0 : Kind == FormulaValueKind.Text && String.Length > 0;
    public static FormulaValue Number(double value, FormulaDimension dimension = default) => double.IsFinite(value)
        ? new(FormulaValueKind.Number, value, "", dimension) : Error("#NUM!", "Non-finite result.");
    public static FormulaValue Text(string value) => new(FormulaValueKind.Text, 0, value, default);
    public static FormulaValue Boolean(bool value) => new(FormulaValueKind.Boolean, value ? 1 : 0, "", default);
    public static FormulaValue Error(string code, string message = "") => new(FormulaValueKind.Error, 0, code + (message.Length > 0 ? " " + message : ""), default);
    public override string ToString() => Kind switch
    {
        FormulaValueKind.Number => Numeric.ToString("G17", CultureInfo.InvariantCulture),
        FormulaValueKind.Boolean => IsTrue ? "TRUE" : "FALSE",
        _ => String
    };
}
