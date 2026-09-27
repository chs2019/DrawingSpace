namespace DrawingSpace.Workbench;

/// <summary>Host-provided storage keeps filesystem and browser interop out of reusable UI controls.</summary>
public interface IWorkspaceStorage
{
    Task<string?> ReadRecoveryAsync(CancellationToken cancellationToken = default);
    Task SaveRecoveryAsync(string json, CancellationToken cancellationToken = default);
    Task<(string Name, string Text)?> OpenAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(string name, byte[] bytes, string contentType, CancellationToken cancellationToken = default);
    Task<string?> ReadClipboardAsync(CancellationToken cancellationToken = default);
    Task WriteClipboardAsync(string text, CancellationToken cancellationToken = default);
}
