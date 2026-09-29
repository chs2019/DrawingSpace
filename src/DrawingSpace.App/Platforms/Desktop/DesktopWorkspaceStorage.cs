using DrawingSpace.Documents;
using DrawingSpace.Workbench;
using DrawingSpace.Visio;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;

namespace DrawingSpace.App;

internal sealed partial class DesktopWorkspaceStorage : IWorkspaceStorage, IBinaryWorkspaceStorage
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrawingSpace");
    private static string RecoveryPath => Path.Combine(DirectoryPath, "workspace.drawingspace.json");
    public async Task<string?> ReadRecoveryAsync(CancellationToken cancellationToken = default)
        => File.Exists(RecoveryPath) ? await File.ReadAllTextAsync(RecoveryPath, cancellationToken) : null;
    public async Task SaveRecoveryAsync(string json, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = RecoveryPath + ".tmp";
        await File.WriteAllTextAsync(temporary, json, cancellationToken);
        File.Move(temporary, RecoveryPath, true);
    }
    public async Task<(string Name, string Text)?> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); picker.FileTypeFilter.Add(".drawingspace");
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > DocumentCodec.MaximumJsonLength) throw new InvalidDataException("The drawing exceeds the import size limit.");
        return (file.Name, await FileIO.ReadTextAsync(file));
    }
    public async Task<DrawingFile?> OpenFileAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileOpenPicker();
        foreach (var extension in new[] { ".json", ".drawingspace", ".vsdx", ".vssx", ".vstx", ".vdx", ".vsd" }) picker.FileTypeFilter.Add(extension);
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        using var input = await file.OpenStreamForReadAsync(); using var output = new MemoryStream();
        var buffer = new byte[65536];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + read > DrawingFileCodec.MaximumFileBytes) throw new InvalidDataException("The drawing exceeds the 32 MiB file limit.");
            output.Write(buffer, 0, read);
        }
        return new(file.Name, output.ToArray());
    }
    public async Task SaveAsync(string name, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(name) };
        picker.FileTypeChoices.Add(contentType, [Path.GetExtension(name)]);
        var file = await picker.PickSaveFileAsync();
        if (file is null) throw new OperationCanceledException("Save was cancelled.");
        await FileIO.WriteBytesAsync(file, bytes);
    }
    public async Task<string?> ReadClipboardAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var data = Clipboard.GetContent();
        return data.Contains(StandardDataFormats.Text) ? await data.GetTextAsync() : null;
    }
    public Task WriteClipboardAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data); return Task.CompletedTask;
    }
}
