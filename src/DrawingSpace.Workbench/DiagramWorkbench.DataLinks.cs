namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private TabularDataCursor? _dataCursor;
    private bool _replaceDataLinks;

    private void BuildSourceBrowser()
    {
        var table = _dataTable!;
        if (!ReferenceEquals(_dataCursor?.Source, table)) _dataCursor = new(table);
        var cursor = _dataCursor;
        var grid = new DataPreviewGrid(cursor, new DataRowLinkIndex(Session.Page, table));
        _properties.Children.Add(grid);
        var selected = OfficeTheme.Text(cursor.SelectedKey is { } key ? "Row: " + key : "Select a source row", 11);
        selected.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetName(selected, "Selected source row");
        grid.RowSelected += row => { selected.Text = "Row: " + row; _dataPlan = null; };
        var generation = _dataPaneGeneration;
        void CheckSource()
        {
            if (generation != _dataPaneGeneration || !ReferenceEquals(_dataTable, table))
                throw new InvalidOperationException("The source or pane changed. Open row actions again.");
        }
        string RowKey()
        {
            CheckSource();
            return cursor.SelectedKey ?? throw new InvalidOperationException("Select a source row first.");
        }
        OfficeButton? actions = null;
        void OpenActions()
        {
            CheckSource();
            var menu = new Flyout();
            var content = OfficeTheme.Column(); content.MinWidth = 245;
            var replace = new CheckBox { Content = "Replace existing links", IsChecked = _replaceDataLinks, MinHeight = 24, FontSize = 12 };
            AutomationProperties.SetName(replace, "Replace existing links");
            void CommitReplace()
            {
                if (generation != _dataPaneGeneration) return;
                _replaceDataLinks = replace.IsChecked == true; _dataPlan = null;
            }
            replace.Checked += (_, _) => CommitReplace(); replace.Unchecked += (_, _) => CommitReplace();
            content.Children.Add(replace);
            OfficeButton Item(string name, OfficeIcon icon, Action action)
                => DataButton(name, icon, () => { menu.Hide(); CheckSource(); action(); });
            content.Children.Add(Item("Link to Selected Shapes", OfficeIcon.Data, () =>
            {
                var row = RowKey();
                _dataPlan = Session.PreviewLinkDataRow(table, row, _dataOverwrite, _replaceDataLinks);
                _dataReport = $"Row {row}: {_dataPlan.ChangedShapes} shapes to link; {_dataPlan.TotalIssues} warnings. Choose Apply Refresh to commit.";
                ShowStatus(_dataReport); RebuildProperties();
            }));
            content.Children.Add(Item("Linked Shapes", OfficeIcon.Search, () =>
            {
                var row = RowKey(); _dataPlan = null;
                var count = Session.SelectShapesLinkedToDataRow(table, row);
                if (count > 0) Surface.Fit(true);
                ShowStatus($"Selected {count} visible shapes linked to row {row} on this page.");
            }));
            content.Children.Add(Item("Unlink Row", OfficeIcon.Data, () =>
            {
                var row = RowKey(); _dataPlan = null;
                var count = Session.UnlinkDataRow(table, row);
                ShowStatus($"Unlinked {count} editable shapes; data and graphics retained."); RebuildProperties();
            }));
            content.Children.Add(Item("Show Linked Row", OfficeIcon.Search, () =>
            {
                var shapes = Session.SelectedShapes;
                if (shapes.Count != 1 || shapes[0].DataBinding is not { } link
                    || link.SourceId != table.SourceId || link.KeyColumn != table.KeyColumn)
                    throw new InvalidOperationException("Select one shape linked to this source.");
                grid.RevealRow(link.RowKey);
                ShowStatus("Showing linked row " + link.RowKey + "; a hiding filter is cleared explicitly.");
            }));
            menu.Content = content; menu.ShowAt(actions!);
        }
        actions = DataButton("Row Actions", OfficeIcon.Data, OpenActions);
        AutomationProperties.SetName(actions, "Source Row Actions");
        var rowPanel = new Grid { ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) } } };
        rowPanel.Children.Add(actions); Grid.SetColumn(selected, 1); rowPanel.Children.Add(selected);
        _properties.Children.Add(rowPanel);
    }
}
