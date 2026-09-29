namespace DrawingSpace.Workbench;

/// <summary>Optional UTF-8 CSV/TSV file picker; existing workspace-storage implementations remain compatible.</summary>
public interface ITabularWorkspaceStorage
{
    Task<(string Name, string Text)?> OpenTableAsync(CancellationToken cancellationToken = default);
}
