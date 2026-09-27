using System.Globalization;

namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private OfficeButton Command(string label, OfficeIcon icon, Action action, bool large = false, Func<bool>? enabled = null, string? tooltip = null)
    {
        var button = new OfficeButton(label, icon, large, () => { Surface.FinishTextEdit(true); Guard(action); }) { VerticalAlignment = VerticalAlignment.Top };
        if (enabled is not null) { _bindings.Add((button, enabled)); button.IsEnabled = enabled(); }
        if (tooltip is not null) ToolTipService.SetToolTip(button, tooltip);
        return button;
    }
    private OfficeButton Tool(string label, OfficeIcon icon, EditorTool tool)
        => Command(label, icon, () => { Session.Tool = tool; Surface.FocusCanvas(); }, tooltip: tool == EditorTool.Pointer ? "Pointer Tool (Ctrl+1)" : tool == EditorTool.Connector ? "Connector (Ctrl+3): drag from one shape to another" : "Text Tool (Ctrl+2)");
    private static StackPanel Stack(params UIElement[] items)
    {
        var panel = OfficeTheme.Column(items); panel.Spacing = 0; return panel;
    }
    private bool HasShapes() => Session.EditableShapes.Count > 0;
    private bool HasSelection() => Session.Selection.Count > 0;
    private void BuildRibbon()
    {
        _ribbon.AddTab("File", FileGroups);
        _ribbon.AddTab("Home", HomeGroups);
        _ribbon.AddTab("Insert", InsertGroups);
        _ribbon.AddTab("Design", DesignGroups);
        _ribbon.AddTab("Data", DataGroups);
        _ribbon.AddTab("Process", ProcessGroups);
        _ribbon.AddTab("Review", ReviewGroups);
        _ribbon.AddTab("View", ViewGroups);
        _ribbon.AddTab("Developer", DeveloperGroups);
        _ribbon.AddTab("Help", HelpGroups);
        _ribbon.TabChanged += _ => Refresh();
    }
    private IEnumerable<RibbonGroup> FileGroups()
    {
        yield return new("File", Command("New", OfficeIcon.New, () => RunAsync(() => NewAsync("blank")), true), Command("Open", OfficeIcon.Open, () => RunAsync(OpenAsync), true), Command("Save", OfficeIcon.Save, () => RunAsync(SaveAsync), true), Command("Rename", OfficeIcon.Text, () => RunAsync(RenameDocumentAsync), true));
        yield return new("New from template", Stack(Command("Basic Flowchart", OfficeIcon.Connector, () => RunAsync(() => NewAsync("flowchart"))), Command("Organization Chart", OfficeIcon.Layout, () => RunAsync(() => NewAsync("organization"))), Command("Network Diagram", OfficeIcon.App, () => RunAsync(() => NewAsync("network")))));
        yield return new("Export", Command("SVG", OfficeIcon.Export, () => RunAsync(() => ExportAsync("svg")), true), Command("PNG", OfficeIcon.Export, () => RunAsync(() => ExportAsync("png")), true), Command("PDF", OfficeIcon.Print, () => RunAsync(() => ExportAsync("pdf")), true));
        yield return new("Visio", Command("VSDX", OfficeIcon.Save, () => RunAsync(() => ExportVisioAsync(DrawingSpace.Visio.VisioPackageKind.Drawing)), true),
            Command("VSSX", OfficeIcon.App, () => RunAsync(() => ExportVisioAsync(DrawingSpace.Visio.VisioPackageKind.Stencil)), true),
            Command("VSTX", OfficeIcon.Page, () => RunAsync(() => ExportVisioAsync(DrawingSpace.Visio.VisioPackageKind.Template)), true));
        yield return new("Local files", Stack(Command("Save recovery copy", OfficeIcon.Save, () => RunAsync(WriteRecoveryAsync)), Command("About file formats", OfficeIcon.Help, () => RunAsync(() => ShowMessageAsync("File formats", "Open supports native JSON, VSDX drawings, VSTX templates, VSSX stencil libraries and VDX XML. VSSX imports masters into the current drawing. SVG, PNG, PDF and Visio packages can be exported. Binary VSD is not decoded. Unsupported Visio constructs are reported in Import Diagnostics. Files remain on this device.")))));
    }
    private IEnumerable<RibbonGroup> HomeGroups()
    {
        yield return new("Clipboard", Command("Paste", OfficeIcon.Paste, () => RunAsync(PasteAsync), true), Stack(Command("Cut", OfficeIcon.Cut, () => RunAsync(CutAsync), enabled: HasSelection), Command("Copy", OfficeIcon.Copy, () => RunAsync(CopyAsync), enabled: HasSelection), Command("Duplicate", OfficeIcon.Copy, () => Session.Duplicate(), enabled: HasSelection)));
        var family = OfficeTheme.Field(Session.SelectedShapes.FirstOrDefault()?.Style.FontFamily ?? "Arial", "Font family", 114);
        var size = OfficeTheme.Field((Session.SelectedShapes.FirstOrDefault()?.Style.FontSize ?? 14).ToString("0.#", CultureInfo.InvariantCulture), "Font size", 42);
        void FontFamily() { if (!string.IsNullOrWhiteSpace(family.Text)) Guard(() => Session.Format(s => s.FontFamily = family.Text.Trim())); }
        void FontSize() { if (double.TryParse(size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= 1024) Guard(() => Session.Format(s => s.FontSize = value)); }
        family.LostFocus += (_, _) => FontFamily(); family.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { FontFamily(); e.Handled = true; Surface.FocusCanvas(); } };
        size.LostFocus += (_, _) => FontSize(); size.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { FontSize(); e.Handled = true; Surface.FocusCanvas(); } };
        var fontRow = OfficeTheme.Row(family, size); fontRow.Spacing = 5;
        var fontButtons = OfficeTheme.Row(Command("", OfficeIcon.Bold, () => Session.Format(s => s.Bold = !s.Bold), enabled: HasShapes, tooltip: "Bold (Ctrl+B)"), Command("", OfficeIcon.Italic, () => Session.Format(s => s.Italic = !s.Italic), enabled: HasShapes, tooltip: "Italic (Ctrl+I)"), Command("A+", OfficeIcon.None, () => Session.Format(s => s.FontSize = Math.Min(1024, s.FontSize + 2)), enabled: HasShapes), Command("A−", OfficeIcon.None, () => Session.Format(s => s.FontSize = Math.Max(1, s.FontSize - 2)), enabled: HasShapes));
        AutomationProperties.SetName(fontButtons.Children[0], "Bold"); AutomationProperties.SetName(fontButtons.Children[1], "Italic");
        yield return new("Font", Stack(fontRow, fontButtons, Command("Font color", OfficeIcon.Text, () => ShowColorMenu("Text color", c => Session.Format(s => s.TextColor = c)), enabled: HasShapes)));
        yield return new("Tools", Stack(Tool("Pointer Tool", OfficeIcon.Pointer, EditorTool.Pointer), Tool("Connector", OfficeIcon.Connector, EditorTool.Connector), Tool("Text", OfficeIcon.Text, EditorTool.Text)), Stack(Command("Rectangle", OfficeIcon.Rectangle, () => { Session.Tool = EditorTool.Rectangle; Surface.FocusCanvas(); }), Command("Ellipse", OfficeIcon.Ellipse, () => { Session.Tool = EditorTool.Ellipse; Surface.FocusCanvas(); }), Command("Rotate", OfficeIcon.Rotate, () => Rotate(90), enabled: HasShapes)));
        var styles = new ColorPalette(); styles.ColorSelected += color => Guard(() => Session.Format(s => s.Fill = color));
        yield return new("Shape Styles", Stack(styles, Command("Format Shape", OfficeIcon.Settings, () => ShowPane("format"))), Stack(Command("Fill", OfficeIcon.Fill, () => ShowColorMenu("Fill color", c => Session.Format(s => s.Fill = c))), Command("Line", OfficeIcon.Line, () => ShowColorMenu("Line color", c => { Session.Format(s => s.Stroke = c); Session.FormatConnector(e => e.Color = c); })), Command("Line weight", OfficeIcon.Line, () => ShowLineWeights())));
        yield return new("Arrange", Command("Position", OfficeIcon.Layout, () => ShowArrangeMenu(), true), Stack(Command("Align", OfficeIcon.Align, () => ShowAlignMenu()), Command("Group", OfficeIcon.Group, () => Session.Group(), enabled: () => Session.EditableShapes.Count >= 2), Command("Ungroup", OfficeIcon.Ungroup, () => Session.Ungroup(), enabled: HasShapes)));
        yield return new("Editing", Stack(Command("Find", OfficeIcon.Search, () => RunAsync(FindAsync)), Command("Select All", OfficeIcon.Pointer, () => Session.SelectAll()), Command("Delete", OfficeIcon.Delete, () => Session.DeleteSelection(), enabled: HasSelection)));
    }
    private IEnumerable<RibbonGroup> InsertGroups()
    {
        yield return new("Pages", Command("New Page", OfficeIcon.Add, () => { Session.AddPage(); Surface.Fit(); }, true), Stack(Command("Rename Page", OfficeIcon.Text, () => RunAsync(() => RenamePageAsync(Session.ActivePageId))), Command("Delete Page", OfficeIcon.Delete, () => RunAsync(DeletePageAsync), enabled: () => Session.Document.Pages.Count > 1)));
        yield return new("Shapes", Command("Rectangle", OfficeIcon.Rectangle, () => Insert(StencilCatalog.Find("rectangle")), true), Command("Ellipse", OfficeIcon.Ellipse, () => Insert(StencilCatalog.Find("ellipse")), true), Command("Text Box", OfficeIcon.Text, () => { Insert(StencilCatalog.Find("text")); EditText(); }, true), Command("Connector", OfficeIcon.Connector, () => { Session.Tool = EditorTool.Connector; Surface.FocusCanvas(); }, true));
        yield return new("Diagram Parts", Command("Container", OfficeIcon.Group, () => { Session.CreateContainer(); ShowPane("containers"); }, true), Command("Callout", OfficeIcon.Comment, () => Insert(StencilCatalog.Find("callout")), true), Command("Note", OfficeIcon.Page, () => Insert(StencilCatalog.Find("note")), true));
        yield return new("Stencils", Stack(Command("Basic Shapes", OfficeIcon.Rectangle, () => _stencils.ShowStencil("basic")), Command("Organization Chart", OfficeIcon.Layout, () => _stencils.ShowStencil("organization")), Command("Network", OfficeIcon.App, () => _stencils.ShowStencil("network"))));
    }
    private IEnumerable<RibbonGroup> DesignGroups()
    {
        yield return new("Page Setup", Command("Orientation", OfficeIcon.Landscape, () => ShowMenu("Orientation", [("Landscape", () => PageOrientation(true)), ("Portrait", () => PageOrientation(false))]), true), Command("Size", OfficeIcon.Page, () => ShowPane("page"), true), Command("Auto Size", OfficeIcon.Fit, () => { Session.AutoSizePage(); Surface.Fit(); }, true));
        yield return new("Themes", Command("Office Blue", OfficeIcon.Fill, () => ApplyTheme("#E8F0FA", "#4672C4"), true), Command("Forest", OfficeIcon.Fill, () => ApplyTheme("#E2F0D9", "#548235"), true), Command("Neutral", OfficeIcon.Fill, () => ApplyTheme("#F2F2F2", "#666666"), true));
        yield return new("Background", Command("Page Color", OfficeIcon.Fill, () => ShowColorMenu("Page color", c => Session.Execute("Page color", () => Session.Page.Background = c)), true));
        yield return new("Layout", Command("Re-Layout Page", OfficeIcon.Layout, () => { Session.AutoLayout(); Surface.Fit(); }, true), Command("Align Shapes", OfficeIcon.Align, ShowAlignMenu, true), Command("Distribute", OfficeIcon.Distribute, ShowArrangeMenu, true));
    }
    private IEnumerable<RibbonGroup> DataGroups()
    {
        yield return new("Shape Data", Command("Shape Data", OfficeIcon.Data, () => ShowPane("data"), true), Command("Add Property", OfficeIcon.Add, () => RunAsync(AddDataAsync), true, HasShapes), Command("Export CSV", OfficeIcon.Export, () => RunAsync(ExportDataAsync), true));
        yield return new("Organization", Command("Layers", OfficeIcon.Layers, () => ShowPane("layers"), true), Command("New Layer", OfficeIcon.Add, () => RunAsync(AddLayerAsync), true));
        yield return new("Inspect", Command("Find Shape", OfficeIcon.Search, () => RunAsync(FindAsync), true), Command("Check Diagram", OfficeIcon.Check, () => ShowPane("validation"), true));
    }
    private IEnumerable<RibbonGroup> ProcessGroups()
    {
        yield return new("Diagram Validation", Command("Check Diagram", OfficeIcon.Check, () => ShowPane("validation"), true));
        yield return new("Layout", Command("Re-Layout Page", OfficeIcon.Layout, () => { Session.AutoLayout(); Surface.Fit(); }, true), Command("Align", OfficeIcon.Align, ShowAlignMenu, true), Command("Distribute", OfficeIcon.Distribute, ShowArrangeMenu, true));
        yield return new("Connections", Command("Connect Shapes", OfficeIcon.Connector, ConnectSelection, true, () => Session.SelectedShapes.Count == 2), Stack(Command("Right Angle", OfficeIcon.Connector, () => Session.FormatConnector(e => e.Kind = ConnectorKind.Orthogonal)), Command("Straight", OfficeIcon.Line, () => Session.FormatConnector(e => e.Kind = ConnectorKind.Straight)), Command("Arrowheads", OfficeIcon.Connector, ShowArrowMenu)));
        yield return new("Flowchart", Command("Process", OfficeIcon.Rectangle, () => Insert(StencilCatalog.Find("process")), true), Command("Decision", OfficeIcon.App, () => Insert(StencilCatalog.Find("decision")), true), Command("Subprocess", OfficeIcon.Group, () => Insert(StencilCatalog.Find("subprocess")), true));
    }
    private IEnumerable<RibbonGroup> ReviewGroups()
    {
        yield return new("Comments", Command("New Comment", OfficeIcon.Comment, () => RunAsync(AddCommentAsync), true, () => Session.SelectedShapes.Count == 1), Command("Comments", OfficeIcon.Comment, () => ShowPane("comments"), true));
        yield return new("Review", Command("Check Diagram", OfficeIcon.Check, () => ShowPane("validation"), true), Command("Shape Data", OfficeIcon.Data, () => ShowPane("data"), true));
        yield return new("Protection", Command("Lock Shapes", OfficeIcon.Lock, () => LockSelection(true), true, HasSelection), Command("Unlock Shapes", OfficeIcon.Lock, () => LockSelection(false), true, HasSelection), Command("Layer Settings", OfficeIcon.Layers, () => ShowPane("layers"), true));
    }
    private IEnumerable<RibbonGroup> ViewGroups()
    {
        yield return new("Show", Stack(Toggle("Ruler", OfficeIcon.Ruler, () => Session.RulersVisible, value => Session.RulersVisible = value), Toggle("Grid", OfficeIcon.Grid, () => Session.GridVisible, value => Session.GridVisible = value), Toggle("Shapes Pane", OfficeIcon.App, () => _stencils.Visibility == Visibility.Visible, value => { _stencils.Visibility = value ? Visibility.Visible : Visibility.Collapsed; _body.ColumnDefinitions[0].Width = new(value ? 232 : 0); })));
        yield return new("Visual Aids", Stack(Toggle("Snap to Grid", OfficeIcon.Grid, () => Session.SnapToGrid, value => Session.SnapToGrid = value), Toggle("Dynamic Guides", OfficeIcon.Align, () => Session.DynamicGuides, value => Session.DynamicGuides = value), Toggle("AutoConnect", OfficeIcon.Connector, () => Session.AutoConnect, value => Session.AutoConnect = value)));
        yield return new("Zoom", Command("Fit Page", OfficeIcon.Fit, () => Surface.Fit(), true), Command("Selection", OfficeIcon.Fit, () => Surface.Fit(true), true, HasShapes), Stack(Command("100%", OfficeIcon.Search, () => Surface.ZoomAt(1)), Command("200%", OfficeIcon.Search, () => Surface.ZoomAt(2)), Command("50%", OfficeIcon.Search, () => Surface.ZoomAt(.5))));
        yield return new("Task Panes", Command("Format Shape", OfficeIcon.Settings, () => ShowPane("format"), true), Command("Shape Data", OfficeIcon.Data, () => ShowPane("data"), true), Command("Layers", OfficeIcon.Layers, () => ShowPane("layers"), true));
    }
    private IEnumerable<RibbonGroup> HelpGroups()
    {
        yield return new("Help", Command("Getting Started", OfficeIcon.Help, () => RunAsync(() => ShowMessageAsync("Getting started", "Drag a shape from the Shapes pane onto the page, or click a stencil to insert at the view center. Drag shapes to move them. Eight handles resize a selection; the circular handle rotates. Blue AutoConnect arrows add a connected copy.\n\nChoose Connector and drag between shapes to glue endpoints. Double-click a shape or connector to edit its label. Shift-click adds to a selection. Drag a selection rectangle left-to-right for enclosure, right-to-left for crossing selection.\n\nSpace+drag pans. Ctrl+wheel zooms around the pointer. The status bar provides fit-to-page and zoom.\n\nFile contains editable JSON save/open and SVG, PNG and PDF export. Recovery copies are saved locally.")), true));
        yield return new("Reference", Command("Keyboard Shortcuts", OfficeIcon.Text, () => RunAsync(() => ShowMessageAsync("Keyboard shortcuts", "Ctrl+1  Pointer Tool\nCtrl+2  Text Tool\nCtrl+3  Connector Tool\nCtrl+S  Save drawing\nCtrl+O  Open drawing\nCtrl+N  New blank drawing\nCtrl+Z / Ctrl+Y  Undo / Redo\nCtrl+C / X / V  Copy / Cut / Paste\nCtrl+D  Duplicate\nCtrl+A  Select all\nCtrl+G  Group\nCtrl+Shift+U  Ungroup\nCtrl+B / I  Bold / Italic\nCtrl+F  Find\nDelete  Delete selection\nF2  Edit label\nArrow keys  Move by 1 pixel\nShift+arrows  Move by 10 pixels\nEscape  Cancel interaction\nSpace+drag  Pan\nCtrl+wheel  Zoom\nShift+resize  Preserve aspect ratio\nShift+rotate  Snap to 15°\nAlt+drag  Bypass snapping")), true), Command("About", OfficeIcon.App, () => RunAsync(() => ShowMessageAsync("DrawingSpace 0.2.0-alpha.1", "Independent, local-first diagramming built with Uno Platform and SkiaSharp.\n\nOriginal shape geometry and custom reusable controls. This is not Microsoft Visio. Full Visio feature parity, ShapeSheet, VSD/VSDX compatibility, collaboration and pixel-identical rendering are not implemented in this release.\n\nSource: github.com/wieslawsoltes/DrawingSpace\nLicense: MIT")), true));
    }
    private OfficeButton Toggle(string name, OfficeIcon icon, Func<bool> get, Action<bool> set)
    {
        OfficeButton? button = null;
        button = Command(name, icon, () => { set(!get()); button!.IsSelected = get(); Session.Notify(ChangeKind.Viewport); });
        button.IsSelected = get(); return button;
    }
    private void ShowMenu(string title, IEnumerable<(string Label, Action Action)> actions)
    {
        var flyout = new Flyout(); var stack = new StackPanel { Spacing = 2, MinWidth = 170 };
        var heading = OfficeTheme.Text(title, 12, OfficeTheme.Accent, true); heading.Margin = new Thickness(5, 4, 5, 8); stack.Children.Add(heading);
        foreach (var (label, action) in actions)
        {
            var button = new OfficeButton(label, action: () => { flyout.Hide(); Guard(action); }); stack.Children.Add(button);
        }
        flyout.Content = stack; flyout.ShowAt(_ribbon);
    }
    private void ShowColorMenu(string title, Action<string> action)
    {
        var flyout = new Flyout(); var palette = new ColorPalette();
        palette.ColorSelected += color => { flyout.Hide(); Guard(() => action(color)); };
        flyout.Content = OfficeTheme.Column(OfficeTheme.Text(title, 12, OfficeTheme.Accent, true), palette, new OfficeButton("No fill / transparent", action: () => { flyout.Hide(); Guard(() => action("#00FFFFFF")); }));
        flyout.ShowAt(_ribbon);
    }
    private void ShowAlignMenu() => ShowMenu("Align selected shapes", Enum.GetValues<Alignment>().Select(value => (value.ToString(), (Action)(() => Session.Align(value)))));
    private void ShowArrangeMenu() => ShowMenu("Position", [("Distribute horizontally", () => Session.Distribute(true)), ("Distribute vertically", () => Session.Distribute(false)), ("Bring to front", () => Session.BringToFront(true)), ("Send to back", () => Session.BringToFront(false)), ("Rotate 90°", () => Rotate(90)), ("Re-layout page", () => { Session.AutoLayout(); Surface.Fit(); })]);
    private void ShowLineWeights() => ShowMenu("Line weight", new[] { .5, 1, 1.5, 2, 3, 4, 6 }.Select(value => ($"{value:0.#} px", (Action)(() => { Session.Format(s => s.StrokeWidth = value); Session.FormatConnector(e => e.Width = value); }))));
    private void ShowArrowMenu() => ShowMenu("End arrowhead", Enum.GetValues<ArrowHead>().Select(value => (value.ToString(), (Action)(() => Session.FormatConnector(e => e.EndArrow = value)))));
    private void Rotate(double degrees)
    {
        if (!HasShapes()) return;
        Session.Execute("Rotate shapes", () => { foreach (var shape in Session.EditableShapes) shape.Rotation = (shape.Rotation + degrees) % 360; });
    }
    private void PageOrientation(bool landscape)
    {
        Session.Execute("Page orientation", () => { var small = Math.Min(Session.Page.Width, Session.Page.Height); var large = Math.Max(Session.Page.Width, Session.Page.Height); Session.Page.Width = landscape ? large : small; Session.Page.Height = landscape ? small : large; }); Surface.Fit();
    }
    private void ApplyTheme(string fill, string stroke)
    {
        Session.Execute("Apply theme", () =>
        {
            foreach (var shape in Session.Page.Shapes.Where(s => !Session.Page.IsLocked(s) && s.Kind is not ShapeKind.Text and not ShapeKind.Note and not ShapeKind.Annotation)) { shape.Style.Fill = fill; shape.Style.Stroke = stroke; shape.Style.TextColor = "#253858"; }
            foreach (var edge in Session.Page.Connectors) edge.Color = stroke;
        });
    }
    private void LockSelection(bool locked)
    {
        if (Session.SelectedShapes.Count == 0) return;
        Session.Execute(locked ? "Lock shapes" : "Unlock shapes", () => { foreach (var shape in Session.SelectedShapes) shape.Locked = locked; });
    }
    private void ConnectSelection()
    {
        var shapes = Session.SelectedShapes; if (shapes.Count == 2) Session.Connect(shapes[0].Id, shapes[1].Id);
    }
    private void EditText()
    {
        if (Session.Selection.Count == 1) Surface.BeginTextEdit(Session.Selection.First());
    }
}
