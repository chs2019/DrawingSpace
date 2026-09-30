using DrawingSpace.Documents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DrawingSpace.Controls;

/// <summary>Bounded source view: five rows and three columns are realized at a time. Sorting/filtering never changes the source or refresh scope.</summary>
public sealed class DataPreviewGrid : UserControl
{
    private readonly CsvDataTable _source;
    private readonly TextBox _filter;
    private readonly StackPanel _body;
    private TabularDataView _view;
    private string _appliedFilter = "";
    private string? _sortColumn;
    private bool _descending, _numeric;
    private int _row, _column;

    public DataPreviewGrid(CsvDataTable source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _view = TabularDataView.Create(source);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var panel = OfficeTheme.Column(); panel.Spacing = 4;
        var search = new Grid { ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        _filter = OfficeTheme.Field("", "Data filter"); _filter.PlaceholderText = "Filter source rows"; _filter.MaxLength = 256;
        _filter.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { ApplyFilter(); e.Handled = true; } };
        search.Children.Add(_filter);
        var apply = new OfficeButton("Filter", OfficeIcon.Search, action: ApplyFilter) { Height = 28 };
        AutomationProperties.SetName(apply, "Filter data rows"); Grid.SetColumn(apply, 1); search.Children.Add(apply);
        panel.Children.Add(search);
        var options = OfficeTheme.Row();
        var numeric = new CheckBox { Content = "Numeric sort", FontSize = 11, MinHeight = 24 };
        AutomationProperties.SetName(numeric, "Numeric data sort");
        numeric.Checked += (_, _) => { _numeric = true; Requery(); };
        numeric.Unchecked += (_, _) => { _numeric = false; Requery(); };
        options.Children.Add(numeric);
        var clear = new OfficeButton("Clear", OfficeIcon.None, action: () =>
        { _filter.Text = ""; _appliedFilter = ""; _sortColumn = null; _descending = false; Requery(); }) { Height = 25 };
        AutomationProperties.SetName(clear, "Clear data view"); options.Children.Add(clear); panel.Children.Add(options);
        _body = OfficeTheme.Column(); _body.Spacing = 4; panel.Children.Add(_body); Content = panel;
        Render();
    }

    private void ApplyFilter() { _appliedFilter = _filter.Text; Requery(); }
    private void Requery()
    {
        _view = TabularDataView.Create(_source, _appliedFilter, _sortColumn, _descending, _numeric);
        _row = 0; Render();
    }

    private void Render()
    {
        _body.Children.Clear();
        var grid = new Grid();
        var columns = Math.Min(3, _source.Columns.Count - _column);
        var rows = Math.Min(5, _view.RowOrdinals.Count - _row);
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i <= rows; i++) grid.RowDefinitions.Add(new() { Height = new GridLength(25) });
        void Cell(int row, int column, string value)
        {
            var length = Math.Min(120, value.Length);
            if (length < value.Length && length > 0 && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
            var display = length < value.Length ? value[..length] + "…" : value;
            var text = OfficeTheme.Text(display, 11); text.TextTrimming = TextTrimming.CharacterEllipsis; text.Margin = new Thickness(5, 1, 3, 1);
            AutomationProperties.SetName(text, $"Data cell {row} {_source.Columns[_column + column]}: {display}");
            var border = new Border
            {
                Child = text, BorderBrush = OfficeTheme.Brush("#D6DFEA"), BorderThickness = new Thickness(0, 0, 1, 1),
                Background = OfficeTheme.Brush(row % 2 == 0 ? "#F8FAFC" : "#FFFFFF")
            };
            Grid.SetRow(border, row); Grid.SetColumn(border, column); grid.Children.Add(border);
        }
        for (var c = 0; c < columns; c++)
        {
            var key = _source.Columns[_column + c];
            var header = new OfficeButton(key + (_sortColumn == key ? _descending ? " ▼" : " ▲" : ""), OfficeIcon.None,
                action: () => { _descending = _sortColumn == key && !_descending; _sortColumn = key; Requery(); })
                { Height = 25, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(header, "Sort data column " + key);
            Grid.SetRow(header, 0); Grid.SetColumn(header, c); grid.Children.Add(header);
            for (var r = 0; r < rows; r++) Cell(r + 1, c, _view[_row + r][key]);
        }
        _body.Children.Add(grid);
        var label = OfficeTheme.Text($"Rows {(rows == 0 ? 0 : _row + 1)}–{_row + rows} / {_view.RowOrdinals.Count}  ·  Columns {_column + 1}–{_column + columns} / {_source.Columns.Count}", 10);
        AutomationProperties.SetName(label, "Data preview range"); _body.Children.Add(label);
        OfficeButton Button(string name, string text, bool enabled, Action action)
        {
            var button = new OfficeButton(text, OfficeIcon.None, action: () => { action(); Render(); }) { IsEnabled = enabled, MinWidth = 48, Height = 25 };
            AutomationProperties.SetName(button, name); return button;
        }
        _body.Children.Add(OfficeTheme.Row(
            Button("Previous data rows", "Rows ‹", _row > 0, () => _row = Math.Max(0, _row - 5)),
            Button("Next data rows", "Rows ›", _row + rows < _view.RowOrdinals.Count, () => _row += 5),
            Button("Previous data columns", "Cols ‹", _column > 0, () => _column = Math.Max(0, _column - 3)),
            Button("Next data columns", "Cols ›", _column + columns < _source.Columns.Count, () => _column += 3)));
        var scope = OfficeTheme.Text("View only: refresh still uses every source row.", 10);
        scope.TextWrapping = TextWrapping.Wrap; _body.Children.Add(scope);
    }
}
