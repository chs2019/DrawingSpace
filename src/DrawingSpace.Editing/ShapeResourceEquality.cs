using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

/// <summary>Allocation-free structural comparisons for inherited mutable resources.</summary>
internal static class ShapeResourceEquality
{
    public static bool Text(Shape a, Shape b)
    {
        if (a.Text != b.Text || a.TextBounds != b.TextBounds || a.TextRotation != b.TextRotation
            || a.TextSpans.Count != b.TextSpans.Count || a.Paragraphs.Count != b.Paragraphs.Count) return false;
        for (var i = 0; i < a.TextSpans.Count; i++)
        {
            var x = a.TextSpans[i]; var y = b.TextSpans[i];
            if (x.Start != y.Start || x.Length != y.Length || x.FontFamily != y.FontFamily || x.FontSize != y.FontSize
                || x.Color != y.Color || x.Background != y.Background || x.Bold != y.Bold || x.Italic != y.Italic
                || x.Underline != y.Underline || x.StrikeThrough != y.StrikeThrough || x.BaselineOffset != y.BaselineOffset) return false;
        }
        for (var i = 0; i < a.Paragraphs.Count; i++)
        {
            var x = a.Paragraphs[i]; var y = b.Paragraphs[i];
            if (x.Start != y.Start || x.Alignment != y.Alignment || x.Direction != y.Direction || x.LineSpacing != y.LineSpacing
                || x.SpaceBefore != y.SpaceBefore || x.SpaceAfter != y.SpaceAfter || x.LeftIndent != y.LeftIndent
                || x.RightIndent != y.RightIndent || x.FirstLineIndent != y.FirstLineIndent || x.Bullet != y.Bullet) return false;
        }
        return true;
    }

    public static bool Geometry(Shape a, Shape b)
    {
        if (a.Geometry.Count != b.Geometry.Count) return false;
        for (var i = 0; i < a.Geometry.Count; i++)
        {
            var x = a.Geometry[i]; var y = b.Geometry[i];
            if (x.Filled != y.Filled || x.Stroked != y.Stroked || x.EvenOdd != y.EvenOdd || x.Segments.Count != y.Segments.Count) return false;
            for (var j = 0; j < x.Segments.Count; j++)
            {
                var u = x.Segments[j]; var v = y.Segments[j];
                if (u.Verb != v.Verb || u.End != v.End || u.Control1 != v.Control1 || u.Control2 != v.Control2
                    || u.Radius != v.Radius || u.Rotation != v.Rotation || u.LargeArc != v.LargeArc || u.Clockwise != v.Clockwise) return false;
            }
        }
        return true;
    }

    public static bool Ports(Shape a, Shape b)
    {
        if (a.ConnectionPoints.Count != b.ConnectionPoints.Count) return false;
        for (var i = 0; i < a.ConnectionPoints.Count; i++)
        {
            var x = a.ConnectionPoints[i]; var y = b.ConnectionPoints[i];
            if (x.Id != y.Id || x.Name != y.Name || x.Position != y.Position || x.Direction != y.Direction
                || x.Incoming != y.Incoming || x.Outgoing != y.Outgoing) return false;
        }
        return true;
    }
}
