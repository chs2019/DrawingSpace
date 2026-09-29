using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using DrawingSpace.Documents;
using DrawingSpace.Workbench;

namespace DrawingSpace.App;

internal sealed partial class BrowserWorkspaceStorage : ITabularWorkspaceStorage
{
    public async Task<(string Name, string Text)?> OpenTableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await BrowserFiles.OpenTable();
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(result)) return null;
        using var data = JsonDocument.Parse(result);
        var text = data.RootElement.GetProperty("text").GetString()!;
        if (text.Length > CsvDataTable.MaximumCharacters) throw new InvalidDataException("CSV exceeds its text limit.");
        return (data.RootElement.GetProperty("name").GetString()!, text);
    }
}

internal static partial class BrowserFiles
{
    [JSImport("globalThis.drawingSpaceStorage.openTable")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> OpenTable();
}
