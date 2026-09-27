using System.Globalization;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench : UserControl, IDisposable
{
    private readonly IWorkspaceStorage _storage;
    private readonly OfficeRibbon _ribbon = new();
    private readonly StencilPane _stencils = new();
    private readonly PageStrip _pages = new();
    private readonly Grid _body = new();
    private readonly StackPanel _properties = new() { Spacing = 10, Margin = new Thickness(14, 10, 14, 16) };
    private readonly ScrollViewer _propertyScroll;
    private readonly TextBlock _title = OfficeTheme.Text("", 12, "#FFFFFF");
    private readonly TextBlock _status = OfficeTheme.Text("Ready", 11);
    private readonly TextBlock _selectionInfo = OfficeTheme.Text("", 11, OfficeTheme.Secondary);
    private readonly TextBlock _zoomLabel = OfficeTheme.Text("100%", 11);
    private readonly Slider _zoom = new() { Minimum = 10, Maximum = 800, Value = 100, Width = 126, MinHeight = 20, StepFrequency = 5 };
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly List<(OfficeButton Button, Func<bool> Enabled)> _bindings = [];
    private bool _refreshing, _disposed, _savingRecovery, _pendingRecovery;
    private string? _clipboard;
    private string _pane = "";
    private StencilMaster? _dragMaster;
    private PointD _dragStart;
    public EditorSession Session { get; }
    public DiagramSurface Surface { get; } = new();
    public string StatusText => _status.Text;
    public event Action? StateChanged;
    public DiagramWorkbench(EditorSession session, IWorkspaceStorage storage)
    {
        Session = session; _storage = storage; Surface.Session = session;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var root = new Grid { Background = OfficeTheme.Brush("#FFFFFF"), RowDefinitions = { new() { Height = new GridLength(38) }, new() { Height = new GridLength(134) }, new() { Height = new GridLength(1, GridUnitType.Star) }, new() { Height = new GridLength(25) } } };
        root.Children.Add(BuildTitleBar());
        Grid.SetRow(_ribbon, 1); root.Children.Add(_ribbon);
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(232) }); _body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _body.ColumnDefinitions.Add(new() { Width = new GridLength(0) });
        _body.Children.Add(_stencils);
        var drawing = new Grid { RowDefinitions = { new() { Height = new GridLength(1, GridUnitType.Star) }, new() { Height = new GridLength(31) } } };
        drawing.Children.Add(Surface); Grid.SetRow(_pages, 1); drawing.Children.Add(_pages); Grid.SetColumn(drawing, 1); _body.Children.Add(drawing);
        _propertyScroll = new ScrollViewer { Content = _properties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = OfficeTheme.Brush("#FFFFFF"), Visibility = Visibility.Collapsed };
        var propertyBorder = new Border { Child = _propertyScroll, BorderBrush = OfficeTheme.Brush("#D5D5D5"), BorderThickness = new Thickness(1, 0, 0, 0) };
        Grid.SetColumn(propertyBorder, 2); _body.Children.Add(propertyBorder); Grid.SetRow(_body, 2); root.Children.Add(_body);
        var status = BuildStatusBar(); Grid.SetRow(status, 3); root.Children.Add(status);
        Content = root;
        BuildRibbon(); _ribbon.Select("Home");
        _stencils.InsertRequested += Insert;
        _stencils.DragRequested += StartStencilDrag;
        PointerMoved += StencilDragMoved; PointerReleased += StencilDragReleased;
        PointerCanceled += (_, _) => EndStencilDrag();
        _pages.PageSelected += id => { Surface.FinishTextEdit(true); Session.SwitchPage(id); Surface.Fit(); };
        _pages.RenameRequested += id => RunAsync(() => RenamePageAsync(id));
        _pages.AddRequested += () => { Session.AddPage(); Surface.Fit(); };
        Surface.CursorChanged += point => { if (Session.Selection.Count == 0) _selectionInfo.Text = $"X: {point.X / 96:0.00} in   Y: {point.Y / 96:0.00} in"; };
        Surface.StatusChanged += text => ShowStatus(text);
        Surface.ContextRequested += ShowContextMenu;
        Session.Changed += SessionChanged;
        KeyDown += WorkbenchKeyDown;
        KeyUp += (_, e) =>
        {
            if (e.Key == VirtualKey.Space) Surface.IsSpaceDown = false;
            if (e.Key == VirtualKey.Menu) Surface.IsAltDown = false;
            if (e.Key == VirtualKey.Shift) Surface.IsShiftDown = false;
        };
        _autosave.Tick += (_, _) => { _autosave.Stop(); RunAsync(WriteRecoveryAsync); };
        Loaded += (_, _) => { Refresh(); Surface.FocusCanvas(); };
        SizeChanged += (_, _) => { _body.ColumnDefinitions[0].Width = _stencils.Visibility == Visibility.Collapsed ? new(0) : new(ActualWidth < 850 ? 190 : 232); };
        Refresh();
    }
    private UIElement BuildTitleBar()
    {
        var grid = new Grid { Background = OfficeTheme.Brush(OfficeTheme.Accent), ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        var app = new OfficeIconView { Icon = OfficeIcon.App, Color = "#FFFFFF", Width = 23, Height = 23, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var save = new OfficeButton("", OfficeIcon.Save, action: () => RunAsync(SaveAsync)) { Dark = true, Width = 33, Height = 32 };
        AutomationProperties.SetName(save, "Save"); ToolTipService.SetToolTip(save, "Save (Ctrl+S)");
        var undo = new OfficeButton("", OfficeIcon.Undo, action: () => Session.Undo()) { Dark = true, Width = 33, Height = 32 };
        var redo = new OfficeButton("", OfficeIcon.Redo, action: () => Session.Redo()) { Dark = true, Width = 33, Height = 32 };
        AutomationProperties.SetName(undo, "Undo"); AutomationProperties.SetName(redo, "Redo");
        _bindings.Add((undo, () => Session.CanUndo)); _bindings.Add((redo, () => Session.CanRedo));
        var quick = OfficeTheme.Row(app, save, undo, redo); quick.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(quick);
        _title.HorizontalAlignment = HorizontalAlignment.Center; _title.Margin = new Thickness(16, 0, 16, 0); Grid.SetColumn(_title, 1); grid.Children.Add(_title);
        var find = new OfficeButton("Search drawing", OfficeIcon.Search, action: () => RunAsync(FindAsync)) { Dark = true, Margin = new Thickness(0, 0, 12, 0), Height = 32 };
        Grid.SetColumn(find, 2); grid.Children.Add(find);
        return grid;
    }
    private UIElement BuildStatusBar()
    {
        var grid = new Grid { Background = OfficeTheme.Brush("#F6F6F6"), Padding = new Thickness(10, 0, 6, 0), ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto } } };
        grid.Children.Add(_status); _selectionInfo.Margin = new Thickness(12, 0, 20, 0); Grid.SetColumn(_selectionInfo, 1); grid.Children.Add(_selectionInfo);
        var zoomOut = new OfficeButton("", OfficeIcon.Minus, action: () => Surface.ZoomAt(Session.Viewport.Zoom / 1.2)) { Width = 25, Height = 23, MinHeight = 20 };
        var zoomIn = new OfficeButton("", OfficeIcon.Add, action: () => Surface.ZoomAt(Session.Viewport.Zoom * 1.2)) { Width = 25, Height = 23, MinHeight = 20 };
        var fit = new OfficeButton("", OfficeIcon.Fit, action: () => Surface.Fit()) { Width = 28, Height = 23, MinHeight = 20 };
        AutomationProperties.SetName(zoomOut, "Zoom out"); AutomationProperties.SetName(zoomIn, "Zoom in"); AutomationProperties.SetName(fit, "Fit page"); AutomationProperties.SetName(_zoom, "Zoom percentage");
        _zoomLabel.Width = 42; _zoomLabel.TextAlignment = TextAlignment.Right;
        _zoom.ValueChanged += (_, _) => { if (!_refreshing) Surface.ZoomAt(_zoom.Value / 100); };
        var row = OfficeTheme.Row(_zoomLabel, zoomOut, _zoom, zoomIn, fit); row.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(row, 2); grid.Children.Add(row);
        return new Border { Child = grid, BorderBrush = OfficeTheme.Brush("#D5D5D5"), BorderThickness = new Thickness(0, 1, 0, 0) };
    }
    private void SessionChanged(ChangeKind kind)
    {
        if (kind == ChangeKind.Preview) { Surface.Invalidate(); StateChanged?.Invoke(); return; }
        if (kind == ChangeKind.Document) { _autosave.Stop(); _autosave.Start(); }
        Refresh(); StateChanged?.Invoke();
    }
    private void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            _title.Text = Session.Document.Title + (Session.IsDirty ? " *" : "") + "  —  DrawingSpace";
            _pages.Update(Session.Document.Pages, Session.ActivePageId);
            foreach (var (button, enabled) in _bindings) button.IsEnabled = enabled();
            _zoom.Value = Session.Viewport.Zoom * 100; _zoomLabel.Text = $"{Session.Viewport.Zoom:P0}";
            if (Session.SelectedShapes.Count == 1)
            {
                var s = Session.SelectedShapes[0]; _selectionInfo.Text = $"{s.Name}   {s.Width / 96:0.00} × {s.Height / 96:0.00} in";
            }
            else _selectionInfo.Text = Session.Selection.Count > 0 ? $"{Session.Selection.Count} objects selected" : $"Page {Session.Document.Pages.IndexOf(Session.Page) + 1} of {Session.Document.Pages.Count}";
            if (_pane.Length > 0) RebuildProperties();
        }
        finally { _refreshing = false; }
    }
    public void ShowStatus(string text, bool error = false)
    {
        _status.Text = text; _status.Foreground = OfficeTheme.Brush(error ? "#B42318" : OfficeTheme.TextColor); StateChanged?.Invoke();
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { Console.Error.WriteLine(ex); ShowStatus(ex.Message, true); }
    }
    private async void RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { ShowStatus("Cancelled"); }
        catch (Exception ex) { Console.Error.WriteLine(ex); ShowStatus(ex.Message, true); }
    }
    private async Task WriteRecoveryAsync()
    {
        if (_savingRecovery) { _pendingRecovery = true; return; }
        _savingRecovery = true;
        try
        {
            do
            {
                _pendingRecovery = false;
                await _storage.SaveRecoveryAsync(DocumentCodec.Save(Session.Document));
                ShowStatus("Recovery copy saved locally");
            } while (_pendingRecovery);
        }
        finally { _savingRecovery = false; }
    }
    public void Insert(StencilMaster master)
    {
        Surface.FinishTextEdit(true); var center = Surface.ViewCenter;
        var shape = master.Create(new(Math.Round(center.X / 8) * 8, Math.Round(center.Y / 8) * 8));
        Session.AddShape(shape); Session.Tool = EditorTool.Pointer; Surface.FocusCanvas();
    }
    private void StartStencilDrag(StencilMaster master, PointerRoutedEventArgs e)
    {
        Surface.FinishTextEdit(true); _dragMaster = master;
        var p = e.GetCurrentPoint(this).Position; _dragStart = new(p.X, p.Y); CapturePointer(e.Pointer);
    }
    private void StencilDragMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragMaster is null) return;
        var p = e.GetCurrentPoint(Surface).Position;
        if (p.X >= 22 && p.Y >= 22 && p.X <= Surface.ActualWidth && p.Y <= Surface.ActualHeight)
            Surface.SetGhost(_dragMaster.Create(Session.Viewport.ToWorld(new(p.X, p.Y))));
        else Surface.SetGhost(null);
        e.Handled = true;
    }
    private void StencilDragReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragMaster is null) return;
        var master = _dragMaster; var p = e.GetCurrentPoint(Surface).Position; var root = e.GetCurrentPoint(this).Position;
        EndStencilDrag();
        if (new PointD(root.X, root.Y).Distance(_dragStart) < 5) Insert(master);
        else if (p.X >= 22 && p.Y >= 22 && p.X <= Surface.ActualWidth && p.Y <= Surface.ActualHeight)
        {
            var world = Session.Viewport.ToWorld(new(p.X, p.Y));
            if (Session.SnapToGrid) world = new(Math.Round(world.X / 8) * 8, Math.Round(world.Y / 8) * 8);
            Session.AddShape(master.Create(world)); Session.Tool = EditorTool.Pointer; Surface.FocusCanvas();
        }
        e.Handled = true;
    }
    private void EndStencilDrag() { _dragMaster = null; Surface.SetGhost(null); ReleasePointerCaptures(); }
    private void ShowPane(string pane)
    {
        Surface.FinishTextEdit(true); _pane = _pane == pane ? "" : pane;
        _body.ColumnDefinitions[2].Width = new GridLength(_pane.Length == 0 ? 0 : _pane is "shapesheet" or "masters" or "richtext" or "connections" ? 330 : 285);
        _propertyScroll.Visibility = _pane.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Refresh();
    }
    private void ShowContextMenu(Point point)
    {
        var menu = new Flyout();
        var items = OfficeTheme.Column(
            Command("Cut", OfficeIcon.Cut, () => RunAsync(CutAsync)), Command("Copy", OfficeIcon.Copy, () => RunAsync(CopyAsync)),
            Command("Paste", OfficeIcon.Paste, () => RunAsync(PasteAsync)), OfficeTheme.Rule(),
            Command("Duplicate", OfficeIcon.Copy, () => Session.Duplicate()), Command("Delete", OfficeIcon.Delete, () => Session.DeleteSelection()),
            OfficeTheme.Rule(), Command("Edit text", OfficeIcon.Text, EditText), Command("Format Shape", OfficeIcon.Settings, () => ShowPane("format")),
            Command("Bring to Front", OfficeIcon.Front, () => Session.BringToFront(true)), Command("Send to Back", OfficeIcon.Back, () => Session.BringToFront(false)));
        items.MinWidth = 176;
        foreach (var button in items.Children.OfType<OfficeButton>()) button.Invoked += () => menu.Hide();
        menu.Content = items; menu.ShowAt(Surface, new FlyoutShowOptions { Position = point });
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _autosave.Stop(); Session.Changed -= SessionChanged; Surface.Dispose();
    }
}
