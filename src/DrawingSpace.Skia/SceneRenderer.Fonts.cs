using DrawingSpace.Documents;
using SkiaSharp;

namespace DrawingSpace.Skia;

public sealed partial class SceneRenderer
{
    /// <summary>Installs four owned, static font faces. The renderer disposes them with its font cache.</summary>
    public void SetTypefaces(SKTypeface regular, SKTypeface bold, SKTypeface italic, SKTypeface boldItalic)
    {
        ArgumentNullException.ThrowIfNull(regular);
        ArgumentNullException.ThrowIfNull(bold);
        ArgumentNullException.ThrowIfNull(italic);
        ArgumentNullException.ThrowIfNull(boldItalic);
        SetTypeface(regular);
        foreach (var typeface in _fallbackStyles.Values.Distinct()) typeface.Dispose();
        _fallbackStyles.Clear();
        _fallbackStyles[(true, false)] = bold;
        _fallbackStyles[(false, true)] = italic;
        _fallbackStyles[(true, true)] = boldItalic;
    }
    private SKTypeface ResolveTypeface(ShapeStyle style, SKTypeface systemTypeface)
    {
        if (_fallbackTypeface is null) return systemTypeface;
        // Browser font managers do not enumerate arbitrary installed system fonts.
        // Preserve real installed families on native hosts; otherwise use the packaged face.
        if (!string.Equals(style.FontFamily, _fallbackTypeface.FamilyName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(style.FontFamily, systemTypeface.FamilyName, StringComparison.OrdinalIgnoreCase)) return systemTypeface;
        return _fallbackStyles.GetValueOrDefault((style.Bold, style.Italic), _fallbackTypeface);
    }
}
