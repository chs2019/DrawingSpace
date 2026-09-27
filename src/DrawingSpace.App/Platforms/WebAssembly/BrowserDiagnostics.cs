using System.Text;
using System.Text.Json;
using DrawingSpace.Controls;
using DrawingSpace.Editing;
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
                json.WriteString("tool", session.Tool.ToString()); json.WriteString("title", session.Document.Title);
                json.WriteNumber("nodes", session.Page.Shapes.Count); json.WriteNumber("edges", session.Page.Connectors.Count);
                json.WriteNumber("pages", session.Document.Pages.Count); json.WriteNumber("selection", session.Selection.Count);
                json.WriteNumber("zoom", session.Viewport.Zoom); json.WriteNumber("panX", session.Viewport.Pan.X); json.WriteNumber("panY", session.Viewport.Pan.Y);
                json.WriteNumber("canvasX", origin.X); json.WriteNumber("canvasY", origin.Y); json.WriteNumber("canvasWidth", surface.ActualWidth); json.WriteNumber("canvasHeight", surface.ActualHeight);
                json.WriteBoolean("canUndo", session.CanUndo); json.WriteBoolean("canRedo", session.CanRedo); json.WriteBoolean("editingText", surface.IsTextEditing); json.WriteString("status", workbench.StatusText);
                json.WriteStartArray("shapes");
                foreach (var shape in session.Page.Shapes)
                {
                    json.WriteStartObject(); json.WriteString("id", shape.Id); json.WriteString("name", shape.Name); json.WriteString("text", shape.Text); json.WriteString("kind", shape.Kind.ToString());
                    json.WriteNumber("x", shape.X); json.WriteNumber("y", shape.Y); json.WriteNumber("width", shape.Width); json.WriteNumber("height", shape.Height); json.WriteNumber("rotation", shape.Rotation);
                    json.WriteBoolean("selected", session.Selection.Contains(shape.Id)); json.WriteEndObject();
                }
                json.WriteEndArray(); json.WriteStartArray("elements");
                void Visit(DependencyObject node)
                {
                    if (node is UIElement { Visibility: Visibility.Collapsed }) return;
                    if (node is FrameworkElement element && element.ActualWidth > 0 && element.ActualHeight > 0)
                    {
                        var name = AutomationProperties.GetName(element);
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
                Visit(workbench); json.WriteEndArray(); json.WriteEndObject();
            }
            BrowserFiles.PublishDiagnostics(Encoding.UTF8.GetString(stream.ToArray()));
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => Publish(); window.Closed += (_, _) => timer.Stop(); timer.Start();
    }
}
