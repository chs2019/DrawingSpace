using DrawingSpace.Core;
using DrawingSpace.Documents;
using SkiaSharp;

namespace DrawingSpace.Skia;

public enum ShapeBooleanOperation { Union, Intersect, Subtract, Combine }

/// <summary>
/// Filled-area Boolean geometry in world coordinates. Inputs are not modified. The first
/// operand supplies text and formatting; Subtract removes every later operand from it.
/// Results are editable normalized compound paths. Stroke-only details and image pixels
/// are not operands. Callers own paths returned by CreateArea; model results own no native resources.
/// </summary>
public static class ShapeBooleanGeometry
{
    public const int MaximumOperands = 128;
    public const int MaximumSegments = 32768;
    private const int MaximumNativePoints = 131072;
    private const int ConicSubdivisionPower = 5;

    public static Shape? Compute(IReadOnlyList<Shape> shapes, ShapeBooleanOperation operation)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        if (shapes.Count is < 2 or > MaximumOperands)
            throw new ArgumentException("Select between two and 128 shapes.", nameof(shapes));
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        var sourceSegments = 0L;
        foreach (var shape in shapes)
        {
            ValidateOperand(shape);
            sourceSegments += shape.Geometry.Sum(g => (long)g.Segments.Count);
        }
        if (sourceSegments > MaximumSegments) throw new InvalidOperationException("The operands exceed the Boolean geometry budget.");
        var op = operation switch
        {
            ShapeBooleanOperation.Union => SKPathOp.Union,
            ShapeBooleanOperation.Intersect => SKPathOp.Intersect,
            ShapeBooleanOperation.Subtract => SKPathOp.Difference,
            ShapeBooleanOperation.Combine => SKPathOp.Xor,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        SKPath? accumulated = null;
        try
        {
            accumulated = CreateArea(shapes[0]);
            for (var i = 1; i < shapes.Count; i++)
            {
                using var operand = CreateArea(shapes[i]);
                var next = accumulated.Op(operand, op) ?? throw new InvalidOperationException("Skia could not resolve this Boolean operation.");
                accumulated.Dispose(); accumulated = next;
                CheckPathBudget(accumulated);
            }
            return ToShape(accumulated, shapes[0]);
        }
        finally { accumulated?.Dispose(); }
    }

    /// <summary>Creates an independent editable filled-outline copy, leaving the source and its semantic bindings untouched.</summary>
    public static Shape? CreatePathCopy(Shape source)
    {
        using var path = CreateArea(source);
        return ToShape(path, source);
    }

    /// <summary>Returns a caller-owned filled area, respecting per-figure fill rules and the complete affine transform.</summary>
    public static SKPath CreateArea(Shape shape)
    {
        ValidateOperand(shape);
        SKPath? area = null;
        try
        {
            if (shape.Geometry.Count == 0)
                area = ShapeGeometry.Create(shape);
            else
            {
                foreach (var figure in shape.Geometry.Where(g => g.Filled))
                {
                    using var part = ShapeGeometry.CreateFigure(shape, figure);
                    CheckPathBudget(part);
                    var next = area is null ? part.Simplify() : area.Op(part, SKPathOp.Union);
                    if (next is null) throw new InvalidOperationException("The filled contours could not be resolved.");
                    area?.Dispose(); area = next;
                    CheckPathBudget(area);
                }
            }
            if (area is null || area.IsEmpty) throw new InvalidOperationException("The shape has no filled vector area.");
            var transform = ShapeGeometry.Matrix(shape.DrawingMatrix);
            area.Transform(in transform);
            CheckPathBudget(area);
            var simplified = area.Simplify() ?? throw new InvalidOperationException("The vector outline could not be simplified.");
            area.Dispose(); area = null;
            try { CheckPathBudget(simplified); return simplified; }
            catch { simplified.Dispose(); throw; }
        }
        finally { area?.Dispose(); }
    }

    private static void ValidateOperand(Shape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.ImageData is not null) throw new InvalidOperationException("Image pixels cannot participate in filled-vector Boolean operations.");
        if (shape.IsGroupAnchor || shape.Kind is ShapeKind.Text or ShapeKind.Annotation)
            throw new InvalidOperationException("Select filled vector shapes, not group anchors, text boxes or open annotations.");
        if (!shape.WorldMatrix.IsFinite || !shape.Bounds.IsFinite || shape.Width is < 1 or > 100000 || shape.Height is < 1 or > 100000)
            throw new ArgumentException("The operand has invalid geometry.", nameof(shape));
        if (shape.Geometry is null || shape.Geometry.Count > 1024 || shape.Geometry.Any(g => g is null || g.Segments is null)
            || shape.Geometry.Sum(g => (long)g.Segments.Count) > MaximumSegments)
            throw new ArgumentException("The operand exceeds the path budget.", nameof(shape));
    }

    private static void CheckPathBudget(SKPath path)
    {
        if (path.PointCount > MaximumNativePoints) throw new InvalidOperationException("The Boolean result exceeds the native path budget.");
        var bounds = path.Bounds;
        if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom))
            throw new InvalidOperationException("The Boolean path has non-finite coordinates.");
    }

    private static Shape? ToShape(SKPath path, Shape primary)
    {
        if (path.IsEmpty) return null;
        var bounds = path.Bounds;
        if (bounds.Width <= 1e-6f || bounds.Height <= 1e-6f) return null;
        var width = Math.Max(1d, bounds.Width); var height = Math.Max(1d, bounds.Height);
        if (width > 100000 || height > 100000 || Math.Abs(bounds.Left) > 1000000 || Math.Abs(bounds.Top) > 1000000)
            throw new InvalidOperationException("The Boolean result exceeds the drawing coordinate limits.");
        var figure = new GeometryFigure { Filled = true, Stroked = true, EvenOdd = path.FillType == SKPathFillType.EvenOdd };
        PointD Normalize(SKPoint p) => new((p.X - (double)bounds.Left) / width, (p.Y - (double)bounds.Top) / height);
        void Add(GeometrySegment segment)
        {
            if (figure.Segments.Count == MaximumSegments) throw new InvalidOperationException("The editable result exceeds the segment budget.");
            if (!segment.End.IsFinite || !segment.Control1.IsFinite || !segment.Control2.IsFinite)
                throw new InvalidOperationException("The editable result has non-finite coordinates.");
            figure.Segments.Add(segment);
        }
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4]; SKPoint[]? quads = null;
        for (var verb = iterator.Next(points); verb != SKPathVerb.Done; verb = iterator.Next(points))
        {
            switch (verb)
            {
                case SKPathVerb.Move: Add(new() { Verb = GeometryVerb.Move, End = Normalize(points[0]) }); break;
                case SKPathVerb.Line: Add(new() { Verb = GeometryVerb.Line, End = Normalize(points[1]) }); break;
                case SKPathVerb.Quad: Add(new() { Verb = GeometryVerb.Quadratic, Control1 = Normalize(points[1]), End = Normalize(points[2]) }); break;
                case SKPathVerb.Cubic: Add(new() { Verb = GeometryVerb.Cubic, Control1 = Normalize(points[1]), Control2 = Normalize(points[2]), End = Normalize(points[3]) }); break;
                case SKPathVerb.Conic:
                    // The document model has quadratic/cubic curves, not rational conics.
                    // Skia's bounded 32-quad conversion preserves smooth geometry without
                    // flattening it into line segments. It is an approximation, not exact conic storage.
                    quads ??= new SKPoint[1 + 2 * (1 << ConicSubdivisionPower)];
                    var count = SKPath.ConvertConicToQuads(points[0], points[1], points[2], iterator.ConicWeight(), quads, ConicSubdivisionPower);
                    if (count is < 1 or > 32) throw new InvalidOperationException("Conic conversion failed.");
                    for (var i = 0; i < count; i++) Add(new() { Verb = GeometryVerb.Quadratic, Control1 = Normalize(quads[i * 2 + 1]), End = Normalize(quads[i * 2 + 2]) });
                    break;
                case SKPathVerb.Close: Add(new() { Verb = GeometryVerb.Close }); break;
                default: throw new InvalidOperationException("Unsupported Boolean result path verb.");
            }
        }
        if (figure.Segments.Count == 0) return null;
        // All contours belong to ONE figure: splitting holes into independently filled
        // figures would turn subtraction and XOR holes back into solid regions.
        return new Shape
        {
            Name = primary.Name, Text = primary.Text, Kind = ShapeKind.Rectangle,
            X = bounds.Left, Y = bounds.Top, Width = width, Height = height,
            LayerId = primary.LayerId, Style = primary.Style.Clone(), Geometry = [figure],
            Data = new(primary.Data), Comments = [.. primary.Comments],
            Threads = primary.Threads.Select(t => t.Clone()).ToList(),
            Hyperlinks = primary.Hyperlinks.Select(h => h with { }).ToList(),
            TextSpans = primary.TextSpans.Select(s => s.Clone()).ToList(),
            Paragraphs = primary.Paragraphs.Select(p => p.Clone()).ToList()
        };
    }
}
