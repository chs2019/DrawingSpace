using System.Text;
using DrawingSpace.Documents;
using DrawingSpace.Workbench;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DrawingSpace.App;

internal sealed partial class DesktopWorkspaceStorage : ITabularWorkspaceStorage
{
    public async Task<(string Name, string Text)?> OpenTableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".csv"); picker.FileTypeFilter.Add(".tsv");
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        using var input = await file.OpenStreamForReadAsync();
        using var output = new MemoryStream();
        var buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > CsvDataTable.MaximumCharacters)
                throw new InvalidDataException("CSV file exceeds the 4 MiB byte limit.");
            output.Write(buffer, 0, count);
        }
        return (file.Name, new UTF8Encoding(false, true).GetString(output.GetBuffer(), 0, (int)output.Length));
    }
}
