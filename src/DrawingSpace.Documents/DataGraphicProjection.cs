using DrawingSpace.Core;

namespace DrawingSpace.Documents;

/// <summary>
/// Projects data graphics to ordinary, local-space vector/text shapes without modifying
/// the document. Callers apply Translation(owner.X, owner.Y) inside owner.DrawingMatrix.
/// </summary>
public static class DataGraphicProjection
{
    public static string Fill(Shape owner)
    {
        var fill = owner.Style.Fill;
        foreach (var rule in owner.DataGraphics)
            if (rule.Kind == DataGraphicKind.ColorByValue && rule.TryFraction(owner, out var fraction))
                fill = rule.Color(fraction);
        return fill;
    }

    public static RectD WorldBounds(Shape owner)
    {
        var bounds = new RectD(0, 0, owner.Width, owner.Height);
        foreach (var rule in owner.DataGraphics)
            if (rule.Kind != DataGraphicKind.ColorByValue && owner.Data.ContainsKey(rule.Field))
                bounds = RectD.Union(bounds, rule.LocalBounds(owner).Inflate(2));
        var matrix = owner.WorldMatrix * MatrixD.Scale(1 / owner.Width, 1 / owner.Height);
        return matrix.Map(bounds.Inflate(Math.Max(2, owner.Style.StrokeWidth)));
    }

    public static IReadOnlyList<Shape> Create(Shape owner)
    {
        var result = new List<Shape>();
        for (var i = 0; i < owner.DataGraphics.Count; i++)
        {
            var rule = owner.DataGraphics[i];
            if (rule.Kind == DataGraphicKind.ColorByValue || !owner.Data.TryGetValue(rule.Field, out var value)) continue;
            var box = rule.LocalBounds(owner);
            var numeric = rule.TryFraction(owner, out var fraction);
            if (!numeric && rule.Kind != DataGraphicKind.TextCallout) continue;
            var label = Clip((rule.Label.Length > 0 ? rule.Label : rule.Field) + ": " + value, 256);
            var prefix = owner.Id + "--data-" + i + "-";
            Shape Add(string part, ShapeKind kind, RectD rectangle, string fill, string stroke, double strokeWidth = 0)
            {
                var shape = new Shape
                {
                    Id = prefix + part, Name = label, Text = "", Kind = kind,
                    X = rectangle.X, Y = rectangle.Y, Width = Math.Max(1, rectangle.Width), Height = Math.Max(1, rectangle.Height),
                    Style = new() { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth, Opacity = owner.Style.Opacity,
                        FontSize = rule.FontSize, FontFamily = owner.Style.FontFamily, TextColor = rule.TextColor }
                };
                result.Add(shape); return shape;
            }
            void Text()
            {
                var text = Add("text", ShapeKind.Text, box, "#00FFFFFF", "#00FFFFFF");
                text.Text = label; text.TextBounds = new(0, 0, 1, 1);
            }
            switch (rule.Kind)
            {
                case DataGraphicKind.DataBar:
                    Add("track", ShapeKind.Rectangle, box, "#F1F4F8", "#AEB9C9", 1);
                    if (fraction > 0)
                        Add("value", ShapeKind.Rectangle, new(box.X, box.Y, box.Width * fraction, box.Height),
                            rule.Color(fraction), "#00FFFFFF");
                    Text();
                    break;
                case DataGraphicKind.IconSet:
                    var size = Math.Min(box.Width, box.Height);
                    var square = new RectD(box.X, box.Y, size, size);
                    Add("badge", ShapeKind.Ellipse, square, rule.Color(fraction), "#00FFFFFF");
                    var mark = Add("symbol", ShapeKind.Annotation, square, "#00FFFFFF", "#FFFFFF", Math.Max(1, size * .08));
                    var points = rule.Band(fraction) switch
                    {
                        2 => new[] { new PointD(.22, .52), new PointD(.43, .73), new PointD(.8, .28) },
                        1 => new[] { new PointD(.5, .2), new PointD(.5, .58), new PointD(.5, .74), new PointD(.5, .83) },
                        _ => new[] { new PointD(.26, .26), new PointD(.74, .74), new PointD(.74, .26), new PointD(.26, .74) }
                    };
                    var figure = new GeometryFigure { Filled = false, Stroked = true };
                    for (var point = 0; point < points.Length; point++)
                        figure.Segments.Add(new() { Verb = point == 0 || point == 2 && points.Length == 4
                            ? GeometryVerb.Move : GeometryVerb.Line, End = points[point] });
                    mark.Geometry.Add(figure);
                    break;
                case DataGraphicKind.TextCallout:
                    Add("background", ShapeKind.Rectangle, box, "#FFFFFF", "#D6E0E8", 1);
                    Text();
                    break;
            }
        }
        return result;
    }

    private static string Clip(string text, int length)
    {
        if (text.Length <= length) return text;
        if (char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length])) length--;
        return text[..length] + "…";
    }
}
