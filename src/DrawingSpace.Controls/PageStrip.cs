using DrawingSpace.Documents;

namespace DrawingSpace.Controls;

public sealed class PageStrip : UserControl
{
    private readonly StackPanel _pages = new() { Orientation = Orientation.Horizontal };
    public event Action<string>? PageSelected;
    public event Action<string>? RenameRequested;
    public event Action? AddRequested;
    public PageStrip()
    {
        var root = new Grid { Background = OfficeTheme.Brush("#F5F5F5"), ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) } } };
        var marker = OfficeTheme.Text("◀  ▶", 10, "#888888"); marker.Margin = new Thickness(10, 0, 10, 0); root.Children.Add(marker);
        var scroll = new ScrollViewer { Content = _pages, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled };
        Grid.SetColumn(scroll, 1); root.Children.Add(scroll);
        Content = new Border { Child = root, BorderBrush = OfficeTheme.Brush("#C9C9C9"), BorderThickness = new Thickness(0, 1, 0, 0) };
        Height = 31; HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }
    public void Update(IEnumerable<DiagramPage> pages, string active)
    {
        _pages.Children.Clear();
        foreach (var page in pages)
        {
            var id = page.Id; var tab = new OfficeButton(page.Name, action: () => PageSelected?.Invoke(id)) { IsSelected = id == active, MinWidth = 86, Height = 30 };
            tab.DoubleTapped += (_, e) => { RenameRequested?.Invoke(id); e.Handled = true; };
            ToolTipService.SetToolTip(tab, "Double-click to rename page"); _pages.Children.Add(tab);
        }
        var add = new OfficeButton("", OfficeIcon.Add, action: () => AddRequested?.Invoke()) { Width = 34, Height = 30 };
        AutomationProperties.SetName(add, "Insert page"); _pages.Children.Add(add);
    }
}
