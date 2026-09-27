using System.Security.Cryptography;
using System.Text;
using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

public static class VisioFingerprint
{
    public static string Compute(DiagramDocument document)
    {
        // A shallow read-only projection avoids mutating the caller or hashing the preserved archive into itself.
        var model = new DiagramDocument
        {
            FormatVersion = document.FormatVersion, Id = document.Id, Title = document.Title,
            Pages = document.Pages, Masters = document.Masters, Cells = document.Cells, Metadata = document.Metadata
        };
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ModelJson.Serialize(model))));
    }
}
