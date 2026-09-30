using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public sealed partial class EditorSession
{
    public const int MaximumDataRowPlacements = 1024;

    /// <summary>
    /// Create a plain rectangular shape already linked to an exact source row.
    /// Shape, copied values, accepted baseline and selection form one undo transaction.
    /// The label defaults to the source key; labelColumn can explicitly select another field.
    /// No existing shape is relinked, copied, or overwritten.
    /// </summary>
    public Shape CreateShapeFromDataRow(CsvDataTable source, string rowKey, PointD center,
        string? labelColumn = null, string? layerId = null)
        => CreateShapesFromDataRows(source, [new(rowKey, center)], labelColumn, layerId)[0];

    /// <summary>
    /// Atomically create linked rectangles at explicit centers. Repeated row keys are allowed:
    /// one source row can drive multiple shapes. All keys, coordinates and layers are checked
    /// before the document is changed. Empty input is a revision/history no-op.
    /// </summary>
    public IReadOnlyList<Shape> CreateShapesFromDataRows(CsvDataTable source,
        IReadOnlyList<DataRowPlacement> placements, string? labelColumn = null, string? layerId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(placements);
        if (IsInteracting) throw new InvalidOperationException("Finish the gesture before creating linked shapes.");
        if (placements.Count > MaximumDataRowPlacements
            || (long)placements.Count * source.Columns.Count > CsvDataTable.MaximumCells)
            throw new ArgumentOutOfRangeException(nameof(placements), "The linked-shape creation budget was exceeded.");
        if (labelColumn is not null && !source.Columns.Contains(labelColumn))
            throw new ArgumentException("The source does not contain the label column.", nameof(labelColumn));
        if (placements.Count == 0) return Array.Empty<Shape>();

        var page = Page;
        var layer = layerId is null
            ? page.Layers.FirstOrDefault(value => value.Visible && !value.Locked)
            : page.Layers.FirstOrDefault(value => value.Id == layerId);
        if (layer is null || !layer.Visible || layer.Locked)
            throw new InvalidOperationException("Choose an existing visible, unlocked layer for the new shapes.");

        var shapes = new Shape[placements.Count];
        for (var i = 0; i < shapes.Length; i++)
        {
            var placement = placements[i];
            if (placement.RowKey is null || !source.TryGetRow(placement.RowKey, out var row))
                throw new ArgumentException("Unknown exact source row key at placement " + i + ".", nameof(placements));
            // The entire default 144 x 64 rectangle must fit the model coordinate envelope.
            if (!placement.Center.IsFinite || Math.Abs(placement.Center.X) > 999928
                || Math.Abs(placement.Center.Y) > 999968)
                throw new ArgumentOutOfRangeException(nameof(placements), "A linked-shape center is non-finite or outside the coordinate envelope.");
            var values = new Dictionary<string, string>(source.Columns.Count, StringComparer.Ordinal);
            foreach (var column in source.Columns) values.Add(column, row[column]);
            shapes[i] = new()
            {
                Kind = ShapeKind.Rectangle, Name = "Linked data", Text = row[labelColumn ?? source.KeyColumn],
                X = placement.Center.X - 72, Y = placement.Center.Y - 32, Width = 144, Height = 64,
                LayerId = layer.Id, Data = values,
                DataBinding = new()
                {
                    SourceId = source.SourceId, KeyColumn = source.KeyColumn, RowKey = placement.RowKey,
                    Baseline = new(values, StringComparer.Ordinal)
                }
            };
        }

        Execute(shapes.Length == 1 ? "Create linked shape" : "Create linked shapes", () =>
        {
            page.Shapes.AddRange(shapes);
            Selection.Clear();
            foreach (var shape in shapes) Selection.Add(shape.Id);
        });
        return Array.AsReadOnly(shapes);
    }
}
