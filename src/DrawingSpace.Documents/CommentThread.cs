namespace DrawingSpace.Documents;

public sealed class CommentThread
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Resolved { get; set; }
    public List<CommentMessage> Messages { get; set; } = [];
    public CommentThread Clone() => new() { Id = Id, Resolved = Resolved, Messages = Messages.Select(m => m with { }).ToList() };
}
public sealed record CommentMessage(string Id, string Author, string Text, DateTimeOffset CreatedAt);
