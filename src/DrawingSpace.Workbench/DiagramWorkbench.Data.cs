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
        try { for (var i = 0; i < _dataEditorCommits.Count; i++) _dataEditorCommits[i](); }
        finally { _committingDataEditors = false; }
    }

    private void RetireDataEditors()
    {
        CommitDataEditors();
        _dataPaneGeneration++;
        _dataEditorCommits.Clear();
    }

    private void InvalidateDataPreview()
    {
        _dataPlan = null; _dataTable = null; _dataReport = "";
    }

    private TextBox DataField(string name, string text, Action<string> change)
    {
        var row = new Grid { ColumnDefinitions = { new() { Width = new GridLength(118) }, new() { Width = new GridLength(1, GridUnitType.Star) } } };
        row.Children.Add(OfficeTheme.Text(name, 11));
        var field = OfficeTheme.Field(text, name); field.MaxLength = 256;
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
        return field;
    }

    private OfficeButton DataButton(string name, OfficeIcon icon, Action action, bool enabled = true)
        => new(name, icon, action: () => Guard(() =>
        {
            CommitDataEditors(); Surface.FinishTextEdit(true); action();
        })) { IsEnabled = enabled };

    private void OpenGraphics(DataGraphicKind kind)
    {
        CommitDataEditors(); _graphicKind = kind;
        if (_pane != "datagraphics") ShowPane("datagraphics"); else RebuildProperties();
    }

    private void BuildDataGraphicsPane()
    {
        _properties.Spacing = 6;
        Paragraph("Rules are non-destructive. Up to eight overlays are stored per shape; these commands replace only the selected family.");
        var kind = new ComboBox { ItemsSource = new[] { "Color by value", "Data bar", "Icon set", "Text callout" },
            SelectedIndex = (int)_graphicKind, FontSize = 12, MinHeight = 28 };
        AutomationProperties.SetName(kind, "Data graphic kind");
        var generation = _dataPaneGeneration; var previousKind = kind.SelectedIndex;
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
