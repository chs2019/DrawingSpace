using DrawingSpace.Routing;
using DrawingSpace.Skia;
using SkiaSharp;

namespace DrawingSpace.Editor;

public sealed partial class DiagramSurface
{
    private string? _editedConnectorId;
    private int _editedWaypoint;
    private bool _editingSource;
    private Connector? _connectorOriginal;
    private readonly Dictionary<string, Connector> _originalConnectors = [];

    private void CaptureConnectorTransforms()
    {
        _originalConnectors.Clear();
        if (Session is not { } session) return;
        foreach (var edge in session.Page.Connectors)
        {
            if (session.Page.Layers.FirstOrDefault(l => l.Id == edge.LayerId)?.Locked == true) continue;
            if (session.Selection.Contains(edge.Id) || edge.SourceId is not null && edge.TargetId is not null && _originals.ContainsKey(edge.SourceId) && _originals.ContainsKey(edge.TargetId))
                _originalConnectors[edge.Id] = edge.Clone();
        }
    }
    private void MoveInternalConnectors(PointD delta)
    {
        if (Session is not { } session) return;
        foreach (var (id, original) in _originalConnectors)
        {
            if (session.Page.Connectors.FirstOrDefault(c => c.Id == id) is not { } edge) continue;
            edge.Waypoints = original.Waypoints.Select(p => p + delta).ToList();
            if (original.SourceId is null) edge.Start = original.Start + delta;
            if (original.TargetId is null) edge.End = original.End + delta;
        }
    }
    private bool TryBeginConnectorEdit(PointD screen, PointD world, VirtualKeyModifiers modifiers)
    {
        if (Session is not { SelectedConnectors.Count: 1 } session) return false;
        var edge = session.SelectedConnectors[0];
        if (session.Page.Layers.FirstOrDefault(l => l.Id == edge.LayerId)?.Locked == true) return false;
        if (!Renderer.Routes(session.Page, session.Revision).TryGetValue(edge.Id, out var route) || route.Points.Count < 2) return false;
        bool Near(PointD point) => session.Viewport.ToScreen(point).Distance(screen) <= 8;
        _editedConnectorId = edge.Id; _connectorOriginal = edge.Clone();
        if (Near(route.Points[0]) || Near(route.Points[^1]))
        {
            _editingSource = Near(route.Points[0]); session.Begin("Reconnect endpoint"); _gesture = Gesture.ConnectorEndpoint; return true;
        }
        for (var index = 0; index < edge.Waypoints.Count; index++)
        {
            if (!Near(edge.Waypoints[index])) continue;
            if (IsShiftDown || modifiers.HasFlag(VirtualKeyModifiers.Shift) || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            {
                var remove = index; session.Execute("Remove waypoint", () => edge.Waypoints.RemoveAt(remove)); ReleaseCaptures(); return true;
            }
            _editedWaypoint = index; session.Begin("Move waypoint"); _gesture = Gesture.ConnectorWaypoint; return true;
        }
        if (!string.IsNullOrEmpty(edge.Text) && Near(LineJumpService.LabelPoint(edge, route)))
        { session.Begin("Move connector label"); _gesture = Gesture.ConnectorLabel; return true; }
        var altDown = IsAltDown || modifiers.HasFlag(VirtualKeyModifiers.Menu)
            || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var gripSegment = Enumerable.Range(1, route.Points.Count - 1).FirstOrDefault(i =>
            route.Points[i - 1].Distance(route.Points[i]) * session.Viewport.Zoom >= 28
            && Near((route.Points[i - 1] + route.Points[i]) / 2));
        if ((altDown || gripSegment > 0) && edge.Waypoints.Count < 4096)
        {
            var segment = Enumerable.Range(1, route.Points.Count - 1)
                .MinBy(i => PointD.DistanceToSegment(world, route.Points[i - 1], route.Points[i]));
            if (PointD.DistanceToSegment(world, route.Points[segment - 1], route.Points[segment]) <= 8 / session.Viewport.Zoom)
            {
                var at = Along(route, world);
                _editedWaypoint = edge.Waypoints.Count(p => Along(route, p) < at);
                session.Begin("Insert waypoint"); edge.Waypoints.Insert(_editedWaypoint, world); _gesture = Gesture.ConnectorWaypoint; session.Preview(); return true;
            }
        }
        _editedConnectorId = null; return false;
    }
    private static double Along(RouteResult route, PointD point)
    {
        var best = double.MaxValue; var result = 0d; var walked = 0d;
        for (var i = 1; i < route.Points.Count; i++)
        {
            var a = route.Points[i - 1]; var v = route.Points[i] - a; var length = v.Length;
            var t = length < 1e-9 ? 0 : Math.Clamp(((point.X - a.X) * v.X + (point.Y - a.Y) * v.Y) / (length * length), 0, 1);
            var distance = (a + v * t).Distance(point);
            if (distance < best) { best = distance; result = walked + length * t; }
            walked += length;
        }
        return result;
    }
    private void MoveConnectorEdit(PointD world)
    {
        if (Session is not { } session || session.Page.Connectors.FirstOrDefault(c => c.Id == _editedConnectorId) is not { } edge) return;
        try
        {
            switch (_gesture)
            {
                case Gesture.ConnectorEndpoint:
                    var target = ConnectionEndpoints.Hit(session.Page, world, 14 / session.Viewport.Zoom, _editingSource);
                    if (target is null && Renderer.HitShape(session.Page, world) is { } shape && !session.Page.IsLocked(shape))
                        target = ConnectionEndpoints.Resolve(shape, null, NearestPort(shape, world), world, world);
                    ConnectionEndpoints.Attach(edge, _editingSource, target, world); break;
                case Gesture.ConnectorWaypoint:
                    if (_editedWaypoint < edge.Waypoints.Count)
                        edge.Waypoints[_editedWaypoint] = session.SnapToGrid ? new(Math.Round(world.X / session.GridSize) * session.GridSize, Math.Round(world.Y / session.GridSize) * session.GridSize) : world;
                    break;
                case Gesture.ConnectorLabel:
                    edge.LabelOffset = _connectorOriginal!.LabelOffset + world - _startWorld; break;
            }
            session.Preview();
        }
        catch (Exception ex) { CancelGesture(); StatusChanged?.Invoke(ex.Message); }
    }
    private void DrawConnectorAdorners(SKCanvas canvas)
    {
        if (Session is not { } session) return;
        using var outline = new SKPaint { Color = SKColor.Parse("#2B579A"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        foreach (var edge in session.SelectedConnectors)
        {
            if (!Renderer.Routes(session.Page, session.Revision).TryGetValue(edge.Id, out var route)) continue;
            // Hollow midpoint grips also expose waypoint insertion to touch and pen users.
            for (var index = 1; index < route.Points.Count; index++)
            {
                var a = route.Points[index - 1]; var b = route.Points[index];
                if (a.Distance(b) * session.Viewport.Zoom < 28) continue;
                var p = session.Viewport.ToScreen((a + b) / 2);
                if (edge.Text.Length > 0 && session.Viewport.ToScreen(LineJumpService.LabelPoint(edge, route)).Distance(p) < 12) continue;
                canvas.DrawCircle((float)p.X, (float)p.Y, 3, white);
                canvas.DrawCircle((float)p.X, (float)p.Y, 3, outline);
            }
            foreach (var waypoint in edge.Waypoints)
            {
                var p = session.Viewport.ToScreen(waypoint); var rect = new SKRect((float)p.X - 4, (float)p.Y - 4, (float)p.X + 4, (float)p.Y + 4);
                canvas.DrawRect(rect, white); canvas.DrawRect(rect, outline);
            }
            if (edge.Text.Length > 0)
            {
                var p = session.Viewport.ToScreen(LineJumpService.LabelPoint(edge, route));
                using var diamond = SceneRenderer.Polyline([p + new PointD(0, -6), p + new PointD(6, 0), p + new PointD(0, 6), p + new PointD(-6, 0), p + new PointD(0, -6)]);
                canvas.DrawPath(diamond, white); canvas.DrawPath(diamond, outline);
            }
        }
        var candidates = _gesture == Gesture.ConnectorEndpoint ? session.Page.Shapes.Where(s => session.Page.IsVisible(s.LayerId)) : session.SelectedShapes;
        foreach (var shape in candidates)
            foreach (var port in shape.ConnectionPoints)
            {
                var p = session.Viewport.ToScreen(shape.WorldMatrix.Map(port.Position));
                canvas.DrawLine((float)p.X - 3, (float)p.Y - 3, (float)p.X + 3, (float)p.Y + 3, outline);
                canvas.DrawLine((float)p.X - 3, (float)p.Y + 3, (float)p.X + 3, (float)p.Y - 3, outline);
            }
    }
}
