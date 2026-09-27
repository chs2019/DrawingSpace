using System.Globalization;
using SkiaSharp;

namespace DrawingSpace.Skia;

public static class TextLayout
{
    public static IReadOnlyList<string> Wrap(string text, SKFont font, float width, int maximumLines)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || width <= 0 || maximumLines <= 0) return lines;
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var current = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (font.MeasureText(candidate) <= width) { current = candidate; continue; }
                if (current.Length > 0) { lines.Add(current); current = ""; }
                if (font.MeasureText(word) <= width) { current = word; continue; }
                var elements = StringInfo.GetTextElementEnumerator(word);
                while (elements.MoveNext())
                {
                    var element = elements.GetTextElement();
                    if (current.Length > 0 && font.MeasureText(current + element) > width) { lines.Add(current); current = ""; }
                    current += element;
                }
            }
            lines.Add(current);
        }
        if (lines.Count <= maximumLines) return lines;
        lines = lines.Take(maximumLines).ToList();
        var last = lines[^1];
        while (last.Length > 0 && font.MeasureText(last + "…") > width)
        {
            var indices = StringInfo.ParseCombiningCharacters(last);
            last = last[..indices[^1]];
        }
        lines[^1] = last + "…";
        return lines;
    }
}
