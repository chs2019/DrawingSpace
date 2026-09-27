using System.Text;
using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

public sealed record DrawingFile(string Name, byte[] Bytes);

/// <summary>Bounded format detection shared by native and browser hosts. Input is never executed.</summary>
public static class DrawingFileCodec
{
    public const int MaximumFileBytes = 32 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static VisioReadResult Read(DrawingFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file); ArgumentNullException.ThrowIfNull(file.Bytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (file.Bytes.Length == 0 || file.Bytes.Length > MaximumFileBytes) throw new InvalidDataException("The drawing is empty or exceeds the 32 MiB input limit.");
        var span = file.Bytes.AsSpan(); var offset = span.StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0;
        while (offset < span.Length && span[offset] is 9 or 10 or 13 or 32) offset++;
        if (offset < span.Length && span[offset] == (byte)'{')
        {
            try { return new(DocumentCodec.Load(StrictUtf8.GetString(span[offset..])), []); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("The JSON drawing is not valid UTF-8.", ex); }
        }
        return VisioReader.Read(file.Bytes, file.Name, new VisioReadOptions { CancellationToken = cancellationToken });
    }
    public static string ContentType(VisioPackageKind kind) => kind switch
    {
        VisioPackageKind.Stencil => "application/vnd.ms-visio.stencil",
        VisioPackageKind.Template => "application/vnd.ms-visio.template",
        _ => "application/vnd.ms-visio.drawing"
    };
}
