using System.Text.Json.Serialization;
using DrawingSpace.Core;

namespace DrawingSpace.Documents;

public sealed partial class Shape
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Process";
    public string Text { get; set; } = "Process";
    public ShapeKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 144;
    public double Height { get; set; } = 64;
    public double Rotation { get; set; }
    public double ShearX { get; set; }
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    public string LayerId { get; set; } = "default";
    public string? GroupId { get; set; }
    public string? ContainerId { get; set; }
    public bool IsGroupAnchor { get; set; }
    public bool Locked { get; set; }
    public ShapeStyle Style { get; set; } = new();
    public Dictionary<string, string> Data { get; set; } = [];
    public List<string> Comments { get; set; } = [];
    public List<CommentThread> Threads { get; set; } = [];
    public string? MasterId { get; set; }
    public string? MasterShapeId { get; set; }
    public string? MasterInstanceId { get; set; }
    public List<string> LocalOverrides { get; set; } = [];
    public Dictionary<string, ShapeCell> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GeometryFigure> Geometry { get; set; } = [];
    public List<ConnectionPoint> ConnectionPoints { get; set; } = [];
    public List<TextSpan> TextSpans { get; set; } = [];
    public List<ParagraphFormat> Paragraphs { get; set; } = [];
    public RectD? TextBounds { get; set; }
    public double TextRotation { get; set; }
    public List<ShapeHyperlink> Hyperlinks { get; set; } = [];
    public ContainerOptions? Container { get; set; }
    public byte[]? ImageData { get; set; }
    public string? ImageContentType { get; set; }
    public uint? VisioId { get; set; }
    public uint? VisioMasterId { get; set; }

    [JsonIgnore] public RectD Bounds => new(X, Y, Width, Height);
    [JsonIgnore] public MatrixD WorldMatrix => MatrixD.Translation(Bounds.Center.X, Bounds.Center.Y) * MatrixD.Rotation(Rotation)
        * new MatrixD(FlipX ? -Width : Width, 0, ShearX * Height, FlipY ? -Height : Height, 0, 0) * MatrixD.Translation(-.5, -.5);
    [JsonIgnore] public MatrixD DrawingMatrix => WorldMatrix * MatrixD.Scale(1 / Width, 1 / Height) * MatrixD.Translation(-X, -Y);
    [JsonIgnore] public RectD WorldBounds => WorldMatrix.Map(new RectD(0, 0, 1, 1));
    [JsonIgnore] public PointD[] WorldCorners => new RectD(0, 0, 1, 1).Corners.Select(WorldMatrix.Map).ToArray();

    public Shape Clone(bool newIdentity = false)
    {
        var clone = (Shape)MemberwiseClone();
        if (newIdentity) { clone.Id = Guid.NewGuid().ToString("N"); clone.VisioId = null; }
        clone.Style = Style.Clone(); clone.Data = new(Data); clone.Comments = [.. Comments];
        clone.Threads = Threads.Select(t => t.Clone()).ToList(); clone.LocalOverrides = [.. LocalOverrides];
        clone.Cells = Cells.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        clone.Geometry = Geometry.Select(g => g.Clone()).ToList(); clone.ConnectionPoints = ConnectionPoints.Select(p => p.Clone()).ToList();
        clone.TextSpans = TextSpans.Select(t => t.Clone()).ToList(); clone.Paragraphs = Paragraphs.Select(p => p.Clone()).ToList();
        clone.Hyperlinks = Hyperlinks.Select(h => h with { }).ToList(); clone.Container = Container?.Clone(); clone.ImageData = ImageData?.ToArray();
        return clone;
    }

    public PointD Port(PortSide side) => WorldMatrix.Map(side switch
    {
        PortSide.North => new(.5, 0), PortSide.East => new(1, .5), PortSide.South => new(.5, 1), PortSide.West => new(0, .5), _ => new PointD(.5, .5)
    });
    public PointD Port(string? pointId, PortSide fallback) => ConnectionPoints.FirstOrDefault(p => p.Id == pointId) is { } point ? WorldMatrix.Map(point.Position) : Port(fallback);

    public void ApplyWorldTransform(MatrixD transform)
    {
        var matrix = transform * WorldMatrix;
        var width = Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B);
        var determinant = matrix.Determinant;
        if (!matrix.IsFinite || width < 1e-8 || Math.Abs(determinant) < 1e-8) throw new ArgumentException("The transform is singular or non-finite.", nameof(transform));
        var height = Math.Abs(determinant) / width; var center = matrix.Map(new PointD(.5, .5));
        Width = width; Height = height; X = center.X - width / 2; Y = center.Y - height / 2;
        Rotation = Math.Atan2(matrix.B, matrix.A) * 180 / Math.PI;
        ShearX = (matrix.A * matrix.C + matrix.B * matrix.D) / (width * height);
        FlipX = false; FlipY = determinant < 0;
    }
}
