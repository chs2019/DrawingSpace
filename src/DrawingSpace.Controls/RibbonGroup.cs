namespace DrawingSpace.Controls;

public sealed class RibbonGroup : UserControl
{
    public StackPanel Items { get; } = new() { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Top };
    public RibbonGroup(string name, params UIElement[] items)
    {
        var grid = new Grid { Padding = new Thickness(7, 5, 7, 0), RowDefinitions = { new() { Height = new GridLength(1, GridUnitType.Star) }, new() { Height = new GridLength(21) } } };
        foreach (var item in items) Items.Children.Add(item);
        grid.Children.Add(Items);
        var caption = OfficeTheme.Text(name, 11, OfficeTheme.Secondary); caption.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(caption, 1); grid.Children.Add(caption);
        Content = new Border { BorderBrush = OfficeTheme.Brush(OfficeTheme.Separator), BorderThickness = new Thickness(0, 0, 1, 0), Child = grid };
        Height = 99; VerticalContentAlignment = VerticalAlignment.Stretch;
    }
}
