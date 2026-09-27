using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Stencils;

public sealed record StencilMaster(string Id, string Name, ShapeKind Kind, double Width = 144, double Height = 64)
{
    public Shape Create(PointD center) => new()
    {
        Name = Name, Text = Kind is ShapeKind.Person or ShapeKind.Cross or ShapeKind.Star or ShapeKind.Arrow or ShapeKind.Ring ? "" : Name,
        Kind = Kind, X = center.X - Width / 2, Y = center.Y - Height / 2, Width = Width, Height = Height,
        Style = new() { Fill = Kind == ShapeKind.Text || Kind == ShapeKind.Annotation ? "#00FFFFFF" : "#FFFFFF", Stroke = Kind == ShapeKind.Text ? "#00FFFFFF" : "#507EAA" }
    };
}

public sealed record Stencil(string Id, string Name, IReadOnlyList<StencilMaster> Masters);
