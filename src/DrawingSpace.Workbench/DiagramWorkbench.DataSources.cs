using System.Globalization;

namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private XlsxDataWorkbook? _excelWorkbook;
    private string _excelName = "", _excelSheet = "", _excelHeader = "1";
    private IReadOnlyList<string> _excelDiagnostics = [];
    private long _sourceLoadVersion;

    private async Task OpenDataFileAsync()
    {
        CommitDataEditors();
        if (_storage is not ITabularWorkspaceStorage storage)
            throw new InvalidOperationException("This host does not provide a CSV picker; paste CSV text instead.");
        var request = ++_sourceLoadVersion;
        var file = await storage.OpenTableAsync();
        if (file is null || request != _sourceLoadVersion) return;
        RetireDataEditors();
        _excelWorkbook = null; _excelDiagnostics = [];
        _dataCsv = file.Value.Text;
        _dataDelimiter = Path.GetExtension(file.Value.Name).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
        InvalidateDataPreview();
        ShowStatus("Loaded data source " + file.Value.Name + "; preview before applying.");
        if (_pane != "externaldata") ShowPane("externaldata"); else RebuildProperties();
    }

    private async Task OpenExcelFileAsync()
    {
        CommitDataEditors();
        if (_storage is not IExcelWorkspaceStorage storage)
            throw new InvalidOperationException("This host does not provide an Excel workbook picker.");
        var request = ++_sourceLoadVersion;
        var file = await storage.OpenWorkbookAsync();
        if (file is null || request != _sourceLoadVersion) return;
        var workbook = XlsxDataWorkbook.Open(file.Value.Bytes);
        RetireDataEditors();
        _excelWorkbook = workbook; _excelName = file.Value.Name;
        _excelSheet = workbook.Worksheets.FirstOrDefault(s => !s.Hidden)?.Name ?? workbook.Worksheets[0].Name;
        _excelHeader = "1"; _excelDiagnostics = [];
        _dataSource = ExcelSourceIdentity();
        InvalidateDataPreview();
        ShowStatus("Loaded workbook " + _excelName + "; choose a worksheet and preview.");
        if (_pane != "externaldata") ShowPane("externaldata"); else RebuildProperties();
    }

    private string ExcelSourceIdentity()
    {
        var name = Path.GetFileNameWithoutExtension(_excelName);
        if (name.Length > 100) name = name[..100];
        return name + "/" + _excelSheet;
    }

    private void PreviewDataSource()
    {
        _dataPlan = null; _dataTable = null; _excelDiagnostics = [];
        if (_excelWorkbook is null)
            _dataTable = CsvDataTable.Parse(_dataCsv, _dataSource.Trim(), _dataKey.Trim(), _dataDelimiter);
        else
        {
            if (!int.TryParse(_excelHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var row))
                throw new InvalidOperationException("Header row must be a positive worksheet row number.");
            var result = _excelWorkbook.ReadTable(_excelSheet, _dataSource.Trim(), _dataKey.Trim(), row);
            _dataTable = result.Table; _excelDiagnostics = result.Diagnostics;
        }
        _dataPlan = Session.PreviewDataRefresh(_dataTable, _dataMatch.Trim(), _dataSelectedOnly, _dataOverwrite);
        _dataReport = $"{_dataTable.Rows.Count} rows; {_dataPlan.ChangedShapes} shapes to update; {_dataPlan.TotalIssues} conflicts or warnings.";
        ShowStatus(_dataReport); RebuildProperties();
    }

    private void BuildExternalDataPane()
    {
        _properties.Spacing = 6;
        _properties.Children.Add(OfficeTheme.Row(
            DataButton("Open CSV", OfficeIcon.Open, () => RunAsync(OpenDataFileAsync), _storage is ITabularWorkspaceStorage),
            DataButton("Open Excel", OfficeIcon.Open, () => RunAsync(OpenExcelFileAsync), _storage is IExcelWorkspaceStorage)));
        TextBox? sourceEditor = null;
        if (_excelWorkbook is not null)
            BuildExcelSourceEditors(value => { if (sourceEditor is not null) sourceEditor.Text = value; });
        sourceEditor = DataField("Source identity", _dataSource, value => { _dataSource = value; InvalidateDataPreview(); });
        DataField("Key column", _dataKey, value => { _dataKey = value; InvalidateDataPreview(); });
        DataField("Match field", _dataMatch, value => { _dataMatch = value; InvalidateDataPreview(); });
        Paragraph("Match by $text, $name, $id, or a shape-data field. Linked shapes refresh by stored key, not row order.");
        if (_excelWorkbook is null) BuildCsvSourceEditors();
        var generation = _dataPaneGeneration;
        CheckBox Check(string name, bool current, Action<bool> change)
        {
            var check = new CheckBox { Content = name, IsChecked = current, MinHeight = 24, FontSize = 12 };
            AutomationProperties.SetName(check, name); var previous = current;
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
            DataButton("Preview Refresh", OfficeIcon.Search, PreviewDataSource),
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
        _properties.Children.Add(DataButton("Unlink Data", OfficeIcon.Data, Session.UnlinkSelectedData));
        foreach (var diagnostic in _excelDiagnostics.Take(4)) Paragraph(diagnostic);
        if (_dataPlan is { } preview)
        {
            foreach (var issue in preview.Issues.Take(6)) Paragraph($"{issue.Field}: {issue.Reason}");
            if (preview.TotalIssues > 6) Paragraph("Additional issues are available through DataRefreshPlan.Issues.");
        }
    }

    private void BuildExcelSourceEditors(Action<string> sourceChanged)
    {
        var workbook = _excelWorkbook!;
        var sheets = workbook.Worksheets.Select(s => s.Name).ToArray();
        var selector = new ComboBox
        {
            ItemsSource = workbook.Worksheets.Select(s => s.Name + (s.Hidden ? " (hidden)" : "")).ToArray(),
            SelectedIndex = Array.IndexOf(sheets, _excelSheet), MinHeight = 28, FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(selector, "Excel worksheet");
        var generation = _dataPaneGeneration; var previous = selector.SelectedIndex;
        void Commit()
        {
            if (generation != _dataPaneGeneration || selector.SelectedIndex < 0 || selector.SelectedIndex == previous) return;
            previous = selector.SelectedIndex; _excelSheet = sheets[previous];
            _dataSource = ExcelSourceIdentity(); sourceChanged(_dataSource);
            _excelDiagnostics = []; InvalidateDataPreview();
        }
        _dataEditorCommits.Add(Commit);
        selector.SelectionChanged += (_, _) => { if (_pane == "externaldata") Commit(); };
        _properties.Children.Add(selector);
        DataField("Excel header row", _excelHeader, value => { _excelHeader = value; InvalidateDataPreview(); });
        _properties.Children.Add(DataButton("Use CSV Text", OfficeIcon.Text, () =>
        {
            ++_sourceLoadVersion; RetireDataEditors(); _excelWorkbook = null; _excelDiagnostics = [];
            InvalidateDataPreview(); RebuildProperties();
        }));
    }

    private void BuildCsvSourceEditors()
    {
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
        var csv = OfficeTheme.Field("", "CSV source");
        csv.AcceptsReturn = true; csv.TextWrapping = TextWrapping.NoWrap;
        csv.Height = 74; csv.MaxLength = CsvDataTable.MaximumCharacters; csv.Text = _dataCsv;
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
    }
}
