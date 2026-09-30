namespace DrawingSpace.Workbench;

/// <summary>Optional bounded XLSX picker. Existing JSON, binary drawing and CSV hosts remain source-compatible.</summary>
public interface IExcelWorkspaceStorage
{
    Task<(string Name, byte[] Bytes)?> OpenWorkbookAsync(CancellationToken cancellationToken = default);
}
