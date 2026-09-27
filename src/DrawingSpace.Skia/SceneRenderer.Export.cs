using System.Globalization;
using System.Text;
using System.Xml;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using SkiaSharp;

namespace DrawingSpace.Skia;

public sealed partial class SceneRenderer
{
    public byte[] ExportPng(DiagramPage page, double scale = 2)
    {
        if (!double.IsFinite(scale) || scale is < .1 or > 4) throw new ArgumentOutOfRangeException(nameof(scale));
        var width = (int)Math.Ceiling(page.Width * scale); var height = (int)Math.Ceiling(page.Height * scale);
        if (width < 1 || height < 1 || (long)width * height > 64_000_000 || width > 16384 || height > 16384) throw new InvalidOperationException("The requested image exceeds the safe export size. Reduce the page size or export scale.");
        using var surface = SKSurface.Create(new SKImageInfo(width, height)) ?? throw new InvalidOperationException("Could not allocate the export surface.");
        surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.Scale((float)scale);
        ClearCache(); DrawPage(surface.Canvas, page, 0, printing: true);
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    public byte[] ExportPdf(DiagramDocument drawing)
    {
        DocumentCodec.Validate(drawing);
        using var stream = new MemoryStream();
        using (var document = SKDocument.CreatePdf(stream) ?? throw new InvalidOperationException("PDF export is not available on this platform."))
        {
            foreach (var page in drawing.Pages)
            {
                var canvas = document.BeginPage((float)page.Width * .75f, (float)page.Height * .75f);
                canvas.Scale(.75f); canvas.ClipRect(ShapeGeometry.Rect(page.Bounds)); ClearCache();
                DrawPage(canvas, page, 0, printing: true); document.EndPage();
            }
            document.Close();
        }
        return stream.ToArray();
    }
    public string ExportSvg(DiagramPage page)
    {
        var output = new StringBuilder();
        using var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true });
        static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        writer.WriteStartElement("svg", "http://www.w3.org/2000/svg");
        writer.WriteAttributeString("viewBox", $"0 0 {N(page.Width)} {N(page.Height)}"); writer.WriteAttributeString("width", N(page.Width)); writer.WriteAttributeString("height", N(page.Height));
        writer.WriteStartElement("title"); writer.WriteString(page.Name); writer.WriteEndElement();
        void Path(SKPath path, string fill, string stroke, double width, double opacity = 1, bool dashed = false)
        {
            if (path.IsEmpty) return;
            writer.WriteStartElement("path"); writer.WriteAttributeString("d", path.ToSvgPathData());
            Paint("fill", fill); Paint("stroke", stroke); writer.WriteAttributeString("stroke-width", N(width)); writer.WriteAttributeString("stroke-linejoin", "round");
            if (path.FillType == SKPathFillType.EvenOdd) writer.WriteAttributeString("fill-rule", "evenodd");
            if (opacity < 1) writer.WriteAttributeString("opacity", N(opacity));
            if (dashed) writer.WriteAttributeString("stroke-dasharray", "6 4"); writer.WriteEndElement();
        }
        void Paint(string name, string color)
        {
            if (color == "none") { writer.WriteAttributeString(name, "none"); return; }
            var c = SKColor.Parse(color); writer.WriteAttributeString(name, $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}");
            if (c.Alpha < 255) writer.WriteAttributeString(name + "-opacity", N(c.Alpha / 255d));
        }
        writer.WriteStartElement("rect"); writer.WriteAttributeString("width", "100%"); writer.WriteAttributeString("height", "100%"); Paint("fill", page.Background); writer.WriteEndElement();
        bool Visible(string layer) => page.IsVisible(layer) && page.IsPrintable(layer);
        void Shape(Shape shape)
        {
            writer.WriteStartElement("g"); writer.WriteAttributeString("id", "shape-" + shape.Id);
            writer.WriteAttributeString("transform", $"rotate({N(shape.Rotation)} {N(shape.Bounds.Center.X)} {N(shape.Bounds.Center.Y)})");
            using var geometry = ShapeGeometry.Create(shape); using var details = ShapeGeometry.Details(shape);
            Path(geometry, shape.Kind == ShapeKind.Annotation ? "none" : shape.Style.Fill, shape.Style.Stroke, shape.Style.StrokeWidth, shape.Style.Opacity, shape.Style.Dashed);
            Path(details, "none", shape.Style.Stroke, shape.Style.StrokeWidth, shape.Style.Opacity);
            var layout = Layout(shape);
            if (layout.Lines.Count > 0)
            {
                writer.WriteStartElement("text"); writer.WriteAttributeString("font-family", shape.Style.FontFamily); writer.WriteAttributeString("font-size", N(shape.Style.FontSize)); Paint("fill", shape.Style.TextColor);
                writer.WriteAttributeString("text-anchor", layout.Left ? "start" : "middle");
                if (shape.Style.Bold) writer.WriteAttributeString("font-weight", "bold"); if (shape.Style.Italic) writer.WriteAttributeString("font-style", "italic");
                var y = layout.Y;
                foreach (var line in layout.Lines)
                {
                    writer.WriteStartElement("tspan"); writer.WriteAttributeString("x", N(layout.X)); writer.WriteAttributeString("y", N(y)); writer.WriteString(line); writer.WriteEndElement(); y += layout.LineHeight;
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        foreach (var shape in page.Shapes.Where(s => s.Kind == ShapeKind.Container && Visible(s.LayerId))) Shape(shape);
        ClearCache(); var routes = Routes(page, 0);
        foreach (var connector in page.Connectors.Where(c => Visible(c.LayerId)))
        {
            var route = routes[connector.Id]; using var line = Polyline(route.Points); Path(line, "none", connector.Color, connector.Width, dashed: connector.Dashed);
            if (route.Points.Count < 2) continue;
            using var start = ArrowPath(route.Points[1], route.Points[0], connector.StartArrow, connector.Width);
            using var end = ArrowPath(route.Points[^2], route.Points[^1], connector.EndArrow, connector.Width);
            Path(start, connector.StartArrow == ArrowHead.Open ? "none" : connector.Color, connector.Color, connector.Width);
            Path(end, connector.EndArrow == ArrowHead.Open ? "none" : connector.Color, connector.Color, connector.Width);
            if (!string.IsNullOrWhiteSpace(connector.Text))
            {
                var midpoint = route.Midpoint; var font = Font(new() { FontSize = 12 }); var width = font.MeasureText(connector.Text);
                writer.WriteStartElement("rect"); writer.WriteAttributeString("x", N(midpoint.X - width / 2 - 5)); writer.WriteAttributeString("y", N(midpoint.Y - 10)); writer.WriteAttributeString("width", N(width + 10)); writer.WriteAttributeString("height", "20"); writer.WriteAttributeString("fill", "white"); writer.WriteEndElement();
                writer.WriteStartElement("text"); writer.WriteAttributeString("x", N(midpoint.X)); writer.WriteAttributeString("y", N(midpoint.Y + 4)); writer.WriteAttributeString("text-anchor", "middle"); writer.WriteAttributeString("font-family", "Arial"); writer.WriteAttributeString("font-size", "12"); writer.WriteAttributeString("fill", "#405574"); writer.WriteString(connector.Text); writer.WriteEndElement();
            }
        }
        foreach (var shape in page.Shapes.Where(s => s.Kind != ShapeKind.Container && Visible(s.LayerId))) Shape(shape);
        writer.WriteEndElement(); writer.Flush(); return output.ToString();
    }
}
