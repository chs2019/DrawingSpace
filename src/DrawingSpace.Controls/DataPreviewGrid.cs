using DrawingSpace.Documents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DrawingSpace.Controls;

/// <summary>Bounded, selectable source view. Selection is a source key, never a view ordinal.</summary>
public sealed class DataPreviewGrid : UserControl
{
    private readonly TabularDataCursor _cursor;
    private readonly DataRowLinkIndex? _links;
    private readonly TextBox _filter;
    private readonly StackPanel _body;
    private readonly List<RowPresentation> _rows = [];
    private int _renderGeneration;
    private sealed record RowPresentation(string Key, OfficeButton Header, List<Border> Cells, string Background);
    public string? SelectedKey => _cursor.SelectedKey;
    public event Action<string>? RowSelected;

    public DataPreviewGrid(CsvDataTable source) : this(new TabularDataCursor(source)) { }

    public DataPreviewGrid(TabularDataCursor cursor, DataRowLinkIndex? links = null)
    {
        _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
        if (links is not null && (links.SourceId != cursor.Source.SourceId || links.KeyColumn != cursor.Source.KeyColumn))
            throw new ArgumentException("Link index belongs to another source.", nameof(links));
        _links = links;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var panel = OfficeTheme.Column(); panel.Spacing = 4;
        var search = new Grid { ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        _filter = OfficeTheme.Field(cursor.FilterText, "Data filter"); _filter.PlaceholderText = "Filter source rows"; _filter.MaxLength = 256;
        _filter.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { ApplyFilter(); e.Handled = true; } };
        search.Children.Add(_filter);
        var apply = new OfficeButton("Filter", OfficeIcon.Search, action: ApplyFilter) { Height = 28 };
        AutomationProperties.SetName(apply, "Filter data rows"); Grid.SetColumn(apply, 1); search.Children.Add(apply);
        panel.Children.Add(search);
        var options = OfficeTheme.Row();
        var numeric = new CheckBox { Content = "Numeric sort", FontSize = 11, MinHeight = 24, IsChecked = cursor.Numeric };
        AutomationProperties.SetName(numeric, "Numeric data sort");
        numeric.Checked += (_, _) => Query(numeric: true);
        numeric.Unchecked += (_, _) => Query(numeric: false);
        options.Children.Add(numeric);
        var clear = new OfficeButton("Clear", OfficeIcon.None, action: () =>
        { _filter.Text = ""; _cursor.Query("", numeric: _cursor.Numeric); Render(); }) { Height = 25 };
        AutomationProperties.SetName(clear, "Clear data view"); options.Children.Add(clear); panel.Children.Add(options);
        _body = OfficeTheme.Column(); _body.Spacing = 4; panel.Children.Add(_body); Content = panel;
        Render();
    }

    /// <summary>Show a linked row, explicitly clearing a hiding filter. No document or refresh mutation.</summary>
    public void RevealRow(string key)
    {
        _cursor.RevealKey(key); _filter.Text = _cursor.FilterText; Render(); RowSelected?.Invoke(key);
    }

    private void ApplyFilter() { _cursor.Query(_filter.Text, _cursor.SortColumn, _cursor.Descending, _cursor.Numeric); Render(); }
    private void Query(bool numeric) { _cursor.Query(_cursor.FilterText, _cursor.SortColumn, _cursor.Descending, numeric); Render(); }
    private void SelectRow(string key)
    {
        var changed = _cursor.SelectedKey != key;
        _cursor.SelectKey(key);
        // Keep the row controls alive when only selection changes: this also keeps
        // keyboard focus out of the drawing canvas and avoids rebuilding cell text.
        UpdateSelection(); FocusSelectedRow(FocusState.Pointer);
        if (changed) RowSelected?.Invoke(key);
    }

    private void MoveSelectedRow(int direction)
    {
        var count = _cursor.View.RowOrdinals.Count;
        if (count == 0) return;
        var row = _cursor.FirstRow;
        for (var i = 0; i < count; i++)
            if (_cursor.View[i][_cursor.Source.KeyColumn] == _cursor.SelectedKey)
            { row = Math.Clamp(i + direction, 0, count - 1); break; }
        var key = _cursor.View[row][_cursor.Source.KeyColumn];
        var changed = key != _cursor.SelectedKey; var first = _cursor.FirstRow;
        _cursor.RevealKey(key);
        if (first != _cursor.FirstRow) Render(); else UpdateSelection();
        FocusSelectedRow(FocusState.Keyboard);
        if (changed) RowSelected?.Invoke(key);
    }

    private void UpdateSelection()
    {
        foreach (var row in _rows)
        {
            var selected = row.Key == _cursor.SelectedKey;
            if (row.Header.IsSelected == selected) continue;
            row.Header.IsSelected = selected;
            foreach (var cell in row.Cells) cell.Background = OfficeTheme.Brush(selected ? "#DEEBF7" : row.Background);
        }
    }

    private void FocusSelectedRow(FocusState focus)
    {
        var target = _rows.FirstOrDefault(row => row.Key == _cursor.SelectedKey)?.Header;
        if (target is null || target.Focus(focus)) return;
        var generation = _renderGeneration;
        void Loaded(object sender, RoutedEventArgs e)
        {
            target.Loaded -= Loaded;
            if (generation == _renderGeneration) target.Focus(focus);
        }
        target.Loaded += Loaded;
    }

    private void Render()
    {
        _renderGeneration++; _rows.Clear(); _body.Children.Clear();
        var source = _cursor.Source; var view = _cursor.View;
        var firstRow = _cursor.FirstRow; var firstColumn = _cursor.FirstColumn;
        var columns = Math.Min(TabularDataCursor.PageColumns, source.Columns.Count - firstColumn);
        var rows = Math.Min(TabularDataCursor.PageRows, view.RowOrdinals.Count - firstRow);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(38) });
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i <= rows; i++) grid.RowDefinitions.Add(new() { Height = new GridLength(25) });
        var linkHeader = OfficeTheme.Text("Link", 10); Grid.SetRow(linkHeader, 0); grid.Children.Add(linkHeader);
        for (var r = 0; r < rows; r++)
        {
            var key = view[firstRow + r][source.KeyColumn];
            var count = _links?.ShapesFor(key).Count ?? 0;
            var button = new OfficeButton(count == 0 ? (firstRow + r + 1).ToString() : count.ToString(),
                count == 0 ? OfficeIcon.None : OfficeIcon.Data, action: () => SelectRow(key))
                { Height = 25, IsSelected = key == _cursor.SelectedKey, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, "Select data row " + key);
            ToolTipService.SetToolTip(button, $"{key}: {count} linked shapes on this page");
            button.KeyDown += (_, e) =>
            {
                if (e.Key is VirtualKey.Up or VirtualKey.Down)
                { MoveSelectedRow(e.Key == VirtualKey.Up ? -1 : 1); e.Handled = true; }
            };
            _rows.Add(new(key, button, [], r % 2 == 1 ? "#F8FAFC" : "#FFFFFF"));
            Grid.SetRow(button, r + 1); grid.Children.Add(button);
        }
        void Cell(int row, int column, string value)
        {
            var length = Math.Min(120, value.Length);
            if (length < value.Length && length > 0 && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
            var display = length < value.Length ? value[..length] + "…" : value;
            var text = OfficeTheme.Text(display, 11); text.TextTrimming = TextTrimming.CharacterEllipsis; text.Margin = new Thickness(5, 1, 3, 1);
            AutomationProperties.SetName(text, $"Data cell {row} {source.Columns[firstColumn + column]}: {display}");
            var presentation = _rows[row - 1]; var key = presentation.Key;
            var border = new Border
            {
                Child = text, BorderBrush = OfficeTheme.Brush("#D6DFEA"), BorderThickness = new Thickness(0, 0, 1, 1),
                Background = OfficeTheme.Brush(key == _cursor.SelectedKey ? "#DEEBF7" : presentation.Background)
            };
            presentation.Cells.Add(border);
            border.Tapped += (_, e) => { SelectRow(key); e.Handled = true; };
            Grid.SetRow(border, row); Grid.SetColumn(border, column + 1); grid.Children.Add(border);
        }
        for (var c = 0; c < columns; c++)
        {
            var key = source.Columns[firstColumn + c];
            var header = new OfficeButton(key + (_cursor.SortColumn == key ? _cursor.Descending ? " ▼" : " ▲" : ""), OfficeIcon.None,
                action: () => { _cursor.Query(_cursor.FilterText, key, _cursor.SortColumn == key && !_cursor.Descending, _cursor.Numeric); Render(); })
                { Height = 25, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(header, "Sort data column " + key);
            Grid.SetColumn(header, c + 1); grid.Children.Add(header);
            for (var r = 0; r < rows; r++) Cell(r + 1, c, view[firstRow + r][key]);
        }
        _body.Children.Add(grid);
        var label = OfficeTheme.Text($"Rows {(rows == 0 ? 0 : firstRow + 1)}–{firstRow + rows} / {view.RowOrdinals.Count}  ·  Columns {firstColumn + 1}–{firstColumn + columns} / {source.Columns.Count}", 10);
        AutomationProperties.SetName(label, "Data preview range"); _body.Children.Add(label);
        OfficeButton Button(string name, string text, bool enabled, Action action)
        {
            var button = new OfficeButton(text, OfficeIcon.None, action: () => { action(); Render(); }) { IsEnabled = enabled, MinWidth = 48, Height = 25 };
            AutomationProperties.SetName(button, name); return button;
        }
        _body.Children.Add(OfficeTheme.Row(
            Button("Previous data rows", "Rows ‹", firstRow > 0, () => _cursor.MoveRows(-1)),
            Button("Next data rows", "Rows ›", firstRow + rows < view.RowOrdinals.Count, () => _cursor.MoveRows(1)),
            Button("Previous data columns", "Cols ‹", firstColumn > 0, () => _cursor.MoveColumns(-1)),
            Button("Next data columns", "Cols ›", firstColumn + columns < source.Columns.Count, () => _cursor.MoveColumns(1))));
        var scope = OfficeTheme.Text("View only: refresh still uses every source row.", 10);
        scope.TextWrapping = TextWrapping.Wrap; _body.Children.Add(scope);
    }
}
