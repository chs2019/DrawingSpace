using DrawingSpace.Stencils;

namespace DrawingSpace.Controls;

public sealed class StencilPane : UserControl
{
    private readonly StackPanel _content = new();
    private readonly TextBox _search;
    private readonly HashSet<string> _expanded = ["flowchart"];
    public event Action<StencilMaster, PointerRoutedEventArgs>? DragRequested;
    public event Action<StencilMaster>? InsertRequested;
    public StencilPane()
    {
        var root = new Grid { Background = OfficeTheme.Brush("#FAFAFA"), RowDefinitions = { new() { Height = new GridLength(42) }, new() { Height = new GridLength(40) }, new() { Height = new GridLength(1, GridUnitType.Star) }, new() { Height = new GridLength(35) } } };
        var title = OfficeTheme.Text("Shapes", 19); title.Margin = new Thickness(13, 0, 0, 0); root.Children.Add(title);
        _search = OfficeTheme.Field("", "Search shapes"); _search.PlaceholderText = "Search shapes"; _search.Margin = new Thickness(10, 3, 10, 6); Grid.SetRow(_search, 1); root.Children.Add(_search);
        _search.TextChanged += (_, _) => Rebuild();
        var scroll = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        var hint = OfficeTheme.Text("Drag shapes onto the page", 11, OfficeTheme.Secondary); hint.Margin = new Thickness(13, 0, 0, 0); Grid.SetRow(hint, 3); root.Children.Add(hint);
        Content = new Border { Child = root, BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = OfficeTheme.Brush("#CACACA") };
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        Rebuild();
    }
    public void ShowStencil(string id) { _expanded.Add(id); _search.Text = ""; Rebuild(); }
    private void Rebuild()
    {
        _content.Children.Clear();
        var search = _search.Text.Trim();
        foreach (var stencil in StencilCatalog.All)
        {
            var masters = stencil.Masters.Where(m => search.Length == 0 || m.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (masters.Length == 0) continue;
            var open = search.Length > 0 || _expanded.Contains(stencil.Id);
            var heading = new OfficeButton(stencil.Name, open ? OfficeIcon.ChevronDown : OfficeIcon.ChevronRight) { MinHeight = 34, HorizontalAlignment = HorizontalAlignment.Stretch };
            heading.Invoked += () => { if (!_expanded.Add(stencil.Id)) _expanded.Remove(stencil.Id); Rebuild(); };
            _content.Children.Add(new Border { Child = heading, Background = OfficeTheme.Brush("#F0F0F0"), BorderBrush = OfficeTheme.Brush("#D8D8D8"), BorderThickness = new Thickness(0, 1, 0, 1) });
            if (!open) continue;
            var grid = new Grid { Padding = new Thickness(7, 6, 7, 9), ColumnDefinitions = { new(), new() } };
            for (var i = 0; i < masters.Length; i++)
            {
                if (i % 2 == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var master = masters[i];
                var label = OfficeTheme.Text(master.Name, 11); label.HorizontalAlignment = HorizontalAlignment.Center; label.TextAlignment = TextAlignment.Center; label.TextWrapping = TextWrapping.Wrap;
                var column = OfficeTheme.Column(new StencilPreview { Master = master, HorizontalAlignment = HorizontalAlignment.Center }, label); column.Spacing = 2;
                var tile = new Border { Child = column, Padding = new Thickness(2, 3, 2, 5), Background = OfficeTheme.Brush("#00FFFFFF"), MinHeight = 68, IsTabStop = true };
                AutomationProperties.SetName(tile, "Insert " + master.Name);
                tile.PointerEntered += (_, _) => tile.Background = OfficeTheme.Brush("#E5EEF9");
                tile.PointerExited += (_, _) => tile.Background = OfficeTheme.Brush("#00FFFFFF");
                tile.PointerPressed += (_, e) => { if (e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) { DragRequested?.Invoke(master, e); e.Handled = true; } };
                tile.KeyDown += (_, e) => { if (e.Key is VirtualKey.Enter or VirtualKey.Space) { InsertRequested?.Invoke(master); e.Handled = true; } };
                Grid.SetRow(tile, i / 2); Grid.SetColumn(tile, i % 2); grid.Children.Add(tile);
            }
            _content.Children.Add(grid);
        }
        if (_content.Children.Count == 0) _content.Children.Add(new TextBlock { Text = "No shapes match your search.", Margin = new Thickness(14), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
    }
}
