using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using DrawingSpace.Documents;
using DrawingSpace.Workbench;

namespace DrawingSpace.App;

internal sealed partial class BrowserWorkspaceStorage : IExcelWorkspaceStorage
{
    public async Task<(string Name, byte[] Bytes)?> OpenWorkbookAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await BrowserFiles.OpenWorkbook();
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(result)) return null;
        if (result.Length > 4 * ((XlsxDataWorkbook.MaximumPackageBytes + 2) / 3) + 4096)
            throw new InvalidDataException("Workbook transfer exceeds its limit.");
        using var data = JsonDocument.Parse(result);
        var bytes = Convert.FromBase64String(data.RootElement.GetProperty("base64").GetString()!);
        if (bytes.Length > XlsxDataWorkbook.MaximumPackageBytes) throw new InvalidDataException("Workbook exceeds the 8 MiB compressed limit.");
        return (data.RootElement.GetProperty("name").GetString()!, bytes);
    }
}

internal static partial class BrowserFiles
{
    [JSImport("globalThis.drawingSpaceStorage.openWorkbook")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> OpenWorkbook();
}
