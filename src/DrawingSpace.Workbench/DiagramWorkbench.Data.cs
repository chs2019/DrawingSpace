using System.Globalization;

namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private string _dataSource = "Assets", _dataKey = "Id", _dataMatch = "$text", _dataCsv = "";
    private string _dataReport = "", _graphicField = "Progress", _graphicLabel = "";
    private string _graphicMinimum = "0", _graphicMaximum = "100";
    private char _dataDelimiter = ',';
    private bool _dataSelectedOnly, _dataOverwrite;
    private CsvDataTable? _dataTable;
    private DataRefreshPlan? _dataPlan;
    private DataGraphicKind _graphicKind;
    private bool _graphicLowerIsBetter;
    private long _dataPaneGeneration;
    private readonly List<Action> _dataEditorCommits = [];
    private bool _committingDataEditors;

    private void CommitDataEditors()
    {
        if (_committingDataEditors) return;
        _committingDataEditors = true;
        try
        {
            for (var i = 0; i < _dataEditorCommits.Count; i++) _dataEditorCommits[i]();
        }
        finally { _committingDataEditors = false; }
    }

    private void RetireDataEditors()
    {
        // Read current control values before a queued refresh detaches them.
        CommitDataEditors();
        _dataPaneGeneration++;
        _dataEditorCommits.Clear();
    }

    private void InvalidateDataPreview()
    {
        _dataPlan = null; _dataTable = null; _dataReport = "";
    }
    private void DataField(string name, string text, Action<string> change)
    {
        var row = new Grid { ColumnDefinitions = { new() { Width = new GridLength(118) }, new() { Width = new GridLength(1, GridUnitType.Star) } } };
        row.Children.Add(OfficeTheme.Text(name, 11));
        var field = OfficeTheme.Field(text, name); field.MaxLength = 256;
        // TextChanged can be deferred. Commands and pane rebuilds also commit
        // the control's current value, without waiting for that notification.
        var generation = _dataPaneGeneration; var pane = _pane; var previous = field.Text;
        void Commit()
        {
            if (generation != _dataPaneGeneration || field.Text == previous) return;
            previous = field.Text; change(previous);
        }
        _dataEditorCommits.Add(Commit);
        field.TextChanged += (_, _) => { if (_pane == pane) Commit(); };
        field.LostFocus += (_, _) => { if (_pane == pane) Commit(); };
        Grid.SetColumn(field, 1); row.Children.Add(field); _properties.Children.Add(row);
    }
    private OfficeButton DataButton(string name, OfficeIcon icon, Action action, bool enabled = true)
        => new(name, icon, action: () => Guard(() =>
        {
            CommitDataEditors();
            Surface.FinishTextEdit(true);
            action();
        })) { IsEnabled = enabled };

    private async Task OpenDataFileAsync()
    {
        CommitDataEditors();
        if (_storage is not ITabularWorkspaceStorage storage)
            throw new InvalidOperationException("This host does not provide a CSV picker; paste CSV text instead.");
        var file = await storage.OpenTableAsync();
        if (file is null) return;
        _dataCsv = file.Value.Text;
        _dataDelimiter = Path.GetExtension(file.Value.Name).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
        InvalidateDataPreview();
        ShowStatus("Loaded data source " + file.Value.Name + "; preview before applying.");
        if (_pane != "externaldata") ShowPane("externaldata"); else RebuildProperties();
    }

    private void BuildExternalDataPane()
    {
        _properties.Spacing = 6;
        _properties.Children.Add(OfficeTheme.Row(
            DataButton("Open CSV", OfficeIcon.Open, () => RunAsync(OpenDataFileAsync), _storage is ITabularWorkspaceStorage),
            DataButton("Unlink Data", OfficeIcon.Data, Session.UnlinkSelectedData)));
        DataField("Source identity", _dataSource, value => { _dataSource = value; InvalidateDataPreview(); });
        DataField("Key column", _dataKey, value => { _dataKey = value; InvalidateDataPreview(); });
        DataField("Match field", _dataMatch, value => { _dataMatch = value; InvalidateDataPreview(); });
        var delimiter = new ComboBox { ItemsSource = new[] { "Comma", "Semicolon", "Tab" },
            SelectedIndex = _dataDelimiter == ',' ? 0 : _dataDelimiter == ';' ? 1 : 2, MinHeight = 28, FontSize = 12 };
        AutomationProperties.SetName(delimiter, "CSV delimiter");
        var generation = _dataPaneGeneration; var previousDelimiter = delimiter.SelectedIndex;
        void CommitDelimiter()
        {
            if (generation != _dataPaneGeneration || delimiter.SelectedIndex < 0 || delimiter.SelectedIndex == previousDelimiter) return;
            previousDelimiter = delimiter.SelectedIndex;
            _dataDelimiter = previousDelimiter == 1 ? ';' : previousDelimiter == 2 ? '\t' : ',';
            InvalidateDataPreview();
        }
        _dataEditorCommits.Add(CommitDelimiter);
        delimiter.SelectionChanged += (_, _) => { if (_pane == "externaldata") CommitDelimiter(); };
        _properties.Children.Add(delimiter);
        Paragraph("Match by $text, $name, $id, or a shape-data field. Linked shapes refresh by stored key, not row order.");
        // Configure multiline semantics before assigning text: a single-line TextBox
        // truncates an imported CSV at its first newline during initialization.
        var csv = OfficeTheme.Field("", "CSV source");
        csv.AcceptsReturn = true; csv.TextWrapping = TextWrapping.NoWrap;
        csv.Height = 74; csv.MaxLength = CsvDataTable.MaximumCharacters;
        csv.Text = _dataCsv;
        var previousCsv = csv.Text;
        void CommitCsv()
        {
            if (generation != _dataPaneGeneration || csv.Text == previousCsv) return;
            previousCsv = csv.Text; _dataCsv = previousCsv; InvalidateDataPreview();
        }
        _dataEditorCommits.Add(CommitCsv);
        csv.TextChanged += (_, _) => { if (_pane == "externaldata") CommitCsv(); };
        csv.LostFocus += (_, _) => { if (_pane == "externaldata") CommitCsv(); };
        _properties.Children.Add(csv);
        CheckBox Check(string name, bool current, Action<bool> change)
        {
            var check = new CheckBox { Content = name, IsChecked = current, MinHeight = 24, FontSize = 12 };
            AutomationProperties.SetName(check, name);
            var previous = current;
            void Commit()
            {
                var value = check.IsChecked == true;
                if (generation != _dataPaneGeneration || value == previous) return;
                previous = value; change(value); _dataPlan = null;
            }
            _dataEditorCommits.Add(Commit);
            check.Checked += (_, _) => { if (_pane == "externaldata") Commit(); };
            check.Unchecked += (_, _) => { if (_pane == "externaldata") Commit(); };
            return check;
        }
        _properties.Children.Add(Check("Selected shapes only", _dataSelectedOnly, value => _dataSelectedOnly = value));
        _properties.Children.Add(Check("Overwrite local conflicts", _dataOverwrite, value => _dataOverwrite = value));
        _properties.Children.Add(OfficeTheme.Row(
            DataButton("Preview Refresh", OfficeIcon.Search, () =>
            {
                _dataPlan = null;
                _dataTable = CsvDataTable.Parse(_dataCsv, _dataSource.Trim(), _dataKey.Trim(), _dataDelimiter);
                _dataPlan = Session.PreviewDataRefresh(_dataTable, _dataMatch.Trim(), _dataSelectedOnly, _dataOverwrite);
                _dataReport = $"{_dataTable.Rows.Count} rows; {_dataPlan.ChangedShapes} shapes to update; {_dataPlan.TotalIssues} conflicts or warnings.";
                ShowStatus(_dataReport); RebuildProperties();
            }),
            DataButton("Apply Refresh", OfficeIcon.Check, () =>
            {
                var plan = _dataPlan ?? throw new InvalidOperationException("Preview the source before applying it.");
                _dataPlan = null;
                var changed = Session.ApplyDataRefresh(plan);
                _dataReport = changed ? $"Updated {plan.ChangedShapes} shapes in one undoable operation." : "No data changes required.";
                ShowStatus(_dataReport); RebuildProperties();
            })));
        if (_dataReport.Length > 0) Paragraph(_dataReport);
        if (_dataTable is not null) _properties.Children.Add(new DataPreviewGrid(_dataTable));
        if (_dataPlan is { } preview)
        {
            foreach (var issue in preview.Issues.Take(6))
                Paragraph($"{issue.Field}: {issue.Reason}");
            if (preview.TotalIssues > 6) Paragraph("Additional issues are available through DataRefreshPlan.Issues.");
        }
    }

    private void OpenGraphics(DataGraphicKind kind)
    {
        CommitDataEditors();
        _graphicKind = kind;
        if (_pane != "datagraphics") ShowPane("datagraphics"); else RebuildProperties();
    }

    private void BuildDataGraphicsPane()
    {
        _properties.Spacing = 6;
        Paragraph("Rules are non-destructive. Up to eight overlays are stored per shape; these commands replace only the selected family.");
        var kind = new ComboBox { ItemsSource = new[] { "Color by value", "Data bar", "Icon set", "Text callout" },
            SelectedIndex = (int)_graphicKind, FontSize = 12, MinHeight = 28 };
        AutomationProperties.SetName(kind, "Data graphic kind");
        var generation = _dataPaneGeneration;
        var previousKind = kind.SelectedIndex;
        void CommitKind()
        {
            if (generation != _dataPaneGeneration || kind.SelectedIndex < 0 || kind.SelectedIndex == previousKind) return;
            previousKind = kind.SelectedIndex; _graphicKind = (DataGraphicKind)previousKind;
        }
        _dataEditorCommits.Add(CommitKind);
        kind.SelectionChanged += (_, _) => { if (_pane == "datagraphics") CommitKind(); };
        _properties.Children.Add(kind);
        DataField("Graphic field", _graphicField, value => _graphicField = value);
        DataField("Graphic label", _graphicLabel, value => _graphicLabel = value);
        DataField("Range minimum", _graphicMinimum, value => _graphicMinimum = value);
        DataField("Range maximum", _graphicMaximum, value => _graphicMaximum = value);
        var lower = new CheckBox { Content = "Lower is better", IsChecked = _graphicLowerIsBetter, MinHeight = 24, FontSize = 12 };
        AutomationProperties.SetName(lower, "Lower is better");
        var previousLower = lower.IsChecked == true;
        void CommitLower()
        {
            var value = lower.IsChecked == true;
            if (generation != _dataPaneGeneration || value == previousLower) return;
            previousLower = value; _graphicLowerIsBetter = value;
        }
        _dataEditorCommits.Add(CommitLower);
        lower.Checked += (_, _) => { if (_pane == "datagraphics") CommitLower(); };
        lower.Unchecked += (_, _) => { if (_pane == "datagraphics") CommitLower(); };
        _properties.Children.Add(lower);
        Paragraph("Numeric bands: lower third red, middle amber, upper green. Text callouts display the value literally. Missing/non-numeric numeric fields do not draw a graphic.");
        _properties.Children.Add(DataButton("Apply Data Graphic", OfficeIcon.Fill, () =>
        {
            var minimum = double.Parse(_graphicMinimum, CultureInfo.InvariantCulture);
            var maximum = double.Parse(_graphicMaximum, CultureInfo.InvariantCulture);
            var box = _graphicKind switch
            {
                DataGraphicKind.IconSet => new RectD(1.05, 0, .3, .4),
                DataGraphicKind.TextCallout => new RectD(0, 1.5, 1.4, .36),
                _ => new RectD(0, 1.08, 1, .3)
            };
            Session.SetSelectedDataGraphic(new()
            {
                Kind = _graphicKind, Field = _graphicField.Trim(), Label = _graphicLabel,
                Minimum = minimum, Maximum = maximum, LowerIsBetter = _graphicLowerIsBetter, Bounds = box
            });
            ShowStatus("Applied " + _graphicKind + " to selected shapes.");
        }));
        _properties.Children.Add(DataButton("Remove Data Graphics", OfficeIcon.Delete, Session.ClearSelectedDataGraphics));
        Paragraph("Graphics follow the complete shape transform and are included in SVG/PNG/PDF. Save native JSON to retain live rules and data links.");
    }
}
