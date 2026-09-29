namespace DrawingSpace.Documents;

public static partial class DocumentCodec
{
    private static void ValidateDataFeatures(Shape shape)
    {
        if (shape.DataBinding is { } link)
        {
            Identifier(link.SourceId); Identifier(link.KeyColumn); Identifier(link.RowKey);
            TextMap(link.Baseline, 4096);
            foreach (var (field, value) in link.Baseline)
            { Identifier(field); Text(value, CsvDataTable.MaximumCellCharacters); }
            if (!link.Baseline.TryGetValue(link.KeyColumn, out var key) || key != link.RowKey)
                throw new InvalidDataException("A data binding must retain its accepted source key.");
        }
        if (shape.DataGraphics is null || shape.DataGraphics.Count > 8)
            throw new InvalidDataException("At most eight data graphics are accepted per shape.");
        foreach (var rule in shape.DataGraphics)
        {
            if (rule is null || !Enum.IsDefined(rule.Kind)) throw new InvalidDataException("Invalid data graphic.");
            Identifier(rule.Field); Text(rule.Label, 256);
            if (!double.IsFinite(rule.Minimum) || !double.IsFinite(rule.Maximum) || rule.Maximum <= rule.Minimum
                || Math.Abs(rule.Minimum) > 1e12 || Math.Abs(rule.Maximum) > 1e12
                || !double.IsFinite(rule.FontSize) || rule.FontSize is < 1 or > 256
                || !rule.Bounds.IsFinite || rule.Bounds.Width is <= 0 or > 16 || rule.Bounds.Height is <= 0 or > 16
                || Math.Abs(rule.Bounds.X) > 16 || Math.Abs(rule.Bounds.Y) > 16)
                throw new InvalidDataException("Invalid data graphic range, font or bounds.");
            Color(rule.LowColor); Color(rule.MiddleColor); Color(rule.HighColor); Color(rule.TextColor);
        }
    }
}
