using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace DrawingSpace.Controls;

/// <summary>A compact custom command control with pointer capture, keyboard invocation and an automation peer.</summary>
public class OfficeButton : UserControl
{
    private readonly Border _border;
    private readonly TextBlock _label;
    private readonly OfficeIconView _icon;
    private bool _hover, _pressed, _selected, _dark;
    public event Action? Invoked;
    public bool IsSelected { get => _selected; set { _selected = value; UpdateVisual(); } }
    public bool Dark { get => _dark; set { _dark = value; UpdateVisual(); } }
    public string Label { get => _label.Text; set { _label.Text = value; AutomationProperties.SetName(this, value); } }
    public OfficeButton(string label = "", OfficeIcon icon = OfficeIcon.None, bool large = false, Action? action = null)
    {
        IsTabStop = true; MinHeight = large ? 68 : 26; MinWidth = large ? 54 : 24;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        _icon = new() { Icon = icon, Width = large ? 30 : 18, Height = large ? 30 : 18, VerticalAlignment = VerticalAlignment.Center };
        _label = OfficeTheme.Text(label, 12); _label.TextAlignment = large ? TextAlignment.Center : TextAlignment.Left;
        _label.TextWrapping = large ? TextWrapping.Wrap : TextWrapping.NoWrap;
        var panel = new StackPanel { Orientation = large ? Orientation.Vertical : Orientation.Horizontal, Spacing = large ? 5 : 6, HorizontalAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (icon != OfficeIcon.None) panel.Children.Add(_icon);
        if (label.Length > 0) panel.Children.Add(_label);
        _border = new Border { Child = panel, Padding = new Thickness(large ? 8 : 5, 3, large ? 8 : 5, 3), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1) };
        Content = _border;
        AutomationProperties.SetName(this, label);
        if (action is not null) Invoked += action;
        PointerEntered += (_, _) => { _hover = true; UpdateVisual(); };
        PointerExited += (_, _) => { _hover = false; UpdateVisual(); };
        PointerPressed += (_, e) =>
        {
            if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _pressed = true; CapturePointer(e.Pointer); Focus(FocusState.Pointer); UpdateVisual(); e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            var invoke = _pressed; var p = e.GetCurrentPoint(this).Position; _pressed = false; ReleasePointerCaptures(); UpdateVisual();
            if (invoke && p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight) Invoke(); e.Handled = true;
        };
        PointerCanceled += (_, _) => Reset(); PointerCaptureLost += (_, _) => Reset();
        KeyDown += (_, e) => { if (e.Key is VirtualKey.Enter or VirtualKey.Space) { Invoke(); e.Handled = true; } };
        GotFocus += (_, _) => UpdateVisual(); LostFocus += (_, _) => UpdateVisual();
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => UpdateVisual());
        UpdateVisual();
    }
    public void Invoke() { if (IsEnabled) Invoked?.Invoke(); }
    private void Reset() { _pressed = false; UpdateVisual(); }
    private void UpdateVisual()
    {
        if (_border is null) return;
        var active = _hover || _selected || _pressed;
        _border.Background = OfficeTheme.Brush(_dark ? active ? "#416EAA" : OfficeTheme.Accent : _pressed ? "#BDD0EA" : active ? "#E5EEF9" : "#00FFFFFF");
        _border.BorderBrush = OfficeTheme.Brush(!_dark && (_selected || FocusState == FocusState.Keyboard) ? "#8CAED7" : "#00FFFFFF");
        _label.Foreground = OfficeTheme.Brush(_dark ? "#FFFFFF" : OfficeTheme.TextColor); _icon.Color = _dark ? "#FFFFFF" : "#3A3A3A";
        Opacity = IsEnabled ? 1 : .38;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new OfficeButtonPeer(this);
    private sealed class OfficeButtonPeer(OfficeButton owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override string GetClassNameCore() => nameof(OfficeButton);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override object? GetPatternCore(PatternInterface patternInterface) => patternInterface == PatternInterface.Invoke ? this : base.GetPatternCore(patternInterface);
        public void Invoke() => owner.Invoke();
    }
}
