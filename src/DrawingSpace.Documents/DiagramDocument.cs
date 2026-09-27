namespace DrawingSpace.Documents;

public sealed class DiagramDocument
{
    public const int CurrentVersion = 1;
    public int FormatVersion { get; set; } = CurrentVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Drawing1";
    public List<DiagramPage> Pages { get; set; } = [new()];
}
