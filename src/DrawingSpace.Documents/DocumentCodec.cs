using System.Text.Json;
using System.Text.RegularExpressions;

namespace DrawingSpace.Documents;

public static partial class DocumentCodec
{
    public const int MaximumJsonLength = 32 * 1024 * 1024;
    public static string Save(DiagramDocument document) => JsonSerializer.Serialize(document, DocumentJsonContext.Default.DiagramDocument);
    public static DiagramDocument Clone(DiagramDocument document) => Load(Save(document));
    public static DiagramDocument Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaximumJsonLength) throw new InvalidDataException("The drawing exceeds the 32 MB JSON limit.");
        var document = JsonSerializer.Deserialize(json, DocumentJsonContext.Default.DiagramDocument) ?? throw new InvalidDataException("Empty drawing.");
        Validate(document);
        return document;
    }
    public static void Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FormatVersion != DiagramDocument.CurrentVersion) throw new InvalidDataException("Unsupported DrawingSpace file version.");
        if (document.Pages is null || document.Pages.Count is < 1 or > 256) throw new InvalidDataException("A drawing must contain 1 to 256 pages.");
        if (document.Title is null || document.Title.Length > 1024) throw new InvalidDataException("Invalid drawing title.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Unique(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || !ids.Add(id)) throw new InvalidDataException("Drawing contains missing or duplicate identities.");
        }
        Unique(document.Id);
        var count = 0;
        foreach (var page in document.Pages)
        {
            if (page is null) throw new InvalidDataException("Invalid page.");
            Unique(page.Id);
            if (!page.Bounds.IsFinite || page.Width is < 24 or > 100000 || page.Height is < 24 or > 100000) throw new InvalidDataException("Invalid page dimensions.");
            if (page.Shapes is null || page.Connectors is null || page.Layers is null || page.Layers.Count is < 1 or > 256) throw new InvalidDataException("Invalid page collections.");
            Color(page.Background);
            var layers = new HashSet<string>();
            foreach (var layer in page.Layers)
                if (layer is null || string.IsNullOrWhiteSpace(layer.Id) || !layers.Add(layer.Id)) throw new InvalidDataException("Duplicate or missing layer.");
            count += page.Shapes.Count + page.Connectors.Count;
            if (count > 50000) throw new InvalidDataException("This version accepts at most 50,000 objects per drawing.");
            var shapeIds = new HashSet<string>();
            foreach (var shape in page.Shapes)
            {
                if (shape is null) throw new InvalidDataException("Invalid shape.");
                Unique(shape.Id); shapeIds.Add(shape.Id);
                if (!shape.Bounds.IsFinite || Math.Abs(shape.X) > 1000000 || Math.Abs(shape.Y) > 1000000 || shape.Width is < 1 or > 100000 || shape.Height is < 1 or > 100000 || !double.IsFinite(shape.Rotation)) throw new InvalidDataException("Invalid shape geometry.");
                if (!Enum.IsDefined(shape.Kind) || !layers.Contains(shape.LayerId)) throw new InvalidDataException("Invalid shape type or layer.");
                if (shape.Style is null || shape.Data is null || shape.Comments is null || shape.Text is null || shape.Text.Length > 100000) throw new InvalidDataException("Invalid shape content.");
                var style = shape.Style;
                if (!double.IsFinite(style.FontSize) || style.FontSize is < 1 or > 1024 || !double.IsFinite(style.StrokeWidth) || style.StrokeWidth is < 0 or > 100 || !double.IsFinite(style.Opacity) || style.Opacity is < 0 or > 1) throw new InvalidDataException("Invalid shape style.");
                Color(style.Fill); Color(style.Stroke); Color(style.TextColor);
            }
            foreach (var edge in page.Connectors)
            {
                if (edge is null) throw new InvalidDataException("Invalid connector.");
                Unique(edge.Id);
                if (edge.SourceId is not null && !shapeIds.Contains(edge.SourceId) || edge.TargetId is not null && !shapeIds.Contains(edge.TargetId)) throw new InvalidDataException("Connector references a missing shape.");
                if (!edge.Start.IsFinite || !edge.End.IsFinite || !double.IsFinite(edge.Width) || edge.Width is < 0.1 or > 100 || !layers.Contains(edge.LayerId)) throw new InvalidDataException("Invalid connector geometry or layer.");
                if (!Enum.IsDefined(edge.Kind) || !Enum.IsDefined(edge.SourcePort) || !Enum.IsDefined(edge.TargetPort) || !Enum.IsDefined(edge.StartArrow) || !Enum.IsDefined(edge.EndArrow)) throw new InvalidDataException("Invalid connector type.");
                Color(edge.Color);
            }
        }
    }
    private static void Color(string value)
    {
        if (value is null || !HexColor().IsMatch(value)) throw new InvalidDataException("Colors must be #RRGGBB or #AARRGGBB.");
    }
    [GeneratedRegex("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();
}
