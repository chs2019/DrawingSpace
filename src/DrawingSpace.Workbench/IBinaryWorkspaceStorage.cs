using DrawingSpace.Visio;

namespace DrawingSpace.Workbench;

/// <summary>Optional binary capability; existing JSON-only hosts remain source compatible.</summary>
public interface IBinaryWorkspaceStorage
{
    Task<DrawingFile?> OpenFileAsync(CancellationToken cancellationToken = default);
}
