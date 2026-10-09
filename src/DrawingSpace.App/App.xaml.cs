using DrawingSpace.Controls;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Stencils;
using DrawingSpace.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DrawingSpace.App;

public partial class App : Application
{
    private Window? _window;
    private DiagramWorkbench? _workbench;
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Light; }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "epINSTROM" };
        _window.Content = new Grid { Background = OfficeTheme.Brush("#F8F8F8"), Children = { new TextBlock { Text = "DrawingSpace", Foreground = OfficeTheme.Brush(OfficeTheme.Accent), FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
        _window.Activate();
        try
        {
#if __WASM__
            IWorkspaceStorage storage = new BrowserWorkspaceStorage();
#else
            IWorkspaceStorage storage = new DesktopWorkspaceStorage();
#endif
            var document = SampleDiagrams.Schematic(); string? warning = null;
            try
            {
                var saved = await storage.ReadRecoveryAsync();
                if (!string.IsNullOrWhiteSpace(saved)) document = DocumentCodec.Load(saved);
            }
            catch (Exception ex) { warning = "The local recovery copy could not be opened: " + ex.Message; }
            var session = new EditorSession(document);
            _workbench = new DiagramWorkbench(session, storage);
            await ApplicationFonts.ConfigureAsync(_workbench.Surface.Renderer);
            _window.Content = _workbench;
            _window.Closed += (_, _) => _workbench.Dispose();
            _window.Activated += (_, e) => { if (e.WindowActivationState == Windows.UI.Core.CoreWindowActivationState.Deactivated) _workbench.Surface.ResetModifierKeys(); };
            if (warning is not null) _workbench.ShowStatus(warning, true);
#if __WASM__
            BrowserDiagnostics.Attach(session, _workbench, _window);
#endif
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _window.Content = new ScrollViewer { Content = new TextBlock { Text = "DrawingSpace could not start.\n\n" + ex.Message + "\n\nYour saved data has not been deleted. Reload to retry.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(40), FontSize = 16 } };
        }
    }
}
