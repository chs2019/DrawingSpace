namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private readonly EditorPaneState _paneState = new();
    private readonly List<(OfficeButton Button, Func<bool> Enabled)> _ribbonBindings = [];
    private readonly List<(OfficeButton Button, Func<bool> Enabled)> _propertyBindings = [];
    private List<(OfficeButton Button, Func<bool> Enabled)>? _activeBindings;

    /// <summary>Read-only counters for diagnosing shell invalidation and retained command state.</summary>
    public long PropertyRebuildCount { get; private set; }
    public int RegisteredCommandBindingCount => _bindings.Count + _ribbonBindings.Count + _propertyBindings.Count;
    public long PageStripRebuildCount => _pages.RebuildCount;

    private void AddRibbonTab(string name, Func<IEnumerable<RibbonGroup>> build)
        => _ribbon.AddTab(name, () => BuildRibbonGroups(build));

    private IEnumerable<RibbonGroup> BuildRibbonGroups(Func<IEnumerable<RibbonGroup>> build)
    {
        _ribbonBindings.Clear();
        var previous = _activeBindings;
        _activeBindings = _ribbonBindings;
        try { return build().ToArray(); } // Materialize the iterator while its binding scope is active.
        finally { _activeBindings = previous; }
    }

    private void RebuildProperties()
    {
        var previousBindings = _activeBindings;
        var previousRefreshing = _refreshing;
        _activeBindings = _propertyBindings;
        _refreshing = true; // Ignore synchronous LostFocus while retiring ordinary editors.
        _propertyBindings.Clear();
        _paneState.Invalidate();
        try
        {
            BuildPropertiesCore();
            _paneState.Capture(Session, _pane);
            PropertyRebuildCount++;
        }
        finally { _activeBindings = previousBindings; _refreshing = previousRefreshing; }
    }

    private void SessionChanged(ChangeKind kind)
    {
        if (_disposed) return;
        if (kind == ChangeKind.Preview) { Surface.Invalidate(); StateChanged?.Invoke(); return; }
        if (kind == ChangeKind.Viewport)
        {
            UpdateViewportPresentation();
            StateChanged?.Invoke();
            return;
        }
        if (kind == ChangeKind.Document)
        {
            ShowRecoveryStatus("Recovery pending");
            _autosave.Stop(); _autosave.Start();
        }
        Refresh(rebuildProperties: kind != ChangeKind.Tool);
        StateChanged?.Invoke();
    }

    private void UpdateViewportPresentation()
    {
        var previous = _refreshing;
        _refreshing = true;
        try { _zoom.Value = Session.Viewport.Zoom * 100; _zoomLabel.Text = $"{Session.Viewport.Zoom:P0}"; }
        finally { _refreshing = previous; }
    }

    // Explicit pane navigation (master/component/cell selection) has state outside
    // the document revision. Session notifications use the non-forcing overload.
    private void Refresh()
    {
        _paneState.Invalidate();
        Refresh(rebuildProperties: true);
    }

    private void Refresh(bool rebuildProperties)
    {
        if (_refreshing || _disposed) return;
        _refreshing = true;
        try
        {
            _title.Text = Session.Document.Title + (Session.IsDirty ? " *" : "") + "  —  DrawingSpace";
            _pages.Update(Session.Document.Pages, Session.ActivePageId);
            UpdateBindings(_bindings); UpdateBindings(_ribbonBindings); UpdateBindings(_propertyBindings);
            UpdateViewportPresentation();
            var selected = Session.SelectedShapes;
            if (selected.Count == 1)
            {
                var shape = selected[0];
                _selectionInfo.Text = $"{shape.Name}   {shape.Width / 96:0.00} × {shape.Height / 96:0.00} in";
            }
            else _selectionInfo.Text = Session.Selection.Count > 0 ? $"{Session.Selection.Count} objects selected" : $"Page {Session.Document.Pages.IndexOf(Session.Page) + 1} of {Session.Document.Pages.Count}";
            if (rebuildProperties && _pane.Length > 0 && _paneState.NeedsRebuild(Session, _pane)) RebuildProperties();
        }
        finally { _refreshing = false; }
    }

    private static void UpdateBindings(List<(OfficeButton Button, Func<bool> Enabled)> bindings)
    {
        foreach (var (button, enabled) in bindings)
        {
            var value = enabled();
            if (button.IsEnabled != value) button.IsEnabled = value;
        }
    }
}
