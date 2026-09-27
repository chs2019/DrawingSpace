using System.Globalization;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private string? _formulaShapeId, _activeMasterId, _rangeShapeId;
    private string _formulaCell = "Width";
    private int _rangeStart, _rangeLength;

    private IEnumerable<RibbonGroup> DeveloperGroups()
    {
        yield return new("Shape Design", Command("ShapeSheet", OfficeIcon.Data, () => ShowPane("shapesheet"), true),
            Command("Masters", OfficeIcon.App, () => ShowPane("masters"), true),
            Command("Containers", OfficeIcon.Group, () => ShowPane("containers"), true));
        yield return new("Editing", Command("Rich Text", OfficeIcon.Text, () => ShowPane("richtext"), true),
            Command("Connections", OfficeIcon.Connector, () => ShowPane("connections"), true),
            Command("Recalculate", OfficeIcon.Layout, Session.Recalculate, true));
        yield return new("Compatibility", Command("Import Diagnostics", OfficeIcon.Check, () => ShowPane("import"), true));
    }

    private Shape? SingleEditableShape()
    {
        if (Session.SelectedShapes.Count == 1 && !Session.Page.IsLocked(Session.SelectedShapes[0])) return Session.SelectedShapes[0];
        Paragraph("Select one unlocked shape to edit these properties."); return null;
    }

    private void BuildShapeSheetPane()
    {
        var shape = SingleEditableShape(); if (shape is null) return;
        if (_formulaShapeId != shape.Id) { _formulaShapeId = shape.Id; _formulaCell = "Width"; }
        var scope = new ShapeSheetScope(Session.Document, Session.Page);
        var cell = scope.Cell(shape, _formulaCell);
        var name = OfficeTheme.Field(_formulaCell, "Cell name");
        var formula = OfficeTheme.Field(string.IsNullOrWhiteSpace(cell?.Formula) ? cell?.Value ?? scope.Evaluate(shape, _formulaCell).ToString() : cell.Formula, "Cell formula");
        formula.AcceptsReturn = true; formula.TextWrapping = TextWrapping.Wrap; formula.MinHeight = 58; formula.MaxHeight = 120;
        Section(shape.Name); Paragraph("Values use Visio internal units: inches and radians. Formulas may reference other cells or ThePage. Errors remain visible and editable.");
        _properties.Children.Add(name); _properties.Children.Add(formula);
        void Apply(bool force)
        {
            _formulaCell = name.Text.Trim(); Session.SetFormula(shape.Id, _formulaCell, formula.Text, force);
            ShowStatus("Updated " + _formulaCell);
        }
        _properties.Children.Add(OfficeTheme.Row(Command("Apply formula", OfficeIcon.Check, () => Apply(false)), Command("Override GUARD", OfficeIcon.Lock, () => Apply(true))));
        _properties.Children.Add(Command("Restore inherited cell", OfficeIcon.Undo, () => Session.RemoveFormula(shape.Id, name.Text.Trim())));
        Section("Cells and evaluated values");
        var names = new[] { "Width", "Height", "PinX", "PinY", "Angle", "LineWeight", "Char.Size", "FillForegnd", "Text" }
            .Concat(scope.Names(shape)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(256).ToArray();
        foreach (var cellName in names)
        {
            var value = scope.Evaluate(shape, cellName).ToString();
            var inherited = !shape.Cells.ContainsKey(cellName) && scope.Cell(shape, cellName) is not null;
            var button = new OfficeButton(cellName + "  =  " + (value.Length > 70 ? value[..70] + "…" : value), action: () => { _formulaCell = cellName; Refresh(); });
            ToolTipService.SetToolTip(button, (inherited ? "Inherited: " : "") + (scope.Cell(shape, cellName)?.Formula ?? value));
            _properties.Children.Add(button);
        }
        foreach (var diagnostic in Session.FormulaDiagnostics.Where(d => d.ShapeId == shape.Id).Take(20)) Paragraph(diagnostic.Cell + ": " + diagnostic.Message);
    }

    private void BuildMastersPane()
    {
        _properties.Children.Add(Command("Create master from shape", OfficeIcon.Add, () => RunAsync(async () =>
        {
            if (Session.SelectedShapes.Count != 1) { ShowStatus("Select one shape to create a master."); return; }
            var id = Session.SelectedShapes[0].Id;
            var name = await PromptAsync("Create master", "Master name", Session.SelectedShapes[0].Name);
            if (!string.IsNullOrWhiteSpace(name)) { _activeMasterId = Session.CreateMaster(id, name.Trim()).Id; Refresh(); }
        })));
        Paragraph("Open a VSSX file to add a stencil library without replacing this drawing. Insertions keep internal connectors and nested groups.");
        if (Session.Document.Masters.Count == 0) { Paragraph("This document has no masters yet."); return; }
        foreach (var master in Session.Document.Masters.Take(128))
        {
            var id = master.Id;
            var select = new OfficeButton(master.Name, OfficeIcon.App, action: () => { _activeMasterId = id; Refresh(); }) { IsSelected = id == _activeMasterId };
            var insert = new OfficeButton("Insert", OfficeIcon.Add, action: () => Guard(() => { Session.InsertMaster(id, Surface.ViewCenter); Surface.FocusCanvas(); }));
            AutomationProperties.SetName(insert, "Insert master " + master.Name);
            _properties.Children.Add(OfficeTheme.Row(select, insert));
        }
        var active = Session.Document.Masters.FirstOrDefault(m => m.Id == _activeMasterId);
        if (active is null) return;
        Section("Edit master: " + active.Name);
        var label = OfficeTheme.Field(active.Shape.Text, "Master text"); _properties.Children.Add(label);
        _properties.Children.Add(Command("Apply master text", OfficeIcon.Check, () => Session.UpdateMaster(active.Id, master => RichTextOperations.ReplaceAll(master.Shape, label.Text))));
        Number("Master width", active.Shape.Width, value => Session.UpdateMaster(active.Id, m => m.Shape.Width = value), 1, 100000);
        Number("Master height", active.Shape.Height, value => Session.UpdateMaster(active.Id, m => m.Shape.Height = value), 1, 100000);
        var palette = new ColorPalette(); palette.ColorSelected += color => Guard(() => Session.UpdateMaster(active.Id, m => m.Shape.Style.Fill = color)); _properties.Children.Add(palette);
        if (Session.SelectedShapes.Count == 1 && Session.SelectedShapes[0] is { MasterId: not null } instance)
        {
            Section("Local overrides");
            foreach (var property in instance.LocalOverrides.ToArray())
                _properties.Children.Add(Command("Reset " + property, OfficeIcon.Undo, () => Session.ResetMasterOverride(instance.Id, property)));
        }
    }

    private void BuildContainersPane()
    {
        _properties.Children.Add(Command("Container around selection", OfficeIcon.Group, () => Session.CreateContainer()));
        var shape = SingleEditableShape(); if (shape is null) return;
        var id = shape.Id;
        if (shape.Container is null)
        {
            Section("Membership");
            _properties.Children.Add(Command("Remove from container", OfficeIcon.Close, () => Session.SetContainerMembership(id, null)));
            foreach (var parent in Session.Page.Shapes.Where(s => s.Container is not null && s.Id != id).Take(128))
                _properties.Children.Add(Command("Join " + parent.Name, OfficeIcon.Group, () => Session.SetContainerMembership(id, parent.Id)));
            return;
        }
        Section(shape.Name);
        _properties.Children.Add(Command("Fit to contents", OfficeIcon.Fit, () => Session.FitContainer(id)));
        _properties.Children.Add(Command("Add swimlane", OfficeIcon.Add, () => Session.AddLane(id, "Lane " + (Session.Page.Shapes.Count(s => s.ContainerId == id) + 1))));
        foreach (var layout in Enum.GetValues<ContainerLayout>())
            _properties.Children.Add(Command(layout.ToString(), OfficeIcon.Layout, () => Session.Execute("Container layout", () =>
            {
                var container = Session.Page.Find(id)!; container.Container!.Layout = layout; container.Container.AutoResize = layout == ContainerLayout.Free;
                ContainerService.LayoutLanes(Session.Page, container);
            })));
        _properties.Children.Add(Toggle("Lock membership", OfficeIcon.Lock, () => Session.Page.Find(id)?.Container?.LockedMembership == true,
            value => EditShape(id, "Membership lock", s => s.Container!.LockedMembership = value)));
        Section("Members");
        foreach (var member in Session.Page.Shapes.Where(s => s.ContainerId == id).ToArray())
            _properties.Children.Add(Command(member.Name, OfficeIcon.App, () => Session.Select(member.Id)));
    }

    private void BuildRichTextPane()
    {
        var shape = SingleEditableShape(); if (shape is null) return;
        if (_rangeShapeId != shape.Id) { _rangeShapeId = shape.Id; _rangeStart = 0; _rangeLength = shape.Text.Length; }
        _rangeStart = Math.Clamp(_rangeStart, 0, shape.Text.Length); _rangeLength = Math.Clamp(_rangeLength, 0, shape.Text.Length - _rangeStart);
        Section(shape.Name); Paragraph("Select a range in the text field, then format it. Apply text commits typing while preserving unchanged formatting.");
        var editor = OfficeTheme.Field(shape.Text, "Rich text content"); editor.AcceptsReturn = true; editor.TextWrapping = TextWrapping.Wrap; editor.Height = 135;
        editor.SelectionChanged += (_, _) => { if (!_refreshing) { _rangeStart = editor.SelectionStart; _rangeLength = editor.SelectionLength; } };
        editor.Loaded += (_, _) => editor.Select(_rangeStart, _rangeLength);
        _properties.Children.Add(editor);
        _properties.Children.Add(Command("Apply text", OfficeIcon.Check, () => Session.SetText(shape.Id, editor.Text)));
        void Format(Action<TextSpan> update)
        {
            if (editor.Text != Session.Page.Find(shape.Id)?.Text) { ShowStatus("Apply text before formatting its range.", true); return; }
            if (_rangeLength == 0) { ShowStatus("Select a text range to format."); return; }
            Session.FormatText(shape.Id, _rangeStart, _rangeLength, update);
        }
        bool On(Func<TextSpan, bool?> read) => read(RichTextOperations.StyleAt(shape, Math.Min(_rangeStart, Math.Max(0, shape.Text.Length - 1)))) == true;
        _properties.Children.Add(OfficeTheme.Row(Command("Range bold", OfficeIcon.Bold, () => Format(s => s.Bold = !On(s => s.Bold))),
            Command("Range italic", OfficeIcon.Italic, () => Format(s => s.Italic = !On(s => s.Italic)))));
        _properties.Children.Add(OfficeTheme.Row(Command("Range underline", OfficeIcon.Text, () => Format(s => s.Underline = !On(s => s.Underline))),
            Command("Range strike", OfficeIcon.Text, () => Format(s => s.StrikeThrough = !On(s => s.StrikeThrough)))));
        Number("Range font size", shape.Style.FontSize, value => Format(s => s.FontSize = value), 1, 1024);
        var palette = new ColorPalette(); palette.ColorSelected += color => Guard(() => Format(s => s.Color = color)); _properties.Children.Add(palette);
        Section("Paragraph");
        foreach (var alignment in Enum.GetValues<ParagraphAlignment>())
            _properties.Children.Add(Command("Text " + alignment, OfficeIcon.Align, () => Session.FormatParagraph(shape.Id, _rangeStart, p => p.Alignment = alignment)));
        _properties.Children.Add(Command("Toggle bullets", OfficeIcon.Text, () => Session.FormatParagraph(shape.Id, _rangeStart, p => p.Bullet = !p.Bullet)));
    }

    private void BuildConnectionsPane()
    {
        Paragraph("Select a connector to drag its endpoint circles, waypoint squares or label diamond. Alt-click a segment to insert a waypoint. Shift-click a waypoint removes it.");
        if (Session.SelectedConnectors.Count == 1)
        {
            var edge = Session.SelectedConnectors[0];
            _properties.Children.Add(Command("Clear waypoints", OfficeIcon.Undo, () => Session.SetWaypoints(edge.Id, [])));
            foreach (var style in Enum.GetValues<LineJumpStyle>())
                _properties.Children.Add(Command("Jumps " + style, OfficeIcon.Line, () => Session.FormatConnector(c => c.LineJumps = style)));
            Number("Jump size", edge.JumpSize, value => Session.FormatConnector(c => c.JumpSize = value), 1, 100);
            Number("Label position", edge.LabelPosition, value => Session.FormatConnector(c => c.LabelPosition = value), 0, 1);
        }
        else if (Session.SelectedShapes.Count == 1)
        {
            var shape = Session.SelectedShapes[0];
            Section("Connection points");
            _properties.Children.Add(Command("Add center point", OfficeIcon.Add, () => Session.AddConnectionPoint(shape.Id, shape.WorldMatrix.Map(new PointD(.5, .5)))));
            foreach (var point in shape.ConnectionPoints.Take(64)) Paragraph(point.Name + $"  ({point.Position.X:0.###}, {point.Position.Y:0.###})");
            Paragraph("Custom points are available for endpoint attachment and respect incoming/outgoing direction flags.");
        }
    }

    private void BuildImportPane()
    {
        Paragraph("Compatibility diagnostics describe unsupported or approximated constructs. A successful import does not certify lossless Visio compatibility.");
        if (_importDiagnostics.Count == 0) Paragraph("No diagnostics from the most recent import or Visio export.");
        foreach (var diagnostic in _importDiagnostics.Take(256)) Paragraph(diagnostic.Code + " — " + diagnostic.Message);
    }
}
