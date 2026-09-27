namespace DrawingSpace.Documents;

/// <summary>UTF-16 range; nullable properties inherit the shape's default character style.</summary>
public sealed class TextSpan
{
    public int Start { get; set; }
    public int Length { get; set; }
    public string? FontFamily { get; set; }
    public double? FontSize { get; set; }
    public string? Color { get; set; }
    public string? Background { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public bool? StrikeThrough { get; set; }
    public double BaselineOffset { get; set; }
    public TextSpan Clone() => (TextSpan)MemberwiseClone();
}
