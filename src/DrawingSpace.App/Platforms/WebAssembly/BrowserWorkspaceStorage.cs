using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using DrawingSpace.Workbench;
using DrawingSpace.Visio;

namespace DrawingSpace.App;

internal sealed class BrowserWorkspaceStorage : IWorkspaceStorage, IBinaryWorkspaceStorage
{
    public async Task<string?> ReadRecoveryAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return await BrowserFiles.Load(); }
    public async Task SaveRecoveryAsync(string json, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); await BrowserFiles.Save(json); }
    public async Task<(string Name, string Text)?> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var result = await BrowserFiles.Open();
        if (string.IsNullOrEmpty(result)) return null;
        using var data = JsonDocument.Parse(result); return (data.RootElement.GetProperty("name").GetString()!, data.RootElement.GetProperty("text").GetString()!);
    }
    public async Task<DrawingFile?> OpenFileAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await BrowserFiles.OpenBinary(); cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(result)) return null;
        using var data = JsonDocument.Parse(result);
        var encoded = data.RootElement.GetProperty("base64").GetString()!;
        if (encoded.Length > (DrawingFileCodec.MaximumFileBytes + 2L) / 3 * 4) throw new InvalidDataException("File exceeds the binary input budget.");
        return new(data.RootElement.GetProperty("name").GetString()!, Convert.FromBase64String(encoded));
    }
    public async Task SaveAsync(string name, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); await BrowserFiles.Download(name, Convert.ToBase64String(bytes), contentType);
    }
    public async Task<string?> ReadClipboardAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return await BrowserFiles.ReadClipboard(); }
    public async Task WriteClipboardAsync(string text, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); await BrowserFiles.WriteClipboard(text); }
}
internal static partial class BrowserFiles
{
    [JSImport("globalThis.drawingSpaceStorage.load")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Load();
    [JSImport("globalThis.drawingSpaceStorage.save")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Save(string json);
    [JSImport("globalThis.drawingSpaceStorage.open")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Open();
    [JSImport("globalThis.drawingSpaceStorage.openBinary")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> OpenBinary();
    [JSImport("globalThis.drawingSpaceStorage.download")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Download(string name, string base64, string contentType);
    [JSImport("globalThis.drawingSpaceStorage.readClipboard")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> ReadClipboard();
    [JSImport("globalThis.drawingSpaceStorage.writeClipboard")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> WriteClipboard(string text);
    [JSImport("globalThis.drawingSpaceStorage.isTestMode")]
    internal static partial bool IsTestMode();
    [JSImport("globalThis.drawingSpaceStorage.publishDiagnostics")]
    internal static partial void PublishDiagnostics(string json);
}
