using DrawingSpace.Controls;
using DrawingSpace.Routing;
using DrawingSpace.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;

namespace DrawingSpace.Editor;

/// <summary>Reusable diagram editor. Transient pointers never outlive their document transaction.</summary>
public sealed partial class DiagramSurface : UserControl, IDisposable
{
    private sealed class DrawingCanvas : SKCanvasElement
    {
        public Action<SKCanvas, Size>? Draw { get; set; }
        protected override void RenderOverride(SKCanvas canvas, Size area) => Draw?.Invoke(canvas, area);
    }
    private enum Gesture { None, Move, Resize, Rotate, Marquee, Pan, Create, Connect, Pinch, ConnectorEndpoint, ConnectorWaypoint, ConnectorLabel, ConnectorSegment, SelectionResize, SelectionRotate }
    private readonly DrawingCanvas _canvas = new();
    private readonly Canvas _overlay = new();
    private readonly Dictionary<string, Shape> _originals = [];
    private readonly Dictionary<uint, PointD> _touches = [];
    private EditorSession? _session;
    private Gesture _gesture;
    private PointD _startScreen, _startWorld, _lastWorld, _startPan;
    private RectD _startBounds;
    private RectD? _marquee;
    private SnapResult? _snap;
    private Shape? _created, _ghost;
    private string? _connectSource;
    private PortSide _connectPort;
    private int _handle;
    private bool _releasing, _disposed;
    private double _pinchDistance, _pinchZoom;
    private PointD _pinchWorld;
    private TextBox? _textEditor;
    private string? _textObjectId;
    public SceneRenderer Renderer { get; } = new();
    public bool IsSpaceDown { get; set; }
    public bool IsAltDown { get; set; }
    public bool IsShiftDown { get; set; }
    public string ActiveGesture => _gesture.ToString();
    public string LastPointerInput { get; private set; } = "";
    public void ResetModifierKeys() { IsSpaceDown = false; IsAltDown = false; IsShiftDown = false; }
    public bool IsTextEditing => _textEditor is not null;
    public event Action<PointD>? CursorChanged;
    public event Action<Point>? ContextRequested;
    public event Action<string>? StatusChanged;
    public EditorSession? Session
    {
        get => _session;
        set
        {
            if (_session == value) return;
            CancelGesture(); FinishTextEdit(false);
            if (_session is not null) _session.Changed -= SessionChanged;
            _session = value;
            if (_session is not null) _session.Changed += SessionChanged;
            Invalidate();
        }
    }
    public DiagramSurface()
    {
        IsTabStop = true; HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Drawing canvas");
        var root = new Grid { Background = OfficeTheme.Brush("#ECECEC") }; root.Children.Add(_canvas); root.Children.Add(_overlay); Content = root;
        _canvas.Draw = Paint;
        _canvas.PointerPressed += Pressed; _canvas.PointerMoved += Moved; _canvas.PointerReleased += Released;
        _canvas.PointerCanceled += (_, _) => CancelGesture();
        _canvas.PointerCaptureLost += (_, _) => { if (!_releasing && _gesture != Gesture.None) CancelGesture(); };
        _canvas.PointerWheelChanged += Wheel;
        _canvas.DoubleTapped += (_, e) =>
        {
            if (Session is not { } session) return;
            var p = e.GetPosition(_canvas); var world = session.Viewport.ToWorld(new(p.X, p.Y));
            var shape = Renderer.HitShape(session.Page, world, revision: session.Revision);
            var edge = shape is null ? Renderer.HitConnector(session.Page, world, session.Revision, 6 / session.Viewport.Zoom) : null;
            var id = shape?.Id ?? edge?.Id;
            if (id is not null) { BeginTextEdit(id); e.Handled = true; }
        };
        _canvas.RightTapped += (_, e) =>
        {
            if (Session is not { } session) return;
            var p = e.GetPosition(_canvas); var world = session.Viewport.ToWorld(new(p.X, p.Y));
            var id = Renderer.HitShape(session.Page, world, revision: session.Revision)?.Id ?? Renderer.HitConnector(session.Page, world, session.Revision, 6 / session.Viewport.Zoom)?.Id;
            if (id is not null && !session.Selection.Contains(id)) session.Select(id);
            ContextRequested?.Invoke(e.GetPosition(this)); e.Handled = true;
        };
        _canvas.SizeChanged += (_, _) => Invalidate();
        Loaded += (_, _) => DispatcherQueue.TryEnqueue(() => { if (Session is not null) Fit(); });
    }
    private void SessionChanged(ChangeKind kind)
    {
        if (kind == ChangeKind.Document && Session?.IsInteracting != true)
        {
            _gesture = Gesture.None; _originals.Clear(); _originalConnectors.Clear(); _editedConnectorId = null; _marquee = null; _snap = null; _created = null; _connectSource = null;
            ResetAdvancedGestures(); ReleaseCaptures(); Renderer.ClearCache();
            if (_textEditor is not null) FinishTextEdit(false);
        }
        if (kind == ChangeKind.Tool) { CancelGesture(); FinishTextEdit(true); }
        Invalidate();
    }
    public void Invalidate() => _canvas.Invalidate();
    public void FocusCanvas() => Focus(FocusState.Programmatic);
    public void Fit(bool selection = false)
    {
        if (Session is not { } session || ActualWidth < 50 || ActualHeight < 50) return;
        FinishTextEdit(true);
        var bounds = selection && session.SelectedShapes.Count > 0 ? session.SelectedShapes.Select(s => s.WorldBounds).Aggregate(RectD.Union).Inflate(32) : session.Page.Bounds;
        session.Viewport.Fit(bounds, ActualWidth, ActualHeight, 42); session.Notify(ChangeKind.Viewport);
    }
    public void ZoomAt(double zoom, PointD? anchor = null)
    {
        if (Session is not { } session) return;
        FinishTextEdit(true); session.Viewport.ZoomAt(zoom, anchor ?? new(ActualWidth / 2, ActualHeight / 2)); session.Notify(ChangeKind.Viewport);
    }
    public PointD ViewCenter => Session?.Viewport.ToWorld(new(ActualWidth / 2, ActualHeight / 2)) ?? new(400, 300);
    public void SetGhost(Shape? shape) { _ghost = shape; Invalidate(); }
    public void CancelGesture()
    {
        _gesture = Gesture.None; _originals.Clear(); _originalConnectors.Clear(); _editedConnectorId = null; _marquee = null; _snap = null; _created = null; _connectSource = null;
        ResetAdvancedGestures();
        if (Session?.IsInteracting == true) Session.Cancel();
        ReleaseCaptures(); Invalidate();
    }
    private void ReleaseCaptures()
    {
        _releasing = true;
        try { _canvas.ReleasePointerCaptures(); }
        finally { _releasing = false; }
    }
    private void FailGesture(Exception error)
    {
        CancelGesture(); StatusChanged?.Invoke(error.Message);
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        try { PressedCore(sender, e); }
        catch (Exception error) { FailGesture(error); e.Handled = true; }
    }
    private void PressedCore(object sender, PointerRoutedEventArgs e)
    {
        if (Session is not { } session) return;
        var pointer = e.GetCurrentPoint(_canvas);
        LastPointerInput = $"{pointer.Position.X:0.##},{pointer.Position.Y:0.##} modifiers={e.KeyModifiers} alt={IsAltDown} shift={IsShiftDown}";
        if (pointer.Properties.IsRightButtonPressed) return;
        var screen = new PointD(pointer.Position.X, pointer.Position.Y); var world = session.Viewport.ToWorld(screen);
        FinishTextEdit(true); FocusCanvas();
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
        {
            _touches[e.Pointer.PointerId] = screen;
            if (_touches.Count == 2)
            {
                CancelGesture(); _gesture = Gesture.Pinch;
                var touches = _touches.Values.ToArray(); var center = (touches[0] + touches[1]) / 2;
                _pinchDistance = Math.Max(1, touches[0].Distance(touches[1])); _pinchZoom = session.Viewport.Zoom; _pinchWorld = session.Viewport.ToWorld(center);
                _canvas.CapturePointer(e.Pointer); e.Handled = true; return;
            }
        }
        _startScreen = screen; _startWorld = _lastWorld = world; _startPan = session.Viewport.Pan;
        _canvas.CapturePointer(e.Pointer); e.Handled = true;
        if (IsSpaceDown || session.Tool == EditorTool.Pan || pointer.Properties.IsMiddleButtonPressed) { _gesture = Gesture.Pan; return; }
        if (session.RulersVisible && (screen.X < 22 || screen.Y < 22)) { _gesture = Gesture.None; return; }
        if (session.Tool == EditorTool.Pointer && TryBeginSelectionTransform(screen)) return;
        if (session.Tool == EditorTool.Pointer && session.SelectedShapes.Count == 1 && !session.Page.IsLocked(session.SelectedShapes[0]))
        {
            var selected = session.SelectedShapes[0]; var handles = ShapeTransforms.Handles(selected);
            for (var i = 0; i < handles.Length; i++)
            {
                if (session.Viewport.ToScreen(handles[i]).Distance(screen) > 7) continue;
                session.Begin("Resize shape"); _originals[selected.Id] = selected.Clone(); _handle = i; _gesture = Gesture.Resize; return;
            }
            if (RotationHandle(selected).Distance(screen) < 8)
            {
                session.Begin("Rotate shape"); _originals[selected.Id] = selected.Clone(); _gesture = Gesture.Rotate; return;
            }
            if (session.AutoConnect)
            {
                foreach (var side in new[] { PortSide.North, PortSide.East, PortSide.South, PortSide.West })
                {
                    if (AutoConnectPosition(selected, side).Distance(screen) > 9) continue;
                    ReleaseCaptures(); session.AddConnected(selected, side); return;
                }
            }
        }
        if (session.Tool == EditorTool.Pointer && TryBeginConnectorEdit(screen, world, e.KeyModifiers)) return;
        var hit = Renderer.HitShape(session.Page, world, 3 / session.Viewport.Zoom, session.Revision);
        if (session.Tool == EditorTool.Connector)
        {
            _connectSource = hit?.Id; _connectPort = hit is null ? PortSide.Auto : NearestPort(hit, world);
            _gesture = Gesture.Connect; return;
        }
        if (session.Tool is EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Text)
        {
            var kind = session.Tool == EditorTool.Rectangle ? ShapeKind.Rectangle : session.Tool == EditorTool.Ellipse ? ShapeKind.Ellipse : ShapeKind.Text;
            session.Begin("Draw " + kind);
            _created = new() { Kind = kind, Name = kind.ToString(), Text = kind == ShapeKind.Text ? "Text" : "", X = world.X, Y = world.Y, Width = 16, Height = 16, LayerId = session.Page.Layers[0].Id };
            if (kind == ShapeKind.Text) { _created.Style.Fill = "#00FFFFFF"; _created.Style.Stroke = "#00FFFFFF"; }
            session.Page.Shapes.Add(_created); session.Selection.Clear(); session.Selection.Add(_created.Id); _gesture = Gesture.Create; session.Preview(); return;
        }
        var additive = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);
        if (hit is not null)
        {
            if (!session.Selection.Contains(hit.Id) || additive) session.Select(hit.Id, additive);
            if (!session.Selection.Contains(hit.Id) || session.Page.IsLocked(hit)) { _gesture = Gesture.None; return; }
            foreach (var shape in session.TransformShapes) _originals[shape.Id] = shape.Clone();
            CaptureConnectorTransforms();
            if (_originals.Count > 0)
            {
                _startBounds = _originals.Values.Select(s => s.WorldBounds).Aggregate(RectD.Union);
                session.Begin("Move shapes"); _gesture = Gesture.Move;
            }
            return;
        }
        var edge = Renderer.HitConnector(session.Page, world, session.Revision, 6 / session.Viewport.Zoom);
        if (edge is not null) { session.Select(edge.Id, additive); _gesture = Gesture.None; return; }
        if (!additive) session.Select(null);
        _gesture = Gesture.Marquee; _marquee = new(world.X, world.Y, 0, 0);
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        try { MovedCore(sender, e); }
        catch (Exception error) { FailGesture(error); e.Handled = true; }
    }
    private void MovedCore(object sender, PointerRoutedEventArgs e)
    {
        if (Session is not { } session) return;
        var pointer = e.GetCurrentPoint(_canvas); var screen = new PointD(pointer.Position.X, pointer.Position.Y); var world = session.Viewport.ToWorld(screen);
        _lastWorld = world; CursorChanged?.Invoke(world);
        if (_touches.ContainsKey(e.Pointer.PointerId)) _touches[e.Pointer.PointerId] = screen;
        if (_gesture == Gesture.Pinch && _touches.Count >= 2)
        {
            var touches = _touches.Values.Take(2).ToArray(); var center = (touches[0] + touches[1]) / 2;
            session.Viewport.Zoom = _pinchZoom * touches[0].Distance(touches[1]) / _pinchDistance;
            session.Viewport.Pan = center - _pinchWorld * session.Viewport.Zoom; session.Notify(ChangeKind.Viewport); e.Handled = true; return;
        }
        switch (_gesture)
        {
            case Gesture.Pan:
                session.Viewport.Pan = _startPan + screen - _startScreen; session.Notify(ChangeKind.Viewport); break;
            case Gesture.Move:
                var raw = world - _startWorld;
                if (screen.Distance(_startScreen) < 2) return;
                var bypass = IsAltDown || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Menu);
                _snap = SnapService.Snap(session.Page, _originals.Keys.ToArray(), _startBounds, raw, session.Viewport.Zoom, session.SnapToGrid && !bypass, session.DynamicGuides && !bypass, session.GridSize);
                foreach (var (id, original) in _originals)
                {
                    if (session.Page.Find(id) is not { } shape) continue;
                    shape.X = original.X + _snap.Delta.X; shape.Y = original.Y + _snap.Delta.Y;
                }
                MoveInternalConnectors(_snap.Delta);
                session.Preview(); break;
            case Gesture.ConnectorEndpoint:
            case Gesture.ConnectorWaypoint:
            case Gesture.ConnectorLabel:
            case Gesture.ConnectorSegment:
                MoveConnectorEdit(world, e.KeyModifiers); break;
            case Gesture.SelectionResize:
            case Gesture.SelectionRotate:
                MoveSelectionTransform(world, e.KeyModifiers); break;
            case Gesture.Resize:
                var entry = _originals.First();
                if (session.Page.Find(entry.Key) is { } resized) ShapeTransforms.Resize(resized, entry.Value, _handle, _startWorld, world, IsShiftDown || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift));
                session.Preview(); break;
            case Gesture.Rotate:
                var rotate = _originals.First();
                if (session.Page.Find(rotate.Key) is { } rotated)
                {
                    var center = rotate.Value.Bounds.Center;
                    var start = Math.Atan2(_startWorld.Y - center.Y, _startWorld.X - center.X);
                    var end = Math.Atan2(world.Y - center.Y, world.X - center.X);
                    var angle = rotate.Value.Rotation + (end - start) * 180 / Math.PI;
                    rotated.Rotation = IsShiftDown || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ? Math.Round(angle / 15) * 15 : angle;
                }
                session.Preview(); break;
            case Gesture.Create:
                if (_created is not null)
                {
                    var bounds = RectD.FromPoints(_startWorld, world);
                    _created.X = bounds.X; _created.Y = bounds.Y; _created.Width = Math.Max(16, bounds.Width); _created.Height = Math.Max(16, bounds.Height);
                    if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift)) _created.Height = _created.Width;
                }
                session.Preview(); break;
            case Gesture.Marquee: _marquee = RectD.FromPoints(_startWorld, world); Invalidate(); break;
            case Gesture.Connect: Invalidate(); break;
        }
        if (_gesture != Gesture.None) e.Handled = true;
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        try { ReleasedCore(sender, e); }
        catch (Exception error) { FailGesture(error); e.Handled = true; }
    }
    private void ReleasedCore(object sender, PointerRoutedEventArgs e)
    {
        if (Session is not { } session) return;
        var p = e.GetCurrentPoint(_canvas).Position; var screen = new PointD(p.X, p.Y); var world = session.Viewport.ToWorld(screen);
        _touches.Remove(e.Pointer.PointerId);
        if (_gesture == Gesture.Pinch) { if (_touches.Count < 2) _gesture = Gesture.None; ReleaseCaptures(); return; }
        // Some input backends coalesce the final motion into pointer release.
        if (world.Distance(_lastWorld) > 1e-8 && _gesture is not Gesture.None and not Gesture.Connect) MovedCore(sender, e);
        var gesture = _gesture; var createdId = _created?.Id; _gesture = Gesture.None;
        if (gesture == Gesture.Marquee && _marquee is { } box)
        {
            foreach (var shape in session.Page.Shapes.Where(s => session.Page.IsVisible(s.LayerId)))
            {
                var select = screen.X >= _startScreen.X ? box.Contains(shape.WorldBounds) : box.Intersects(shape.WorldBounds);
                if (select) session.Selection.Add(shape.Id);
            }
            session.Notify(ChangeKind.Selection);
        }
        if (gesture == Gesture.Connect)
        {
            var target = Renderer.HitShape(session.Page, world, 8 / session.Viewport.Zoom, session.Revision);
            if (_connectSource is not null && target is not null)
                session.Connect(_connectSource, target.Id, _connectPort, NearestPort(target, world));
            else if (screen.Distance(_startScreen) > 8)
            {
                var source = _connectSource;
                session.Execute("Draw connector", () =>
                {
                    var edge = new Connector { SourceId = source, TargetId = target?.Id, SourcePort = _connectPort, TargetPort = target is null ? PortSide.Auto : NearestPort(target, world), Start = _startWorld, End = world, LayerId = session.Page.Layers[0].Id };
                    session.Page.Connectors.Add(edge); session.Selection.Clear(); session.Selection.Add(edge.Id);
                });
            }
        }
        if (gesture == Gesture.Create && _created is not null && screen.Distance(_startScreen) < 5) { _created.Width = _created.Kind == ShapeKind.Text ? 160 : 144; _created.Height = _created.Kind == ShapeKind.Text ? 40 : 64; }
        if (session.IsInteracting) session.Commit();
        _originals.Clear(); _originalConnectors.Clear(); _editedConnectorId = null; _marquee = null; _snap = null; _connectSource = null; _created = null;
        ResetAdvancedGestures(); ReleaseCaptures(); Invalidate(); e.Handled = true;
        if (gesture == Gesture.Create && createdId is not null && session.Page.Find(createdId)?.Kind == ShapeKind.Text) BeginTextEdit(createdId);
    }
    private void Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (Session is not { } session) return;
        var p = e.GetCurrentPoint(_canvas); var delta = p.Properties.MouseWheelDelta;
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) ZoomAt(session.Viewport.Zoom * Math.Pow(1.0015, delta), new(p.Position.X, p.Position.Y));
        else
        {
            FinishTextEdit(true);
            session.Viewport.Pan += p.Properties.IsHorizontalMouseWheel || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ? new PointD(delta * .45, 0) : new PointD(0, delta * .45);
            session.Notify(ChangeKind.Viewport);
        }
        e.Handled = true;
    }
    private static PortSide NearestPort(Shape shape, PointD point)
        => new[] { PortSide.North, PortSide.East, PortSide.South, PortSide.West }.MinBy(side => shape.Port(side).Distance(point));
    private PointD RotationHandle(Shape shape)
    {
        var zoom = Session!.Viewport.Zoom;
        var top = shape.Port(PortSide.North); var center = shape.WorldMatrix.Map(new PointD(.5, .5));
        var point = top + (top - center).Normalized * (25 / zoom);
        return Session.Viewport.ToScreen(point);
    }
    private PointD AutoConnectPosition(Shape shape, PortSide side)
        => Session!.Viewport.ToScreen(shape.Port(side)) + OrthogonalRouter.Direction(side).Rotate(shape.Rotation, PointD.Zero) * (side == PortSide.North ? 49 : 27);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        CancelGesture(); FinishTextEdit(false);
        if (_session is not null) _session.Changed -= SessionChanged;
        Renderer.Dispose(); _canvas.Draw = null;
    }
}
