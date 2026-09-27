namespace DrawingSpace.Documents;

public enum ParagraphAlignment { Left, Center, Right, Justify }
public enum ParagraphDirection { Auto, LeftToRight, RightToLeft }
public enum VerticalTextAlignment { Top, Center, Bottom }
public sealed class ParagraphFormat
{
    public int Start { get; set; }
    public ParagraphAlignment Alignment { get; set; } = ParagraphAlignment.Center;
    public ParagraphDirection Direction { get; set; }
    public double LineSpacing { get; set; } = 1.24;
    public double SpaceBefore { get; set; }
    public double SpaceAfter { get; set; }
    public double LeftIndent { get; set; }
    public double RightIndent { get; set; }
    public double FirstLineIndent { get; set; }
    public bool Bullet { get; set; }
    public ParagraphFormat Clone() => (ParagraphFormat)MemberwiseClone();
}
