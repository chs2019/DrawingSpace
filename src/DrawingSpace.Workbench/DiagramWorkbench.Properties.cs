using System.Globalization;

namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private void RebuildProperties()
    {
        _properties.Children.Clear();
        var title = _pane switch { "data" => "Shape Data", "layers" => "Layers", "comments" => "Comments", "validation" => "Issues", "page" => "Page Setup", _ => "Format Shape" };
        var header = new Grid { ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        header.Children.Add(OfficeTheme.Text(title, 18));
        var close = new OfficeButton("", OfficeIcon.Close, action: () => ShowPane(_pane)) { Width = 26, Height = 26 };
        AutomationProperties.SetName(close, "Close task pane"); Grid.SetColumn(close, 1); header.Children.Add(close); _properties.Children.Add(header);
        _properties.Children.Add(OfficeTheme.Rule());
        if (_pane == "layers") { BuildLayers(); return; }
        if (_pane == "validation") { BuildIssues(); return; }
        if (_pane == "page") { BuildPageProperties(); return; }
        var shape = Session.SelectedShapes.FirstOrDefault(); var edge = Session.SelectedConnectors.FirstOrDefault();
        if (_pane == "comments") { BuildComments(shape); return; }
        if (shape is null && edge is null)
        {
            Paragraph("Select a shape or connector to view its properties.");
            if (_pane == "format") BuildPageProperties();
            return;
        }
        if (_pane == "data") { BuildShapeData(shape); return; }
        if (shape is not null)
        {
            var id = shape.Id;
            Section(Session.SelectedShapes.Count > 1 ? $"{Session.SelectedShapes.Count} selected shapes" : shape.Name);
            Paragraph("Dimensions are in drawing pixels (96 px = 1 in).");
            Number("Position X", shape.X, value => EditShape(id, "Change X", s => s.X = value), -1000000, 1000000);
            Number("Position Y", shape.Y, value => EditShape(id, "Change Y", s => s.Y = value), -1000000, 1000000);
            Number("Width", shape.Width, value => EditShape(id, "Change width", s => s.Width = value), 1, 100000);
            Number("Height", shape.Height, value => EditShape(id, "Change height", s => s.Height = value), 1, 100000);
            Number("Rotation", shape.Rotation, value => EditShape(id, "Rotate shape", s => s.Rotation = value % 360), -36000, 36000);
            Section("Fill"); var fill = new ColorPalette(); fill.ColorSelected += color => Guard(() => Session.Format(s => s.Fill = color)); _properties.Children.Add(fill);
            _properties.Children.Add(Command("No Fill", OfficeIcon.Fill, () => Session.Format(s => s.Fill = "#00FFFFFF")));
            Section("Line"); var line = new ColorPalette(); line.ColorSelected += color => Guard(() => Session.Format(s => s.Stroke = color)); _properties.Children.Add(line);
            Number("Line width", shape.Style.StrokeWidth, value => Session.Format(s => s.StrokeWidth = value), 0, 100);
            _properties.Children.Add(Toggle("Dashed line", OfficeIcon.Line, () => Session.Page.Find(id)?.Style.Dashed ?? false, value => Session.Format(s => s.Dashed = value)));
            Number("Opacity", shape.Style.Opacity, value => Session.Format(s => s.Opacity = value), 0, 1);
            Section("Text");
            var text = OfficeTheme.Field(shape.Text, "Label text"); text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.MinHeight = 70; text.MaxHeight = 140;
            text.LostFocus += (_, _) => { if (!_refreshing && Session.Page.Find(id) is { } current && current.Text != text.Text) Guard(() => EditShape(id, "Edit label", s => s.Text = text.Text)); };
            _properties.Children.Add(text);
            Number("Font size", shape.Style.FontSize, value => Session.Format(s => s.FontSize = value), 1, 1024);
            _properties.Children.Add(OfficeTheme.Row(Command("Bold", OfficeIcon.Bold, () => Session.Format(s => s.Bold = !s.Bold)), Command("Italic", OfficeIcon.Italic, () => Session.Format(s => s.Italic = !s.Italic))));
            Section("Layer");
            var layers = new ComboBox { ItemsSource = Session.Page.Layers.Select(l => l.Name).ToArray(), SelectedIndex = Session.Page.Layers.FindIndex(l => l.Id == shape.LayerId), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 28, FontSize = 12 };
            AutomationProperties.SetName(layers, "Shape layer");
            layers.SelectionChanged += (_, _) => { if (!_refreshing && layers.SelectedIndex >= 0) Guard(() => EditShape(id, "Assign layer", s => s.LayerId = Session.Page.Layers[layers.SelectedIndex].Id)); };
            _properties.Children.Add(layers);
            _properties.Children.Add(Command(shape.Locked ? "Unlock shape" : "Lock shape", OfficeIcon.Lock, () => LockSelection(!shape.Locked)));
        }
        else if (edge is not null)
        {
            var id = edge.Id;
            Section("Connector");
            var text = OfficeTheme.Field(edge.Text, "Connector label");
            text.LostFocus += (_, _) => { if (!_refreshing && Session.Page.Connectors.FirstOrDefault(c => c.Id == id)?.Text != text.Text) Guard(() => Session.FormatConnector(c => c.Text = text.Text)); };
            _properties.Children.Add(text);
            _properties.Children.Add(Command("Right-angle route", OfficeIcon.Connector, () => Session.FormatConnector(c => c.Kind = ConnectorKind.Orthogonal)));
            _properties.Children.Add(Command("Straight route", OfficeIcon.Line, () => Session.FormatConnector(c => c.Kind = ConnectorKind.Straight)));
            Number("Line width", edge.Width, value => Session.FormatConnector(c => c.Width = value), .1, 100);
            _properties.Children.Add(Toggle("Dashed line", OfficeIcon.Line, () => Session.Page.Connectors.FirstOrDefault(c => c.Id == id)?.Dashed ?? false, value => Session.FormatConnector(c => c.Dashed = value)));
            var colors = new ColorPalette(); colors.ColorSelected += color => Guard(() => Session.FormatConnector(c => c.Color = color)); _properties.Children.Add(colors);
            _properties.Children.Add(Command("Arrowheads", OfficeIcon.Connector, ShowArrowMenu));
            Paragraph($"Source: {Session.Page.Find(edge.SourceId)?.Name ?? "Free endpoint"}\nTarget: {Session.Page.Find(edge.TargetId)?.Name ?? "Free endpoint"}");
        }
    }
    private void EditShape(string id, string name, Action<Shape> action)
    {
        if (Session.Page.Find(id) is not { } shape || Session.Page.IsLocked(shape)) return;
        Session.Execute(name, () => action(shape));
    }
    private void Section(string text)
    {
        var label = OfficeTheme.Text(text, 12, OfficeTheme.Accent, true); label.Margin = new Thickness(0, 8, 0, 0); _properties.Children.Add(label);
    }
    private void Paragraph(string text)
        => _properties.Children.Add(new TextBlock { Text = text, FontFamily = OfficeTheme.Font, FontSize = 12, Foreground = OfficeTheme.Brush(OfficeTheme.Secondary), TextWrapping = TextWrapping.Wrap });
    private void Number(string name, double value, Action<double> change, double minimum, double maximum)
    {
        var row = new Grid { ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = new GridLength(108) } } };
        row.Children.Add(OfficeTheme.Text(name));
        var field = OfficeTheme.Field(value.ToString("0.###", CultureInfo.InvariantCulture), name);
        void Commit()
        {
            if (_refreshing) return;
            if (!double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < minimum || number > maximum)
            {
                ShowStatus($"{name} must be between {minimum} and {maximum}.", true); field.Text = value.ToString("0.###", CultureInfo.InvariantCulture); return;
            }
            if (Math.Abs(number - value) < 1e-8) return;
            Guard(() => change(number));
        }
        field.LostFocus += (_, _) => Commit(); field.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Commit(); e.Handled = true; Surface.FocusCanvas(); } };
        Grid.SetColumn(field, 1); row.Children.Add(field); _properties.Children.Add(row);
    }
    private void BuildPageProperties()
    {
        Section(Session.Page.Name);
        Number("Page width", Session.Page.Width, value => Session.Execute("Page width", () => Session.Page.Width = value), 24, 100000);
        Number("Page height", Session.Page.Height, value => Session.Execute("Page height", () => Session.Page.Height = value), 24, 100000);
        Paragraph("Page dimensions use 96 drawing pixels per inch. PDF export uses 72 points per inch.");
        _properties.Children.Add(Command("Landscape", OfficeIcon.Landscape, () => PageOrientation(true)));
        _properties.Children.Add(Command("Portrait", OfficeIcon.Portrait, () => PageOrientation(false)));
        _properties.Children.Add(Command("Fit page to drawing", OfficeIcon.Fit, () => { Session.AutoSizePage(); Surface.Fit(); }));
        Section("Page color"); var palette = new ColorPalette(); palette.ColorSelected += color => Guard(() => Session.Execute("Page color", () => Session.Page.Background = color)); _properties.Children.Add(palette);
    }
    private void BuildLayers()
    {
        Paragraph("Visibility, editing protection and export inclusion are independent for each layer.");
        foreach (var layer in Session.Page.Layers)
        {
            var id = layer.Id;
            var title = OfficeTheme.Text(layer.Name, 13, OfficeTheme.TextColor, true); _properties.Children.Add(title);
            _properties.Children.Add(OfficeTheme.Row(
                Toggle("Visible", OfficeIcon.Eye, () => Session.Page.Layers.First(l => l.Id == id).Visible, value => Session.Execute("Layer visibility", () => Session.Page.Layers.First(l => l.Id == id).Visible = value)),
                Toggle("Locked", OfficeIcon.Lock, () => Session.Page.Layers.First(l => l.Id == id).Locked, value => Session.Execute("Layer lock", () => Session.Page.Layers.First(l => l.Id == id).Locked = value))));
            _properties.Children.Add(Toggle("Include in export", OfficeIcon.Export, () => Session.Page.Layers.First(l => l.Id == id).Printable, value => Session.Execute("Layer export", () => Session.Page.Layers.First(l => l.Id == id).Printable = value)));
            _properties.Children.Add(Command("Assign selected shapes", OfficeIcon.Layers, () => Session.Execute("Assign layer", () => { foreach (var shape in Session.EditableShapes) shape.LayerId = id; }), enabled: HasShapes));
            _properties.Children.Add(OfficeTheme.Rule());
        }
        _properties.Children.Add(Command("New layer", OfficeIcon.Add, () => RunAsync(AddLayerAsync)));
    }
    private void BuildShapeData(Shape? shape)
    {
        if (shape is null) { Paragraph("Select a shape to inspect its data."); return; }
        var id = shape.Id; Section(shape.Name);
        foreach (var property in shape.Data)
        {
            var key = property.Key; _properties.Children.Add(OfficeTheme.Text(key, 11, OfficeTheme.Secondary));
            var field = OfficeTheme.Field(property.Value, "Data " + key);
            field.LostFocus += (_, _) =>
            {
                if (_refreshing || Session.Page.Find(id) is not { } current || current.Data.GetValueOrDefault(key) == field.Text) return;
                Guard(() => EditShape(id, "Edit shape data", s => s.Data[key] = field.Text));
            };
            _properties.Children.Add(field);
        }
        if (shape.Data.Count == 0) Paragraph("This shape has no custom data. Add properties such as owner, status, cost or equipment tag.");
        _properties.Children.Add(Command("Add property", OfficeIcon.Add, () => RunAsync(AddDataAsync), enabled: HasShapes));
        _properties.Children.Add(Command("Export page data as CSV", OfficeIcon.Export, () => RunAsync(ExportDataAsync)));
    }
    private void BuildComments(Shape? selected)
    {
        var shapes = selected is not null ? new[] { selected } : Session.Page.Shapes.ToArray();
        var count = 0;
        foreach (var shape in shapes)
        {
            if (shape.Comments.Count == 0) continue;
            Section(shape.Name);
            foreach (var comment in shape.Comments)
            {
                Paragraph(comment); _properties.Children.Add(OfficeTheme.Rule()); count++;
            }
        }
        if (count == 0) Paragraph(selected is null ? "Select a shape to add a local comment." : "No comments on this shape.");
        _properties.Children.Add(Command("New comment", OfficeIcon.Comment, () => RunAsync(AddCommentAsync), enabled: () => Session.SelectedShapes.Count == 1));
    }
    private void BuildIssues()
    {
        var issues = DiagramValidator.Check(Session.Page);
        if (issues.Count == 0) { Section("No issues found"); Paragraph("The basic connection and page checks passed. This does not validate domain-specific engineering or business rules."); return; }
        Section($"{issues.Count} items to review");
        foreach (var issue in issues.Take(100))
        {
            Paragraph(issue.Severity + ": " + issue.Message);
            _properties.Children.Add(Command("Show object", OfficeIcon.Search, () => { Session.Select(issue.ObjectId); Surface.Fit(true); })); _properties.Children.Add(OfficeTheme.Rule());
        }
        if (issues.Count > 100) Paragraph("Only the first 100 issues are shown. Resolve these and check again.");
    }
}
