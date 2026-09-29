using System.Text;
using System.Text.Json;
using DrawingSpace.Controls;
using DrawingSpace.Editing;
using DrawingSpace.Routing;
using DrawingSpace.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DrawingSpace.App;

/// <summary>Opt-in read-only observations for pointer-driven acceptance tests, never an editing backdoor.</summary>
internal static class BrowserDiagnostics
{
    public static void Attach(EditorSession session, DiagramWorkbench workbench, Window window)
    {
        if (!BrowserFiles.IsTestMode()) return;
        void Publish()
        {
            if (workbench.XamlRoot is null) return;
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                var surface = workbench.Surface; var origin = surface.TransformToVisual(null).TransformPoint(new Point());
                json.WriteStartObject(); json.WriteBoolean("ready", surface.ActualWidth > 0 && surface.ActualHeight > 0);
                json.WriteString("gesture", surface.ActiveGesture); json.WriteString("lastPointerInput", surface.LastPointerInput);
                json.WriteString("tool", session.Tool.ToString()); json.WriteString("title", session.Document.Title);
                json.WriteNumber("nodes", session.Page.Shapes.Count); json.WriteNumber("edges", session.Page.Connectors.Count);
                json.WriteNumber("masters", session.Document.Masters.Count); json.WriteNumber("groups", session.Page.Groups.Count); json.WriteBoolean("dirty", session.IsDirty); json.WriteNumber("pages", session.Document.Pages.Count); json.WriteNumber("selection", session.Selection.Count);
                var textFont = surface.Renderer.Font(new());
                json.WriteString("fontFamily", textFont.Typeface.FamilyName);
                json.WriteNumber("fontWidthRatio", textFont.MeasureText("WWW") / Math.Max(.01f, textFont.MeasureText("iii")));
                json.WriteNumber("zoom", session.Viewport.Zoom); json.WriteNumber("panX", session.Viewport.Pan.X); json.WriteNumber("panY", session.Viewport.Pan.Y);
                json.WriteNumber("canvasX", origin.X); json.WriteNumber("canvasY", origin.Y); json.WriteNumber("canvasWidth", surface.ActualWidth); json.WriteNumber("canvasHeight", surface.ActualHeight);
                json.WriteBoolean("canUndo", session.CanUndo); json.WriteBoolean("canRedo", session.CanRedo); json.WriteBoolean("editingText", surface.IsTextEditing); json.WriteString("status", workbench.StatusText);
                json.WriteStartArray("shapes");
                foreach (var shape in session.Page.Shapes)
                {
                    json.WriteStartObject(); json.WriteString("id", shape.Id); json.WriteString("name", shape.Name); json.WriteString("text", shape.Text); json.WriteString("kind", shape.Kind.ToString());
                    json.WriteNumber("x", shape.X); json.WriteNumber("y", shape.Y); json.WriteNumber("width", shape.Width); json.WriteNumber("height", shape.Height); json.WriteNumber("rotation", shape.Rotation);
                    json.WriteString("masterId", shape.MasterId); json.WriteString("containerId", shape.ContainerId);
                    json.WriteString("dataSource", shape.DataBinding?.SourceId);
                    json.WriteString("dataRowKey", shape.DataBinding?.RowKey);
                    json.WriteString("baseFill", shape.Style.Fill);
                    json.WriteString("effectiveFill", DrawingSpace.Documents.DataGraphicProjection.Fill(shape));
                    json.WriteStartObject("data");
                    foreach (var (key, value) in shape.Data) json.WriteString(key, value);
                    json.WriteEndObject();
                    json.WriteStartArray("dataGraphics");
                    foreach (var rule in shape.DataGraphics) json.WriteStringValue(rule.Kind.ToString());
                    json.WriteEndArray();
                    json.WriteNumber("textSpans", shape.TextSpans.Count); json.WriteNumber("threads", shape.Threads.Count);
                    json.WriteBoolean("rangeBold", shape.TextSpans.Any(s => s.Bold == true)); json.WriteNumber("connectionPoints", shape.ConnectionPoints.Count);
                    json.WriteBoolean("selected", session.Selection.Contains(shape.Id)); json.WriteEndObject();
                }
                json.WriteEndArray(); json.WriteStartArray("connectors");
                var routes = surface.Renderer.Routes(session.Page, session.Revision);
                foreach (var edge in session.Page.Connectors)
                {
                    json.WriteStartObject(); json.WriteString("id", edge.Id); json.WriteString("sourceId", edge.SourceId); json.WriteString("targetId", edge.TargetId);
                    json.WriteString("sourcePointId", edge.SourcePointId); json.WriteString("targetPointId", edge.TargetPointId);
                    json.WriteString("text", edge.Text); json.WriteBoolean("selected", session.Selection.Contains(edge.Id)); json.WriteNumber("waypointCount", edge.Waypoints.Count);
                    json.WriteString("jumps", edge.LineJumps.ToString());
                    void Point(string name, DrawingSpace.Core.PointD point)
                    { json.WriteStartObject(name); json.WriteNumber("x", point.X); json.WriteNumber("y", point.Y); json.WriteEndObject(); }
                    Point("start", edge.Start); Point("end", edge.End);
                    json.WriteStartArray("waypoints"); foreach (var point in edge.Waypoints) { json.WriteStartObject(); json.WriteNumber("x", point.X); json.WriteNumber("y", point.Y); json.WriteEndObject(); } json.WriteEndArray();
                    if (routes.TryGetValue(edge.Id, out var route))
                    {
                        Point("label", LineJumpService.LabelPoint(edge, route)); json.WriteStartArray("route");
                        foreach (var point in route.Points) { json.WriteStartObject(); json.WriteNumber("x", point.X); json.WriteNumber("y", point.Y); json.WriteEndObject(); }
                        json.WriteEndArray();
                    }
                    json.WriteEndObject();
                }
                json.WriteEndArray(); json.WriteStartArray("elements");
                var visited = new HashSet<DependencyObject>();
                void Visit(DependencyObject node)
                {
                    if (!visited.Add(node)) return;
                    if (node is UIElement { Visibility: Visibility.Collapsed }) return;
                    if (node is FrameworkElement element && element.ActualWidth > 0 && element.ActualHeight > 0)
                    {
                        var name = AutomationProperties.GetName(element);
                        if (string.IsNullOrEmpty(name) && element is Microsoft.UI.Xaml.Controls.Button button && button.Content is string label) name = label;
                        if (!string.IsNullOrEmpty(name))
                        {
                            var p = element.TransformToVisual(null).TransformPoint(new Point());
                            json.WriteStartObject(); json.WriteString("name", name); json.WriteString("type", element.GetType().Name);
                            json.WriteNumber("x", p.X); json.WriteNumber("y", p.Y); json.WriteNumber("width", element.ActualWidth); json.WriteNumber("height", element.ActualHeight);
                            json.WriteBoolean("enabled", element is not Microsoft.UI.Xaml.Controls.Control control || control.IsEnabled); json.WriteEndObject();
                        }
                    }
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i));
                }
                Visit(workbench); foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(workbench.XamlRoot)) if (popup.Child is not null) Visit(popup.Child); json.WriteEndArray(); json.WriteEndObject();
            }
            BrowserFiles.PublishDiagnostics(Encoding.UTF8.GetString(stream.ToArray()));
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => Publish(); window.Closed += (_, _) => timer.Stop(); timer.Start();
    }
}
