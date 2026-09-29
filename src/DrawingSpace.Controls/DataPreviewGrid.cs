using DrawingSpace.Documents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DrawingSpace.Controls;

/// <summary>Bounded, read-only tabular preview: five rows and three columns are realized at a time.</summary>
public sealed class DataPreviewGrid : UserControl
{
    private readonly CsvDataTable _source;
    private int _row, _column;
    public DataPreviewGrid(CsvDataTable source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Render();
    }

    private void Render()
    {
        var panel = OfficeTheme.Column();
        panel.Spacing = 4;
        var grid = new Grid();
        var columns = Math.Min(3, _source.Columns.Count - _column);
        var rows = Math.Min(5, _source.Rows.Count - _row);
        for (var i = 0; i < columns; i++)
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i <= rows; i++) grid.RowDefinitions.Add(new() { Height = new GridLength(25) });
        void Cell(int row, int column, string value, bool header)
        {
            var length = Math.Min(120, value.Length);
            if (length < value.Length && length > 0 && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
            var text = OfficeTheme.Text(length < value.Length ? value[..length] + "…" : value, 11, bold: header);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Margin = new Thickness(5, 1, 3, 1);
            var border = new Border
            {
                Child = text, BorderBrush = OfficeTheme.Brush("#D6DFEA"), BorderThickness = new Thickness(0, 0, 1, 1),
                Background = OfficeTheme.Brush(header ? "#EDF2F8" : row % 2 == 0 ? "#F8FAFC" : "#FFFFFF")
            };
            Grid.SetRow(border, row); Grid.SetColumn(border, column); grid.Children.Add(border);
        }
        for (var c = 0; c < columns; c++)
        {
            var key = _source.Columns[_column + c]; Cell(0, c, key, true);
            for (var r = 0; r < rows; r++) Cell(r + 1, c, _source.Rows[_row + r][key], false);
        }
        panel.Children.Add(grid);
        var label = OfficeTheme.Text(
            $"Rows {(rows == 0 ? 0 : _row + 1)}–{_row + rows} / {_source.Rows.Count}  ·  Columns {_column + 1}–{_column + columns} / {_source.Columns.Count}", 10);
        AutomationProperties.SetName(label, "Data preview range"); panel.Children.Add(label);
        OfficeButton Button(string name, string labelText, bool enabled, Action action)
        {
            var button = new OfficeButton(labelText, OfficeIcon.None, action: () => { action(); Render(); })
                { IsEnabled = enabled, MinWidth = 48, Height = 25 };
            AutomationProperties.SetName(button, name); return button;
        }
        panel.Children.Add(OfficeTheme.Row(
            Button("Previous data rows", "Rows ‹", _row > 0, () => _row = Math.Max(0, _row - 5)),
            Button("Next data rows", "Rows ›", _row + rows < _source.Rows.Count, () => _row += 5),
            Button("Previous data columns", "Cols ‹", _column > 0, () => _column = Math.Max(0, _column - 3)),
            Button("Next data columns", "Cols ›", _column + columns < _source.Columns.Count, () => _column += 3)));
        Content = panel;
    }
}
