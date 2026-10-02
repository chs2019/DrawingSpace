using System;
using System.IO;
using System.Reflection;
using SkiaSharp;

namespace DrawingSpace.Text.Internal;

/// <summary>
/// Process-lifetime stream-backed fallback faces used when a platform font
/// manager cannot resolve a requested family. The bytes come from the same
/// Open Sans package used by the Uno application, so headless tests and
/// exports retain real glyph outlines and HarfBuzz shaping.
/// </summary>
internal static class PackagedOpenSans
{
    private static readonly Lazy<SKTypeface> Regular =
        new(() => Load("OpenSans-Regular.ttf"), true);
    private static readonly Lazy<SKTypeface> Bold =
        new(() => Load("OpenSans-Bold.ttf"), true);
    private static readonly Lazy<SKTypeface> Italic =
        new(() => Load("OpenSans-Italic.ttf"), true);
    private static readonly Lazy<SKTypeface> BoldItalic =
        new(() => Load("OpenSans-BoldItalic.ttf"), true);

    public static SKTypeface Get(int weight, bool italic)
        => (weight >= 600, italic) switch
        {
            (true, true) => BoldItalic.Value,
            (true, false) => Bold.Value,
            (false, true) => Italic.Value,
            _ => Regular.Value
        };

    public static bool IsShared(SKTypeface typeface)
        => ReferenceEquals(typeface, Regular.IsValueCreated ? Regular.Value : null)
        || ReferenceEquals(typeface, Bold.IsValueCreated ? Bold.Value : null)
        || ReferenceEquals(typeface, Italic.IsValueCreated ? Italic.Value : null)
        || ReferenceEquals(typeface, BoldItalic.IsValueCreated ? BoldItalic.Value : null);

    private static SKTypeface Load(string fileName)
    {
        var resource = "DrawingSpace.Text.Internal.Fonts." + fileName;
        using var stream = typeof(PackagedOpenSans).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Missing embedded fallback font: " + resource);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        using var data = SKData.CreateCopy(memory.ToArray());
        var typeface = SKTypeface.FromData(data)
            ?? throw new InvalidOperationException("Skia could not load embedded fallback font: " + fileName);
        if (typeface.GlyphCount == 0)
        {
            typeface.Dispose();
            throw new InvalidOperationException("Embedded fallback font has no glyphs: " + fileName);
        }
        return typeface;
    }
}
