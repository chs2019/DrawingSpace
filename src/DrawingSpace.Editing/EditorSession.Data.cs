using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public sealed partial class EditorSession
{
    /// <summary>Refresh existing links by stable key and automatically match unlinked shapes.</summary>
    public DataRefreshPlan PreviewDataRefresh(CsvDataTable source, string matchField = "$text",
        bool selectedOnly = false, bool overwriteConflicts = false)
    {
        if (string.IsNullOrWhiteSpace(matchField) || matchField.Length > 256)
            throw new ArgumentException("Specify a matching field.", nameof(matchField));
        return PreviewDataRefreshCore(source, matchField, selectedOnly, overwriteConflicts, null, false);
    }

    /// <summary>
    /// Preview linking selected shapes to one exact source key, independently of labels.
    /// Replacing an existing link and overwriting local fields require separate consent.
    /// Apply through ApplyDataRefresh for complete preflight and transactional undo.
    /// </summary>
    public DataRefreshPlan PreviewLinkDataRow(CsvDataTable source, string rowKey,
        bool overwriteConflicts = false, bool replaceExistingLinks = false)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(rowKey);
        if (!source.TryGetRow(rowKey, out _))
            throw new ArgumentException("The source does not contain this exact row key.", nameof(rowKey));
        return PreviewDataRefreshCore(source, "$id", true, overwriteConflicts, rowKey, replaceExistingLinks);
    }

    private DataRefreshPlan PreviewDataRefreshCore(CsvDataTable source, string matchField,
        bool selectedOnly, bool overwriteConflicts, string? manualRowKey, bool replaceExistingLinks)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (IsInteracting) throw new InvalidOperationException("Finish the gesture before refreshing data.");
        var targets = new List<DataRefreshTarget>();
        var issues = new List<DataRefreshIssue>(); var issueCount = 0; var work = 0L;
        ShapeSheetScope? propertyScope = null;
        void Issue(Shape shape, string field, string reason)
        {
            issueCount++;
            if (issues.Count < 2048) issues.Add(new(shape.Id, field, reason));
        }
        foreach (var shape in Page.Shapes)
        {
            if (selectedOnly && !Selection.Contains(shape.Id)) continue;
            var previous = shape.DataBinding;
            var sameSource = previous is not null && previous.SourceId == source.SourceId
                && previous.KeyColumn == source.KeyColumn;
            if (manualRowKey is null && previous is not null && !sameSource) continue;
            if (manualRowKey is not null && previous is not null
                && (!sameSource || previous.RowKey != manualRowKey) && !replaceExistingLinks)
            {
                Issue(shape, "", "Already linked to a different row. Enable Replace existing links to relink explicitly.");
                continue;
            }
            var key = manualRowKey ?? previous?.RowKey ?? (matchField switch
            {
                "$text" => shape.Text, "$name" => shape.Name, "$id" => shape.Id,
                _ => shape.Data.GetValueOrDefault(matchField)
            });
            if (string.IsNullOrEmpty(key)) continue;
            if (!source.TryGetRow(key, out var row))
            {
                if (previous is not null) Issue(shape, "", "The linked source row is missing; shape and data retained.");
                continue;
            }
            if (Page.IsLocked(shape)) { Issue(shape, "", "The shape or its layer is locked."); continue; }
            work += shape.Data.Count + (previous?.Baseline.Count ?? 0) + source.Columns.Count;
            if (work > CsvDataTable.MaximumCells)
                throw new InvalidOperationException("Refresh exceeds 250,000 shape-field work units. Refresh a smaller selection.");
            // Never reuse another row's accepted baseline when replacing a link.
            var accepted = sameSource && previous!.RowKey == key ? previous : null;
            var values = new Dictionary<string, string>(shape.Data, StringComparer.Ordinal);
            var baseline = accepted is null ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new(accepted.Baseline, StringComparer.Ordinal);
            foreach (var column in source.Columns)
            {
                var incoming = row[column];
                if (DataCellOwned(Document, Page, shape, column, ref propertyScope))
                {
                    Issue(shape, column, "ShapeSheet owns this property; edit its cell explicitly before linking this field.");
                    continue;
                }
                var hasCurrent = shape.Data.TryGetValue(column, out var current);
                string? old = null;
                var hadBaseline = accepted is not null && accepted.Baseline.TryGetValue(column, out old);
                var equalIncoming = hasCurrent && current == incoming;
                var equalBaseline = hadBaseline && hasCurrent && current == old;
                if (overwriteConflicts || equalIncoming || equalBaseline || !hadBaseline && !hasCurrent)
                { values[column] = incoming; baseline[column] = incoming; }
                else if (!hadBaseline || incoming != old)
                    Issue(shape, column, "Local and source values differ from the accepted import baseline.");
                // If source equals baseline, retain local edits/deletions without a conflict.
            }
            if (accepted is not null)
                foreach (var column in accepted.Baseline.Keys)
                    if (!row.ContainsKey(column))
                        Issue(shape, column, "Source column removed; prior value and baseline retained.");
            baseline[source.KeyColumn] = key;
            var binding = new ShapeDataBinding
            { SourceId = source.SourceId, KeyColumn = source.KeyColumn, RowKey = key, Baseline = baseline };
            var changed = !DataMapEquals(shape.Data, values) || !DataBindingEquals(previous, binding);
            targets.Add(new(shape, shape.Name, shape.Text, new(shape.Data, StringComparer.Ordinal),
                previous?.Clone(), values, binding, changed));
        }
        return new(this, selectedOnly, targets, issues, issueCount);
    }

    public bool ApplyDataRefresh(DataRefreshPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(plan.Owner, this) || !ReferenceEquals(plan.Document, Document)
            || !ReferenceEquals(plan.Page, Page) || plan.Revision != Revision || IsInteracting
            || plan.PageShapeCount != Page.Shapes.Count
            || plan.SelectedIds is not null && !Selection.SetEquals(plan.SelectedIds))
            throw new InvalidOperationException("Refresh preview is stale; preview the source again.");
        var index = Page.Shapes.ToDictionary(s => s.Id, StringComparer.Ordinal);
        ShapeSheetScope? propertyScope = null;
        foreach (var target in plan.Targets)
        {
            if (!index.TryGetValue(target.Shape.Id, out var shape) || !ReferenceEquals(shape, target.Shape)
                || Page.IsLocked(shape) || shape.Name != target.Name || shape.Text != target.Text
                || !DataMapEquals(shape.Data, target.Expected)
                || !DataBindingEquals(shape.DataBinding, target.ExpectedBinding))
                throw new InvalidOperationException("A refresh target changed; preview the source again.");
            foreach (var (field, value) in target.Values)
                if ((!target.Expected.TryGetValue(field, out var old) || value != old)
                    && DataCellOwned(Document, Page, shape, field, ref propertyScope))
                    throw new InvalidOperationException("ShapeSheet now owns an imported field; preview the source again.");
        }
        if (plan.ChangedShapes == 0) return false;
        Execute("Refresh linked data", () =>
        {
            foreach (var target in plan.Targets)
            {
                if (!target.Changed) continue;
                target.Shape.Data = new(target.Values, StringComparer.Ordinal);
                target.Shape.DataBinding = target.Binding.Clone();
            }
        });
        return true;
    }

    public void UnlinkSelectedData()
    {
        var targets = EditableShapes.Where(s => s.DataBinding is not null).ToArray();
        if (targets.Length == 0) return;
        Execute("Unlink shape data", () => { foreach (var s in targets) s.DataBinding = null; });
    }

    /// <summary>Unlink editable shapes on the active page from this exact row; keep data and graphics.</summary>
    public int UnlinkDataRow(CsvDataTable source, string rowKey)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(rowKey);
        if (IsInteracting) throw new InvalidOperationException("Finish the gesture before unlinking data.");
        var targets = Page.Shapes.Where(s => MatchesDataRow(s, source, rowKey) && !Page.IsLocked(s)).ToArray();
        if (targets.Length != 0)
            Execute("Unlink source row", () => { foreach (var shape in targets) shape.DataBinding = null; });
        return targets.Length;
    }

    /// <summary>Select visible linked shapes without expanding groups or editing the document.</summary>
    public int SelectShapesLinkedToDataRow(CsvDataTable source, string rowKey)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(rowKey);
        if (IsInteracting) throw new InvalidOperationException("Finish the gesture before navigating linked data.");
        Selection.Clear();
        foreach (var shape in Page.Shapes)
            if (Page.IsVisible(shape.LayerId) && MatchesDataRow(shape, source, rowKey)) Selection.Add(shape.Id);
        Notify(ChangeKind.Selection);
        return Selection.Count;
    }

    private static bool MatchesDataRow(Shape shape, CsvDataTable source, string rowKey)
        => shape.DataBinding is { } link && link.SourceId == source.SourceId
            && link.KeyColumn == source.KeyColumn && link.RowKey == rowKey;

    /// <summary>Replace the selected shapes' rule of this kind; keep other graphic families.</summary>
    public void SetSelectedDataGraphic(ShapeDataGraphic rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var targets = EditableShapes.Where(s => s.DataGraphics.Count(g => g.Kind == rule.Kind) != 1
            || !s.DataGraphics.Contains(rule)).ToArray();
        if (targets.Length == 0) return;
        Execute("Change data graphic", () =>
        {
            foreach (var shape in targets)
            { shape.DataGraphics.RemoveAll(g => g.Kind == rule.Kind); shape.DataGraphics.Add(rule); }
        });
    }

    public void ClearSelectedDataGraphics()
    {
        var targets = EditableShapes.Where(s => s.DataGraphics.Count > 0).ToArray();
        if (targets.Length == 0) return;
        Execute("Remove data graphics", () => { foreach (var shape in targets) shape.DataGraphics.Clear(); });
    }

    private static bool DataCellOwned(DiagramDocument document, DiagramPage page, Shape shape,
        string field, ref ShapeSheetScope? scope)
    {
        if (shape.Cells.Count == 0 && shape.MasterId is null) return false;
        scope ??= new(document, page);
        return scope.Cell(shape, "Prop." + field) is not null
            || scope.Cell(shape, "Prop." + field + ".Value") is not null;
    }

    private static bool DataMapEquals(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (key, value) in a) if (!b.TryGetValue(key, out var other) || value != other) return false;
        return true;
    }
    private static bool DataBindingEquals(ShapeDataBinding? a, ShapeDataBinding? b)
        => ReferenceEquals(a, b) || a is not null && b is not null && a.SourceId == b.SourceId
            && a.KeyColumn == b.KeyColumn && a.RowKey == b.RowKey && DataMapEquals(a.Baseline, b.Baseline);
}
