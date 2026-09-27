namespace DrawingSpace.Editing;

internal sealed record HistoryEntry(string Name, string Before, string After, string PageBefore, string PageAfter, string[] SelectionBefore, string[] SelectionAfter)
{
    public long EstimatedBytes => ((long)Before.Length + After.Length) * sizeof(char);
}
