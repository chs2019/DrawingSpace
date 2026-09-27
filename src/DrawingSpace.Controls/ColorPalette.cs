namespace DrawingSpace.Controls;

public sealed class ColorPalette : UserControl
{
    public static string[] Colors { get; } = ["#FFFFFF", "#242424", "#4672C4", "#2B579A", "#70AD47", "#FFC000", "#ED7D31", "#C0504D", "#8064A2", "#5B9BD5", "#E8F0FA", "#D9E2F3", "#E2F0D9", "#FFF2CC", "#FCE4D6", "#F4CCCC", "#E4DFEC", "#DDEBF7", "#F2F2F2", "#808080"];
    public event Action<string>? ColorSelected;
    public ColorPalette()
    {
        var grid = new Grid(); for (var i = 0; i < 10; i++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(22) });
        grid.RowDefinitions.Add(new() { Height = new GridLength(24) }); grid.RowDefinitions.Add(new() { Height = new GridLength(24) });
        for (var i = 0; i < Colors.Length; i++)
        {
            var color = Colors[i]; var cell = new OfficeButton("", OfficeIcon.None, action: () => ColorSelected?.Invoke(color)) { Width = 21, Height = 22, MinWidth = 0, MinHeight = 0 };
            cell.Content = new Border { Background = OfficeTheme.Brush(color), BorderBrush = OfficeTheme.Brush("#B5B5B5"), BorderThickness = new Thickness(1), Margin = new Thickness(2) };
            AutomationProperties.SetName(cell, "Color " + color); ToolTipService.SetToolTip(cell, color);
            Grid.SetColumn(cell, i % 10); Grid.SetRow(cell, i / 10); grid.Children.Add(cell);
        }
        Content = grid;
    }
}
