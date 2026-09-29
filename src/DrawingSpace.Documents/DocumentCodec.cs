using System.Text.Json;
using System.Text.RegularExpressions;

namespace DrawingSpace.Documents;

public static partial class DocumentCodec
{
    public const int MaximumJsonLength = 32 * 1024 * 1024;
    public const int MaximumPackageBytes = 16 * 1024 * 1024;
    public static string Save(DiagramDocument document)
    {
        var json = JsonSerializer.Serialize(document, DocumentJsonContext.Default.DiagramDocument);
        if (json.Length > MaximumJsonLength) throw new InvalidDataException("The drawing exceeds the 32 MiB JSON budget.");
        return json;
    }
    public static DiagramDocument Clone(DiagramDocument document) => Load(Save(document));
    public static DiagramDocument Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaximumJsonLength) throw new InvalidDataException("The drawing exceeds the 32 MiB JSON budget.");
        DiagramDocument document;
        try { document = JsonSerializer.Deserialize(json, DocumentJsonContext.Default.DiagramDocument) ?? throw new InvalidDataException("Empty drawing."); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid drawing JSON.", ex); }
        if (document.FormatVersion == 1)
        {
            document.FormatVersion = DiagramDocument.CurrentVersion;
            if (document.Pages is not null)
                foreach (var page in document.Pages.Where(p => p is not null && p.Shapes is not null))
                    foreach (var group in page.Shapes.Where(s => s is not null && s.GroupId is not null).Select(s => s.GroupId!).Distinct())
                        if (!page.Groups.Any(g => g.Id == group)) page.Groups.Add(new() { Id = group });
        }
        Validate(document);
        return document;
    }
    public static void Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FormatVersion is not 1 and not DiagramDocument.CurrentVersion) throw new InvalidDataException("Unsupported DrawingSpace file version.");
        if (document.Pages is null || document.Pages.Count is < 1 or > 256 || document.Masters is null || document.Masters.Count > 4096) throw new InvalidDataException("Invalid page or master collection.");
        Text(document.Title, 1024); TextMap(document.Metadata, 1024); Cells(document.Cells);
        if (document.VisioOrigin is { } origin)
        {
            if (origin.Package is null || origin.Package.Length > MaximumPackageBytes) throw new InvalidDataException("Original Visio package exceeds the preservation budget.");
            Text(origin.Format, 16); Text(origin.ModelFingerprint, 128);
            if (origin.Diagnostics is null || origin.Diagnostics.Count > 4096) throw new InvalidDataException("Invalid import diagnostics.");
            foreach (var diagnostic in origin.Diagnostics) Text(diagnostic, 4096);
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Unique(string id) { Identifier(id); if (!ids.Add(id)) throw new InvalidDataException("Drawing contains duplicate identities."); }
        Unique(document.Id);
        var masterIds = new HashSet<string>(); var count = 0;
        foreach (var master in document.Masters)
        {
            if (master is null || master.Shape is null || master.Children is null || master.Children.Count > 8192) throw new InvalidDataException("Invalid master.");
            Unique(master.Id); masterIds.Add(master.Id); Text(master.Name, 1024);
            foreach (var shape in master.Children.Prepend(master.Shape)) { Unique(shape.Id); ValidateShape(shape); count++; }
        }
        foreach (var page in document.Pages)
        {
            if (page is null) throw new InvalidDataException("Invalid page.");
            Unique(page.Id); Text(page.Name, 1024); Cells(page.Cells);
            if (!page.Bounds.IsFinite || page.Width is < 24 or > 100000 || page.Height is < 24 or > 100000) throw new InvalidDataException("Invalid page dimensions.");
            if (page.Shapes is null || page.Connectors is null || page.Layers is null || page.Layers.Count is < 1 or > 256 || page.Groups is null || page.Groups.Count > 10000) throw new InvalidDataException("Invalid page collections.");
            Color(page.Background);
            var layers = new HashSet<string>();
            foreach (var layer in page.Layers)
            {
                if (layer is null) throw new InvalidDataException("Invalid layer.");
                Identifier(layer.Id); Text(layer.Name, 1024);
                if (!layers.Add(layer.Id)) throw new InvalidDataException("Duplicate layer.");
            }
            count += page.Shapes.Count + page.Connectors.Count;
            if (count > 50000) throw new InvalidDataException("At most 50,000 diagram and master objects are accepted.");
            var shapeIds = new HashSet<string>();
            foreach (var shape in page.Shapes)
            {
                if (shape is null) throw new InvalidDataException("Invalid shape.");
                Unique(shape.Id); shapeIds.Add(shape.Id); ValidateShape(shape);
                if (!layers.Contains(shape.LayerId)) throw new InvalidDataException("Missing shape layer.");
                if (shape.MasterId is not null && !masterIds.Contains(shape.MasterId)) throw new InvalidDataException("Missing shape master.");
            }
            foreach (var group in page.Groups) { if (group is null) throw new InvalidDataException("Invalid group."); Unique(group.Id); Text(group.Name, 1024); }
            ValidateTree(page.Groups.ToDictionary(g => g.Id, g => g.ParentId), "group");
            var containers = page.Shapes.ToDictionary(s => s.Id, s => s.ContainerId);
            ValidateTree(containers, "container");
            foreach (var shape in page.Shapes)
                if (shape.ContainerId is { } containerId && page.Find(containerId) is { } container && container.Container is null && container.Kind != Core.ShapeKind.Container)
                    throw new InvalidDataException("A shape is assigned to a non-container.");
            foreach (var edge in page.Connectors)
            {
                if (edge is null) throw new InvalidDataException("Invalid connector.");
                Unique(edge.Id);
                if (edge.SourceId is not null && !shapeIds.Contains(edge.SourceId) || edge.TargetId is not null && !shapeIds.Contains(edge.TargetId)) throw new InvalidDataException("Connector references a missing shape.");
                if (!edge.Start.IsFinite || !edge.End.IsFinite || !double.IsFinite(edge.Width) || edge.Width is < 0.1 or > 100 || !layers.Contains(edge.LayerId)) throw new InvalidDataException("Invalid connector geometry or layer.");
                if (!Enum.IsDefined(edge.Kind) || !Enum.IsDefined(edge.SourcePort) || !Enum.IsDefined(edge.TargetPort) || !Enum.IsDefined(edge.StartArrow) || !Enum.IsDefined(edge.EndArrow) || !Enum.IsDefined(edge.LineJumps)) throw new InvalidDataException("Invalid connector type.");
                if (edge.Waypoints is null || edge.Waypoints.Count > 4096 || edge.Waypoints.Any(p => !p.IsFinite || Math.Abs(p.X) > 1000000 || Math.Abs(p.Y) > 1000000)) throw new InvalidDataException("Invalid route waypoints.");
                if (!double.IsFinite(edge.JumpSize) || edge.JumpSize is < 0 or > 128 || !double.IsFinite(edge.LabelPosition) || edge.LabelPosition is < 0 or > 1 || !edge.LabelOffset.IsFinite) throw new InvalidDataException("Invalid connector decoration.");
                if (edge.SourcePointId is not null && page.Find(edge.SourceId)?.ConnectionPoints.Any(p => p.Id == edge.SourcePointId) != true) throw new InvalidDataException("Missing source connection point.");
                if (edge.TargetPointId is not null && page.Find(edge.TargetId)?.ConnectionPoints.Any(p => p.Id == edge.TargetPointId) != true) throw new InvalidDataException("Missing target connection point.");
                Text(edge.Text, 100000); Color(edge.Color); Cells(edge.Cells);
            }
        }
        ValidateTree(document.Pages.ToDictionary(p => p.Id, p => p.BackgroundPageId), "background page");
        AdvancedDocumentValidation.Validate(document);
    }
    private static void ValidateShape(Shape shape)
    {
        if (!shape.Bounds.IsFinite || Math.Abs(shape.X) > 1000000 || Math.Abs(shape.Y) > 1000000 || shape.Width is < 1 or > 100000 || shape.Height is < 1 or > 100000 || !double.IsFinite(shape.Rotation) || !double.IsFinite(shape.ShearX) || Math.Abs(shape.ShearX) > 1000) throw new InvalidDataException("Invalid shape geometry.");
        if (!Enum.IsDefined(shape.Kind) || shape.Style is null) throw new InvalidDataException("Invalid shape type or style.");
        Text(shape.Name, 1024); Text(shape.Text, 100000); TextMap(shape.Data, 4096); Cells(shape.Cells);
        ValidateDataFeatures(shape);
        if (shape.Comments is null || shape.Comments.Count > 4096 || shape.Threads is null || shape.Threads.Count > 4096) throw new InvalidDataException("Invalid comments.");
        foreach (var comment in shape.Comments) Text(comment, 100000);
        foreach (var thread in shape.Threads)
        {
            if (thread is null || thread.Messages is null || thread.Messages.Count > 4096) throw new InvalidDataException("Invalid comment thread.");
            Identifier(thread.Id);
            foreach (var message in thread.Messages) { if (message is null) throw new InvalidDataException("Invalid message."); Identifier(message.Id); Text(message.Author, 1024); Text(message.Text, 100000); }
        }
        var style = shape.Style;
        if (!double.IsFinite(style.FontSize) || style.FontSize is < 1 or > 1024 || !double.IsFinite(style.StrokeWidth) || style.StrokeWidth is < 0 or > 100 || !double.IsFinite(style.Opacity) || style.Opacity is < 0 or > 1) throw new InvalidDataException("Invalid shape style.");
        Color(style.Fill); Color(style.Stroke); Color(style.TextColor); Text(style.FontFamily, 1024);
        if (shape.LocalOverrides is null || shape.LocalOverrides.Count > 4096) throw new InvalidDataException("Invalid master overrides.");
        foreach (var key in shape.LocalOverrides) Text(key, 256);
        if (shape.Geometry is null || shape.Geometry.Count > 1024 || shape.Geometry.Any(g => g is null || g.Segments is null) || shape.Geometry.Sum(g => g.Segments.Count) > 32768) throw new InvalidDataException("Invalid custom geometry.");
        foreach (var segment in shape.Geometry.SelectMany(g => g.Segments))
            if (segment is null || !Enum.IsDefined(segment.Verb) || !segment.End.IsFinite || !segment.Control1.IsFinite || !segment.Control2.IsFinite || !segment.Radius.IsFinite || !double.IsFinite(segment.Rotation)) throw new InvalidDataException("Invalid geometry segment.");
        if (shape.ConnectionPoints is null || shape.ConnectionPoints.Count > 4096) throw new InvalidDataException("Invalid connection points.");
        var pointIds = new HashSet<string>();
        foreach (var point in shape.ConnectionPoints)
        {
            if (point is null || !point.Position.IsFinite || !point.Direction.IsFinite) throw new InvalidDataException("Invalid connection point.");
            Identifier(point.Id); Text(point.Name, 1024); if (!pointIds.Add(point.Id)) throw new InvalidDataException("Duplicate connection point.");
        }
        if (shape.TextSpans is null || shape.TextSpans.Count > 16384 || shape.Paragraphs is null || shape.Paragraphs.Count > 4096) throw new InvalidDataException("Invalid rich text.");
        foreach (var span in shape.TextSpans)
        {
            if (span is null || span.Start < 0 || span.Length < 0 || (long)span.Start + span.Length > shape.Text.Length || !Boundary(shape.Text, span.Start) || !Boundary(shape.Text, span.Start + span.Length)) throw new InvalidDataException("Invalid UTF-16 formatting range.");
            if (span.FontSize is { } size && (!double.IsFinite(size) || size is < 1 or > 1024) || !double.IsFinite(span.BaselineOffset) || Math.Abs(span.BaselineOffset) > 1024) throw new InvalidDataException("Invalid text span style.");
            if (span.Color is not null) Color(span.Color); if (span.Background is not null) Color(span.Background); if (span.FontFamily is not null) Text(span.FontFamily, 1024);
        }
        foreach (var paragraph in shape.Paragraphs)
            if (paragraph is null || paragraph.Start < 0 || paragraph.Start > shape.Text.Length || !Boundary(shape.Text, paragraph.Start) || !Enum.IsDefined(paragraph.Alignment) || !Enum.IsDefined(paragraph.Direction) || !double.IsFinite(paragraph.LineSpacing) || paragraph.LineSpacing is < .3 or > 10 || !double.IsFinite(paragraph.LeftIndent) || !double.IsFinite(paragraph.RightIndent) || !double.IsFinite(paragraph.FirstLineIndent) || !double.IsFinite(paragraph.SpaceBefore) || !double.IsFinite(paragraph.SpaceAfter)) throw new InvalidDataException("Invalid paragraph formatting.");
        if (shape.Container is { } container && (!double.IsFinite(container.Padding) || container.Padding is < 0 or > 4096 || !double.IsFinite(container.HeaderHeight) || container.HeaderHeight is < 0 or > 4096 || !Enum.IsDefined(container.Layout))) throw new InvalidDataException("Invalid container settings.");
        if (shape.ImageData is { Length: > 8388608 }) throw new InvalidDataException("Embedded images are limited to 8 MiB each.");
    }
    private static bool Boundary(string text, int offset) => offset <= 0 || offset >= text.Length || !char.IsHighSurrogate(text[offset - 1]) || !char.IsLowSurrogate(text[offset]);
    private static void ValidateTree(IReadOnlyDictionary<string, string?> parents, string kind)
    {
        foreach (var key in parents.Keys)
        {
            var seen = new HashSet<string> { key }; var parent = parents[key];
            while (parent is not null)
            {
                if (!parents.TryGetValue(parent, out var next)) throw new InvalidDataException("Missing " + kind + " parent.");
                if (!seen.Add(parent) || seen.Count > 64) throw new InvalidDataException("Cyclic or over-deep " + kind + " hierarchy.");
                parent = next;
            }
        }
    }
    private static void Identifier(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new InvalidDataException("Missing or invalid identity."); }
    private static void Text(string value, int maximum) { if (value is null || value.Length > maximum) throw new InvalidDataException("Text field exceeds its budget or is null."); }
    private static void TextMap(Dictionary<string, string> values, int maximum)
    {
        if (values is null || values.Count > maximum) throw new InvalidDataException("Invalid property collection.");
        foreach (var (key, value) in values) { Text(key, 1024); Text(value, 100000); }
    }
    private static void Cells(Dictionary<string, ShapeCell> cells)
    {
        if (cells is null || cells.Count > 4096) throw new InvalidDataException("Invalid ShapeSheet cell collection.");
        foreach (var (key, cell) in cells) { Text(key, 256); if (cell is null) throw new InvalidDataException("Invalid ShapeSheet cell."); Text(cell.Formula, 32768); Text(cell.Value, 32768); Text(cell.Unit, 128); }
    }
    private static void Color(string value) { if (value is null || !HexColor().IsMatch(value)) throw new InvalidDataException("Colors must be #RRGGBB or #AARRGGBB."); }
    [GeneratedRegex("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();
}
