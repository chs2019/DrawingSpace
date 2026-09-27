using SkiaSharp;

namespace DrawingSpace.Controls;

public static class OfficeTheme
{
    public const string Accent = "#2B579A";
    public const string TextColor = "#242424";
    public const string Secondary = "#666666";
    public const string Separator = "#DADADA";
    public static FontFamily Font { get; set; } = new("ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans.ttf");
    public static SolidColorBrush Brush(string color)
    {
        var c = SKColor.Parse(color);
        return new(Windows.UI.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
    }
    public static TextBlock Text(string text, double size = 12, string color = TextColor, bool bold = false) => new()
    {
        Text = text, FontFamily = Font, FontSize = size, Foreground = Brush(color),
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
    };
    public static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static StackPanel Column(params UIElement[] children)
    {
        var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); return panel;
    }
    public static TextBox Field(string value, string name, double width = double.NaN)
    {
        var field = new TextBox
        {
            Text = value, Width = width, MinHeight = 26, FontSize = 12, FontFamily = Font,
            Padding = new Thickness(6, 3, 6, 3), BorderThickness = new Thickness(1),
            BorderBrush = Brush("#BDBDBD"), Background = Brush("#FFFFFF"), Foreground = Brush(TextColor),
            CornerRadius = new CornerRadius(0), VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(field, name); return field;
    }
    public static Border Rule(bool vertical = false) => new()
    {
        Background = Brush(Separator), Width = vertical ? 1 : double.NaN, Height = vertical ? double.NaN : 1
    };
}
