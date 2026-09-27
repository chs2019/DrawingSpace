namespace DrawingSpace.Controls;

public sealed class OfficeRibbon : UserControl
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, (OfficeButton Button, Func<IEnumerable<RibbonGroup>> Build)> _definitions = [];
    public string SelectedTab { get; private set; } = "Home";
    public event Action<string>? TabChanged;
    public OfficeRibbon()
    {
        var root = new Grid { Background = OfficeTheme.Brush("#FFFFFF"), RowDefinitions = { new() { Height = new GridLength(32) }, new() { Height = new GridLength(101) } } };
        var tabs = new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled };
        var groups = new ScrollViewer { Content = _groups, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled };
        root.Children.Add(tabs); Grid.SetRow(groups, 1); root.Children.Add(groups);
        Content = new Border { Child = root, BorderBrush = OfficeTheme.Brush(OfficeTheme.Separator), BorderThickness = new Thickness(0, 0, 0, 1) };
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }
    public void AddTab(string name, Func<IEnumerable<RibbonGroup>> build)
    {
        var button = new OfficeButton(name) { MinWidth = 61, Height = 31 };
        button.Invoked += () => Select(name);
        _tabs.Children.Add(button); _definitions.Add(name, (button, build));
    }
    public void Select(string name)
    {
        if (!_definitions.TryGetValue(name, out var definition)) return;
        SelectedTab = name;
        foreach (var entry in _definitions) entry.Value.Button.IsSelected = entry.Key == name;
        _groups.Children.Clear(); foreach (var group in definition.Build()) _groups.Children.Add(group);
        TabChanged?.Invoke(name);
    }
}
