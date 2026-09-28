using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Editing;

/// <summary>Authoring and detachment of self-contained master graphs. Session APIs own transactions.</summary>
public static class MasterAuthoring
{
    /// <summary>
    /// Captures the selected graph, including nested groups, semantic descendants and internal
    /// connectors. Identities are isolated, external references are materialized, and top-level
    /// geometry is rebased into an unpainted master anchor. The source is never modified.
    /// </summary>
    public static DiagramMaster Capture(DiagramDocument document, DiagramPage source, IEnumerable<string> selection, string name)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selection); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 1024) throw new ArgumentException("Master names are limited to 1024 characters.", nameof(name));
        if (!document.Pages.Contains(source)) throw new ArgumentException("The page does not belong to this document.", nameof(source));
        var clipboard = DocumentCodec.Load(ClipboardService.Copy(document, source, selection));
        var copied = clipboard.Pages[0];
        if (copied.Shapes.Count == 0) throw new InvalidOperationException("Select at least one shape to create a master.");
        if (copied.Shapes.Count > 8192) throw new InvalidOperationException("A master is limited to 8192 component shapes plus its anchor.");
        var bounds = copied.Shapes.Select(s => s.WorldBounds).Aggregate(RectD.Union);
        var scene = RoutingScene.Capture(copied); var router = new OrthogonalRouter();
        foreach (var edge in copied.Connectors)
        {
            var route = router.RouteSnapshot(scene, edge);
            if (route.Points.Count > 0) bounds = RectD.Union(bounds, RectD.Bounds(route.Points));
        }
        var page = new DiagramPage { Width = source.Width, Height = source.Height };
        var scratch = new DiagramDocument { Pages = [page] };
        ClipboardService.Paste(scratch, page, clipboard, new(-bounds.X, -bounds.Y));
        Materialize(scratch, page, page.Shapes, freezeContext: true);
        foreach (var shape in page.Shapes)
        {
            shape.LayerId = "default"; shape.Locked = false; shape.VisioId = null; shape.VisioMasterId = null;
            shape.Threads.Clear(); shape.Comments.Clear();
        }
        foreach (var edge in page.Connectors) { edge.LayerId = "default"; edge.VisioId = null; }
        foreach (var group in page.Groups) group.VisioId = null;

        Shape root;
        if (page.Shapes.Count == 1 && page.Connectors.Count == 0)
        {
            root = page.Shapes[0]; root.GroupId = null; root.ContainerId = null; page.Groups.Clear();
        }
        else
        {
            root = new Shape
            {
                Name = name, Text = "", Kind = ShapeKind.Annotation, IsGroupAnchor = true,
                Width = Math.Max(1, bounds.Width), Height = Math.Max(1, bounds.Height), UsesVisioCoordinates = true,
                CoordinateWidth = Math.Max(1, bounds.Width) / 96, CoordinateHeight = Math.Max(1, bounds.Height) / 96,
                Style = new() { Fill = "#00000000", StrokeWidth = 0, Opacity = 0 }
            };
            var outer = new DiagramGroup { Name = name, AnchorShapeId = root.Id }; root.GroupId = outer.Id;
            foreach (var group in page.Groups.Where(g => g.ParentId is null)) group.ParentId = outer.Id;
            foreach (var shape in page.Shapes.Where(s => s.GroupId is null)) shape.GroupId = outer.Id;
            foreach (var edge in page.Connectors.Where(c => c.GroupId is null)) edge.GroupId = outer.Id;
            page.Groups.Add(outer); page.Shapes.Insert(0, root);
            CoordinateRebase.Reparent(page, root, null);
            foreach (var shape in page.Shapes.Where(s => s != root && s.FormulaParentId is null))
            {
                // The new frame has the same unit scale and orientation as the page. Only
                // placement changes; keep size/angle/flip formulas, including GUARD/SETATREF.
                var preserved = shape.Cells.Where(c => c.Key.Equals("Width", StringComparison.OrdinalIgnoreCase)
                    || c.Key.Equals("Height", StringComparison.OrdinalIgnoreCase) || c.Key.Equals("Angle", StringComparison.OrdinalIgnoreCase)
                    || c.Key.Equals("FlipX", StringComparison.OrdinalIgnoreCase) || c.Key.Equals("FlipY", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                shape.UsesVisioCoordinates = true; CoordinateRebase.Reparent(page, shape, root.Id);
                foreach (var (key, cell) in preserved) shape.Cells[key] = cell;
            }
        }
        var master = new DiagramMaster { Name = name, Shape = root, Children = page.Shapes.Where(s => s != root).ToList(),
            Connectors = page.Connectors, Groups = page.Groups };
        DocumentCodec.Validate(new DiagramDocument { Masters = [master] });
        return master;
    }

    /// <summary>Freezes inherited definitions into local cells while preserving same-instance references.</summary>
    public static void Detach(DiagramDocument document, DiagramPage page, IEnumerable<Shape> instances)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(instances);
        var members = instances.ToArray();
        foreach (var shape in members)
            if (!page.Shapes.Contains(shape) || page.IsLocked(shape)) throw new InvalidOperationException("Every detached instance member must belong to the page and be unlocked.");
        Materialize(document, page, members, freezeContext: false);
    }

    private static void Materialize(DiagramDocument document, DiagramPage page, IReadOnlyCollection<Shape> members, bool freezeContext)
    {
        var scope = new ShapeSheetScope(document, page);
        var definitions = new Dictionary<string, Dictionary<string, ShapeCell>>(StringComparer.Ordinal);
        foreach (var shape in members)
        {
            var cells = new Dictionary<string, ShapeCell>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in scope.Names(shape))
            {
                if (scope.Cell(shape, name) is not { } definition) continue;
                var cell = definition.Clone(); cell.Inherited = false;
                cell.Formula = FormulaReferenceRewriter.Rewrite(cell.Formula, token =>
                {
                    var bang = token.IndexOf('!'); var sheet = token[..bang];
                    if (scope.FindSheet(shape, sheet) is { } target) return "Sheet." + target.Id + token[bang..];
                    if (!freezeContext) return null;
                    var value = scope.Resolve(shape, token);
                    if (value.IsError) throw new InvalidOperationException("Cannot materialize master reference '" + token + "'.");
                    return FormulaReferenceRewriter.Literal(value);
                });
                cells[name] = cell;
            }
            definitions.Add(shape.Id, cells);
        }
        // Resolve every definition before severing any master link; no partial materialization.
        foreach (var shape in members)
        {
            shape.Cells = definitions[shape.Id]; shape.MasterId = null; shape.MasterShapeId = null;
            shape.MasterInstanceId = null; shape.VisioMasterId = null; shape.LocalOverrides.Clear();
        }
    }

    internal static void SynchronizeCells(DiagramMaster before, DiagramMaster current)
    {
        const string pageId = "master-authoring-frame";
        DiagramDocument Frame(DiagramMaster master) => new() { Pages = [new DiagramPage
            { Id = pageId, Shapes = master.Children.Prepend(master.Shape).ToList(), Groups = master.Groups }] };
        ShapeSheetService.SynchronizeDirectEdits(Frame(before), Frame(current));
    }
}
