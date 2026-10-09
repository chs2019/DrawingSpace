using DrawingSpace.Skia;
using SkiaSharp;
using Windows.Storage;

namespace DrawingSpace.App;

internal static class ApplicationFonts
{
    public static async Task ConfigureAsync(SceneRenderer renderer)
    {
        var loaded = new List<SKTypeface>();
        try
        {
            foreach (var variant in new[] { "Regular", "Bold", "Italic", "BoldItalic" })
            {
                var uri = new Uri($"ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-{variant}.ttf");
                var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                await using var stream = await file.OpenStreamForReadAsync();
                using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes);
                using var data = SKData.CreateCopy(bytes.ToArray());
                loaded.Add(SKTypeface.FromData(data) ?? throw new InvalidDataException($"Cannot load the packaged {variant} font."));
            }
            renderer.SetTypefaces(loaded[0], loaded[1], loaded[2], loaded[3]);
            loaded.Clear(); // Ownership transferred to the renderer.
        }
        finally { foreach (var face in loaded) face.Dispose(); }
    }
}
