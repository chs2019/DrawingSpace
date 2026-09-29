using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public sealed partial class EditorSession
{
    /// <summary>
    /// Refresh existing links by stable key; match unlinked shapes by $text, $name,
    /// $id or an existing property. Preview never writes to the source or drawing.
    /// </summary>
    public DataRefreshPlan PreviewDataRefresh(CsvDataTable source, string matchField = "$text",
        bool selectedOnly = false, bool overwriteConflicts = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(matchField) || matchField.Length > 256)
            throw new ArgumentException("Specify a matching field.", nameof(matchField));
        if (IsInteracting) throw new InvalidOperationException("Finish the gesture before refreshing data.");
        var targets = new List<DataRefreshTarget>();
        var issues = new List<DataRefreshIssue>(); var issueCount = 0; var work = 0L;
        void Issue(Shape shape, string field, string reason)
        {
            issueCount++;
            if (issues.Count < 2048) issues.Add(new(shape.Id, field, reason));
        }
        foreach (var shape in Page.Shapes)
        {
            if (selectedOnly && !Selection.Contains(shape.Id)) continue;
            var previous = shape.DataBinding;
            if (previous is not null && (previous.SourceId != source.SourceId || previous.KeyColumn != source.KeyColumn))
                continue; // Other recordsets are independent, not refresh conflicts.
            var key = previous?.RowKey ?? (matchField switch
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
            if (Page.IsLocked(shape))
            { Issue(shape, "", "The shape or its layer is locked."); continue; }
            // Charge copied local/baseline cells as well as source columns.
            work += shape.Data.Count + (previous?.Baseline.Count ?? 0) + source.Columns.Count;
            if (work > CsvDataTable.MaximumCells)
                throw new InvalidOperationException("Refresh exceeds 250,000 shape-field work units. Refresh a smaller selection.");
            var values = new Dictionary<string, string>(shape.Data, StringComparer.Ordinal);
            var baseline = previous is null ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new(previous.Baseline, StringComparer.Ordinal);
            foreach (var column in source.Columns)
            {
                var incoming = row[column];
                var hasCurrent = shape.Data.TryGetValue(column, out var current);
                string? old = null;
                var hadBaseline = previous is not null && previous.Baseline.TryGetValue(column, out old);
                var equalIncoming = hasCurrent && current == incoming;
                var equalBaseline = hadBaseline && hasCurrent && current == old;
                if (overwriteConflicts || equalIncoming || equalBaseline || !hadBaseline && !hasCurrent)
                {
                    values[column] = incoming; baseline[column] = incoming;
                }
                else if (!hadBaseline || incoming != old)
                {
                    // Both source and local state changed. Preserve the old baseline;
                    // deleting a local property is a real local edit, not an empty value.
                    Issue(shape, column, "Local and source values differ from the accepted import baseline.");
                }
                // If R == B, retain a local edit or deletion without reporting conflict.
            }
            if (previous is not null)
                foreach (var column in previous.Baseline.Keys)
                    if (!row.ContainsKey(column))
                        Issue(shape, column, "Source column removed; prior value and baseline retained.");
            // A displayed key property may be locally edited; the link identity must not follow it.
            baseline[source.KeyColumn] = key;
            var binding = new ShapeDataBinding
            {
                SourceId = source.SourceId, KeyColumn = source.KeyColumn,
                RowKey = key, Baseline = baseline
            };
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
        foreach (var target in plan.Targets)
        {
            if (!index.TryGetValue(target.Shape.Id, out var shape) || !ReferenceEquals(shape, target.Shape)
                || Page.IsLocked(shape) || shape.Name != target.Name || shape.Text != target.Text
                || !DataMapEquals(shape.Data, target.Expected)
                || !DataBindingEquals(shape.DataBinding, target.ExpectedBinding))
                throw new InvalidOperationException("A refresh target changed; preview the source again.");
        }
        if (plan.ChangedShapes == 0) return false;
        Execute("Refresh linked data", () =>
        {
            foreach (var target in plan.Targets)
            {
                if (!target.Changed) continue;
                // Do not give the mutable document ownership of the reusable preview's state.
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
