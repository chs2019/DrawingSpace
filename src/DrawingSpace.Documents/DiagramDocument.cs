namespace DrawingSpace.Documents;

public sealed class DiagramDocument
{
    public const int CurrentVersion = 2;
    public int FormatVersion { get; set; } = CurrentVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Drawing1";
    public List<DiagramPage> Pages { get; set; } = [new()];
    public List<DiagramMaster> Masters { get; set; } = [];
    public Dictionary<string, ShapeCell> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public VisioPackageOrigin? VisioOrigin { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}
