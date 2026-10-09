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
    public byte[] ExportPng(DiagramPage page, double scale = 2) => ExportPngCore(null, page, scale);

    public byte[] ExportPng(DiagramDocument document, DiagramPage page, double scale = 2)
    {
        DocumentCodec.Validate(document);
        return ExportPngCore(document, page, scale);
    }

    private byte[] ExportPngCore(DiagramDocument? document, DiagramPage page, double scale)
    {
        if (!double.IsFinite(scale) || scale is < .1 or > 4) throw new ArgumentOutOfRangeException(nameof(scale));
        var width = (int)Math.Ceiling(page.Width * scale); var height = (int)Math.Ceiling(page.Height * scale);
        if (width < 1 || height < 1 || (long)width * height > 64_000_000 || width > 16384 || height > 16384) throw new InvalidOperationException("The requested image exceeds the safe export size. Reduce the page size or export scale.");
        using var surface = SKSurface.Create(new SKImageInfo(width, height)) ?? throw new InvalidOperationException("Could not allocate the export surface.");
        surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.Scale((float)scale);
        ClearCache();
        if (document is null) DrawPage(surface.Canvas, page, 0, printing: true);
        else DrawDocumentPage(surface.Canvas, document, page, 0, printing: true);
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    public byte[] ExportPdf(DiagramDocument drawing)
    {
        DocumentCodec.Validate(drawing);
        using var stream = new MemoryStream();
        using (var document = SKDocument.CreatePdf(stream) ?? throw new InvalidOperationException("PDF export is not available on this platform."))
        {
            foreach (var page in drawing.Pages.Where(p => !p.IsBackground))
            {
                var canvas = document.BeginPage((float)page.Width * .75f, (float)page.Height * .75f);
                canvas.Scale(.75f); canvas.ClipRect(ShapeGeometry.Rect(page.Bounds)); ClearCache();
                DrawDocumentPage(canvas, drawing, page, 0, printing: true); document.EndPage();
            }
            document.Close();
        }
        return stream.ToArray();
    }
    public string ExportSvg(DiagramPage page) => ExportSvgCore(null, page);

    public string ExportSvg(DiagramDocument document, DiagramPage page)
    {
        DocumentCodec.Validate(document);
        return ExportSvgCore(document, page);
    }

    private string ExportSvgCore(DiagramDocument? document, DiagramPage page)
    {
        const string ns = "http://www.w3.org/2000/svg";
        var output = new StringBuilder();
        using var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true });
        static string N(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
        void Start(string name) => writer.WriteStartElement(name, ns);
        void Attribute(string name, string value) => writer.WriteAttributeString(name, value);
        void Paint(string name, string color)
        {
            if (color == "none") { Attribute(name, "none"); return; }
            var c = SKColor.Parse(color); Attribute(name, $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}");
            if (c.Alpha < 255) Attribute(name + "-opacity", N(c.Alpha / 255d));
        }
        void Path(SKPath path, string fill, string stroke, double width, double opacity = 1, bool dashed = false)
        {
            if (path.IsEmpty) return;
            Start("path"); Attribute("d", path.ToSvgPathData()); Paint("fill", fill); Paint("stroke", stroke);
            Attribute("stroke-width", N(width)); Attribute("stroke-linejoin", "round");
            if (path.FillType == SKPathFillType.EvenOdd) Attribute("fill-rule", "evenodd");
            if (opacity < 1) Attribute("opacity", N(opacity));
            if (dashed) Attribute("stroke-dasharray", "6 4"); writer.WriteEndElement();
        }
        void Rect(RectD bounds)
        {
            Attribute("x", N(bounds.X)); Attribute("y", N(bounds.Y)); Attribute("width", N(bounds.Width)); Attribute("height", N(bounds.Height));
        }
        Start("svg"); Attribute("viewBox", $"0 0 {N(page.Width)} {N(page.Height)}"); Attribute("width", N(page.Width)); Attribute("height", N(page.Height));
        Attribute("role", "img"); Attribute("aria-label", page.Name); Attribute("overflow", "hidden");
        Start("title"); writer.WriteString(page.Name); writer.WriteEndElement();
        Start("rect"); Rect(page.Bounds); Paint("fill", page.Background); writer.WriteEndElement();

        void Text(Shape shape, string clipId)
        {
            if (string.IsNullOrEmpty(shape.Text)) return;
            var layout = TextEngine.Layout(shape);
            Start("g"); Attribute("transform", $"translate({N(shape.X)} {N(shape.Y)}) rotate({N(layout.Rotation)} {N(layout.TextBounds.Center.X)} {N(layout.TextBounds.Center.Y)})");
            Start("defs"); Start("clipPath"); Attribute("id", clipId); Attribute("clipPathUnits", "userSpaceOnUse");
            Start("rect"); Rect(layout.TextBounds); writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
            Start("g"); Attribute("clip-path", $"url(#{clipId})"); Attribute("aria-label", shape.Text);
            // Text is outlined intentionally: exported glyph positions match HarfBuzz without font redistribution.
            foreach (var outline in layout.GetOutlines())
            {
                if (string.IsNullOrEmpty(outline.PathData)) continue;
                Start("path"); Attribute("d", outline.PathData);
                var c = outline.Color; Attribute("fill", $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}");
                if (c.Alpha < 255) Attribute("fill-opacity", N(c.Alpha / 255d));
                writer.WriteEndElement();
            }
            writer.WriteEndElement(); writer.WriteEndElement();
        }
        void Shape(Shape shape, string prefix)
        {
            Start("g"); Attribute("id", prefix + "shape-" + shape.Id); Attribute("role", "img"); Attribute("aria-label", string.IsNullOrEmpty(shape.Text) ? shape.Name : shape.Text);
            var shapeFill = DataGraphicProjection.Fill(shape);
            var m = shape.DrawingMatrix; Attribute("transform", $"matrix({N(m.A)} {N(m.B)} {N(m.C)} {N(m.D)} {N(m.Tx)} {N(m.Ty)})");
            Start("title"); writer.WriteString(string.IsNullOrEmpty(shape.Text) ? shape.Name : shape.Text); writer.WriteEndElement();
            if (shape.Geometry.Count > 0)
            {
                foreach (var figure in shape.Geometry)
                {
                    using var path = ShapeGeometry.CreateFigure(shape, figure);
                    Path(path, figure.Filled ? shapeFill : "none", figure.Stroked ? shape.Style.Stroke : "none", shape.Style.StrokeWidth, shape.Style.Opacity, shape.Style.Dashed);
                }
            }
            else
            {
                using var geometry = ShapeGeometry.Create(shape).Snapshot(); using var details = ShapeGeometry.Details(shape);
                Path(geometry, shape.Kind == ShapeKind.Annotation ? "none" : shapeFill, shape.Style.Stroke, shape.Style.StrokeWidth, shape.Style.Opacity, shape.Style.Dashed);
                Path(details, "none", shape.Style.Stroke, shape.Style.StrokeWidth, shape.Style.Opacity);
            }
            if (shape.ImageData is { Length: > 0 } bytes && shape.ImageContentType is "image/png" or "image/jpeg" or "image/webp")
            {
                Start("image"); Rect(shape.Bounds); Attribute("preserveAspectRatio", "none"); Attribute("opacity", N(shape.Style.Opacity));
                Attribute("href", "data:" + shape.ImageContentType + ";base64," + Convert.ToBase64String(bytes)); writer.WriteEndElement();
            }
            Text(shape, prefix + "clip-" + shape.Id);
            if (shape.DataGraphics.Count > 0)
            {
                Start("g"); Attribute("data-graphics-for", shape.Id);
                Attribute("transform", $"translate({N(shape.X)} {N(shape.Y)})");
                foreach (var graphic in DataGraphicsFor(shape)) Shape(graphic, prefix);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        var pages = new List<DiagramPage>(); var seen = new HashSet<string>(StringComparer.Ordinal); var current = page;
        while (seen.Add(current.Id))
        {
            pages.Add(current);
            if (document is null || current.BackgroundPageId is not { } id || document.Pages.FirstOrDefault(p => p.Id == id) is not { } background) break;
            current = background;
        }
        foreach (var part in pages.AsEnumerable().Reverse())
        {
            bool Visible(string layer) => part.IsVisible(layer) && part.IsPrintable(layer);
            var prefix = "page-" + part.Id + "-";
            Start("g"); Attribute("id", prefix + "content");
            foreach (var shape in part.Shapes.Where(s => s.Kind == ShapeKind.Container && Visible(s.LayerId))) Shape(shape, prefix);
            ClearCache(); var routes = Routes(part, 0, printing: true);
            // Non-printable connectors cannot introduce jumps into a printed/exported route.
            var jumps = _jumps;
            foreach (var connector in part.Connectors.Where(c => Visible(c.LayerId)))
            {
                if (!routes.TryGetValue(connector.Id, out var route) || route.Points.Count < 2) continue;
                using var line = ConnectorPath(connector, route, jumps.GetValueOrDefault(connector.Id));
                Path(line, "none", connector.Color, connector.Width, dashed: connector.Dashed);
                using var start = ArrowPath(route.Points[1], route.Points[0], connector.StartArrow, connector.Width);
                using var end = ArrowPath(route.Points[^2], route.Points[^1], connector.EndArrow, connector.Width);
                Path(start, connector.StartArrow == ArrowHead.Open ? "none" : connector.Color, connector.Color, connector.Width);
                Path(end, connector.EndArrow == ArrowHead.Open ? "none" : connector.Color, connector.Color, connector.Width);
                if (!string.IsNullOrWhiteSpace(connector.Text))
                {
                    var midpoint = LineJumpService.LabelPoint(connector, route); var font = Font(new() { FontSize = 12 }); var label = connector.Text.Length > 120 ? connector.Text[..120] + "…" : connector.Text;
                    var width = font.MeasureText(label);
                    Start("rect"); Rect(new(midpoint.X - width / 2 - 5, midpoint.Y - 10, width + 10, 20)); Paint("fill", "#FFFFFF"); writer.WriteEndElement();
                    Start("text"); Attribute("x", N(midpoint.X)); Attribute("y", N(midpoint.Y + 4)); Attribute("text-anchor", "middle"); Attribute("font-family", "Arial"); Attribute("font-size", "12"); Paint("fill", "#405574"); writer.WriteString(label); writer.WriteEndElement();
                }
            }
            foreach (var shape in part.Shapes.Where(s => s.Kind != ShapeKind.Container && Visible(s.LayerId))) Shape(shape, prefix);
            writer.WriteEndElement();
        }
        writer.WriteEndElement(); writer.Flush(); return output.ToString();
    }
}
