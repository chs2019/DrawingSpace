using DrawingSpace.Documents;
using DrawingSpace.Workbench;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DrawingSpace.App;

internal sealed partial class DesktopWorkspaceStorage : IExcelWorkspaceStorage
{
    public async Task<(string Name, byte[] Bytes)?> OpenWorkbookAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".xlsx");
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        using var input = await file.OpenStreamForReadAsync();
        using var output = new MemoryStream(); var buffer = new byte[32768]; int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > XlsxDataWorkbook.MaximumPackageBytes)
                throw new InvalidDataException("Workbook exceeds the 8 MiB compressed limit.");
            output.Write(buffer, 0, count);
        }
        return (file.Name, output.ToArray());
    }
}
