namespace DrawingSpace.Core;

public sealed class Viewport
{
    public const double MinimumZoom = 0.1;
    public const double MaximumZoom = 8;
    private double _zoom = 1;
    private PointD _pan = new(48, 48);
    public double Zoom
    {
        get => _zoom;
        set => _zoom = double.IsFinite(value) ? Math.Clamp(value, MinimumZoom, MaximumZoom) : 1;
    }
    public PointD Pan
    {
        get => _pan;
        set => _pan = value.IsFinite ? value : PointD.Zero;
    }
    public PointD ToWorld(PointD screen) => (screen - Pan) / Zoom;
    public PointD ToScreen(PointD world) => world * Zoom + Pan;
    public void ZoomAt(double zoom, PointD anchor)
    {
        var world = ToWorld(anchor);
        Zoom = zoom;
        Pan = anchor - world * Zoom;
    }
    public void Fit(RectD bounds, double width, double height, double margin = 40)
    {
        if (!bounds.IsFinite || width <= margin * 2 || height <= margin * 2) return;
        Zoom = Math.Min((width - 2 * margin) / Math.Max(1, bounds.Width), (height - 2 * margin) / Math.Max(1, bounds.Height));
        Pan = new PointD(width / 2, height / 2) - bounds.Center * Zoom;
    }
}
