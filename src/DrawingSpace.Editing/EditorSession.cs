using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public enum DocumentCommitKind { Edit, Undo, Redo, Remote }
public sealed record DocumentCommit(string Name, string Before, string After, DocumentCommitKind Kind);

/// <summary>UI-independent editor. Live gestures are one transaction, including dependent formulas and inherited properties.</summary>
public sealed partial class EditorSession
{
    private readonly List<HistoryEntry> _history = [];
    private int _historyIndex;
    private string? _before;
    private DiagramDocument? _semanticBefore;
    private string _transactionName = "";
    private string _pageBefore = "";
    private string[] _selectionBefore = [];
    private EditorTool _tool;
    private string _saved;
    private string? _currentSnapshot;

    public DiagramDocument Document { get; private set; }
    public string ActivePageId { get; private set; }
    public DiagramPage Page => Document.Pages.First(p => p.Id == ActivePageId);
    public HashSet<string> Selection { get; } = [];
    public Viewport Viewport { get; } = new();
    public bool GridVisible { get; set; } = true;
    public bool RulersVisible { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public bool DynamicGuides { get; set; } = true;
    public bool AutoConnect { get; set; } = true;
    public double GridSize { get; set; } = 8;
    public long Revision { get; private set; }
    public bool IsInteracting => _before is not null;
    public bool CanUndo => _historyIndex > 0 && !IsInteracting;
    public bool CanRedo => _historyIndex < _history.Count && !IsInteracting;
    public string UndoName => CanUndo ? _history[_historyIndex - 1].Name : "";
    public string RedoName => CanRedo ? _history[_historyIndex].Name : "";
    /// <summary>Cached per notified revision. Use transactions or Notify after directly changing the mutable model.</summary>
    public bool IsDirty => (_currentSnapshot ??= DocumentCodec.Save(Document)) != _saved;
    public IReadOnlyList<Shape> SelectedShapes => Page.Shapes.Where(s => Selection.Contains(s.Id)).ToArray();
    public IReadOnlyList<Shape> EditableShapes => SelectedShapes.Where(s => !Page.IsLocked(s)).ToArray();
    public IReadOnlyList<Connector> SelectedConnectors => Page.Connectors.Where(c => Selection.Contains(c.Id)).ToArray();
    public IReadOnlyList<FormulaDiagnostic> FormulaDiagnostics { get; private set; } = [];

    /// <summary>Optional selective undo adapter for a replicated document. It receives the original local transaction, not the current document.</summary>
    public Func<DocumentCommit, bool>? HistoryReplayer { get; set; }
    public EditorTool Tool
    {
        get => _tool;
        set { if (_tool == value) return; if (IsInteracting) Cancel(); _tool = value; Notify(ChangeKind.Tool); }
    }
    public event Action<ChangeKind>? Changed;
    public event Action<DocumentCommit>? Committed;

    public EditorSession(DiagramDocument? document = null)
    {
        Document = document ?? new();
        DocumentCodec.Validate(Document);
        ActivePageId = Document.Pages[0].Id;
        _saved = _currentSnapshot = DocumentCodec.Save(Document);
    }

    public void Notify(ChangeKind kind)
    {
        if (kind is ChangeKind.Document or ChangeKind.Preview) { Revision++; _currentSnapshot = null; }
        Changed?.Invoke(kind);
    }

    private void PublishDocument(string snapshot)
    {
        Revision++; _currentSnapshot = snapshot; Changed?.Invoke(ChangeKind.Document);
    }

    public void Select(string? id, bool additive = false, bool subselect = false)
    {
        if (!additive) Selection.Clear();
        if (id is not null)
        {
            var shape = Page.Find(id);
            var group = subselect ? null : shape?.GroupId;
            var ids = group is not null
                ? Page.Shapes.Where(s => Page.IsInGroup(s.GroupId, Page.RootGroup(group)) && Page.IsVisible(s.LayerId)).Select(s => s.Id)
                    .Concat(Page.Connectors.Where(c => c.GroupId is not null && Page.IsInGroup(c.GroupId, Page.RootGroup(group)) && Page.IsVisible(c.LayerId)).Select(c => c.Id)).ToArray()
                : new[] { id };
            var remove = additive && ids.All(Selection.Contains);
            foreach (var selected in ids) { if (remove) Selection.Remove(selected); else Selection.Add(selected); }
        }
        Notify(ChangeKind.Selection);
    }

    public void SelectAll()
    {
        Selection.Clear();
        foreach (var shape in Page.Shapes.Where(s => Page.IsVisible(s.LayerId))) Selection.Add(shape.Id);
        foreach (var edge in Page.Connectors.Where(c => Page.IsVisible(c.LayerId))) Selection.Add(edge.Id);
        Notify(ChangeKind.Selection);
    }

    public void Begin(string name)
    {
        if (IsInteracting) throw new InvalidOperationException("A diagram transaction is already active.");
        _before = DocumentCodec.Save(Document); _transactionName = name;
        _pageBefore = ActivePageId; _selectionBefore = [.. Selection];
        _semanticBefore = HasFormulasOrMasters() ? CaptureSemanticState(Document) : null;
    }

    public void Preview()
    {
        if (IsInteracting) PrepareSemantics();
        Notify(ChangeKind.Preview);
    }

    public void Commit()
    {
        if (_before is null) return;
        string after;
        try
        {
            DocumentCodec.Validate(Document);
            PrepareSemantics();
            DocumentCodec.Validate(Document);
            after = DocumentCodec.Save(Document);
        }
        catch { Cancel(); throw; }
        var before = _before; var name = _transactionName;
        if (before != after)
        {
            if (_historyIndex < _history.Count) _history.RemoveRange(_historyIndex, _history.Count - _historyIndex);
            _history.Add(new(name, before, after, _pageBefore, ActivePageId, _selectionBefore, [.. Selection]));
            while (_history.Count > 1 && (_history.Count > 200 || _history.Sum(h => h.EstimatedBytes) > 32 * 1024 * 1024)) _history.RemoveAt(0);
            _historyIndex = _history.Count;
        }
        _before = null; _semanticBefore = null;
        PublishDocument(after);
        if (before != after) Committed?.Invoke(new(name, before, after, DocumentCommitKind.Edit));
    }

    private bool HasFormulasOrMasters() => Document.Masters.Count > 0 || Document.Pages.Any(p => p.Shapes.Any(s => s.Cells.Count > 0));
    private void PrepareSemantics()
    {
        if (!HasFormulasOrMasters()) { FormulaDiagnostics = []; return; }
        if (_semanticBefore is not null)
        {
            MasterService.CaptureLocalOverrides(_semanticBefore, Document);
            ShapeSheetService.SynchronizeDirectEdits(_semanticBefore, Document);
        }
        MasterService.Refresh(Document);
        FormulaDiagnostics = ShapeSheetService.Recalculate(Document);
        _semanticBefore = CaptureSemanticState(Document);
    }

    // Snapshots deliberately omit images, imported package bytes, routes, geometry and comments.
    // Those can be large and are irrelevant to detecting direct property writes during a gesture.
    private static DiagramDocument CaptureSemanticState(DiagramDocument source)
    {
        static Shape Copy(Shape shape) => new()
        {
            Id = shape.Id, Name = shape.Name, Text = shape.Text, Kind = shape.Kind, X = shape.X, Y = shape.Y,
            Width = shape.Width, Height = shape.Height, Rotation = shape.Rotation, ShearX = shape.ShearX, FlipX = shape.FlipX, FlipY = shape.FlipY,
            MasterId = shape.MasterId, MasterShapeId = shape.MasterShapeId, MasterInstanceId = shape.MasterInstanceId, ContainerId = shape.ContainerId,
            UsesVisioCoordinates = shape.UsesVisioCoordinates, FormulaParentId = shape.FormulaParentId, CoordinateWidth = shape.CoordinateWidth, CoordinateHeight = shape.CoordinateHeight, IsGroupAnchor = shape.IsGroupAnchor,
            Style = shape.Style.Clone(), Data = new(shape.Data), VisioId = shape.VisioId, LocalOverrides = [.. shape.LocalOverrides],
            TextBounds = shape.TextBounds, TextRotation = shape.TextRotation,
            TextSpans = shape.MasterId is null ? [] : shape.TextSpans.Select(s => s.Clone()).ToList(),
            Paragraphs = shape.MasterId is null ? [] : shape.Paragraphs.Select(p => p.Clone()).ToList(),
            Cells = shape.Cells.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase)
        };
        return new()
        {
            Id = source.Id,
            Pages = source.Pages.Select(p => new DiagramPage { Id = p.Id, Width = p.Width, Height = p.Height, Shapes = p.Shapes.Select(Copy).ToList(), Cells = p.Cells.ToDictionary(c => c.Key, c => c.Value.Clone(), StringComparer.OrdinalIgnoreCase) }).ToList(),
            Masters = source.Masters.Select(m => new DiagramMaster { Id = m.Id, Shape = Copy(m.Shape), Children = m.Children.Select(Copy).ToList() }).ToList(),
            Cells = source.Cells.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase)
        };
    }

    public void Recalculate()
    {
        Execute("Recalculate ShapeSheet", () => { MasterService.Refresh(Document); FormulaDiagnostics = ShapeSheetService.Recalculate(Document); });
    }

    public void Cancel()
    {
        if (_before is null) return;
        var json = _before; _before = null; _semanticBefore = null;
        Restore(json, _pageBefore, _selectionBefore);
    }

    public void Execute(string name, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Begin(name);
        try { action(); Commit(); }
        catch { Cancel(); throw; }
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var entry = _history[_historyIndex - 1];
        var transaction = new DocumentCommit(entry.Name, entry.Before, entry.After, DocumentCommitKind.Undo);
        if (HistoryReplayer is { } replay)
        { if (replay(transaction)) { _historyIndex--; Notify(ChangeKind.Selection); } return; }
        var before = DocumentCodec.Save(Document);
        _historyIndex--; Restore(entry.Before, entry.PageBefore, entry.SelectionBefore);
        Committed?.Invoke(new(entry.Name, before, entry.Before, DocumentCommitKind.Undo));
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var entry = _history[_historyIndex];
        var transaction = new DocumentCommit(entry.Name, entry.Before, entry.After, DocumentCommitKind.Redo);
        if (HistoryReplayer is { } replay)
        { if (replay(transaction)) { _historyIndex++; Notify(ChangeKind.Selection); } return; }
        var before = DocumentCodec.Save(Document);
        _historyIndex++; Restore(entry.After, entry.PageAfter, entry.SelectionAfter);
        Committed?.Invoke(new(entry.Name, before, entry.After, DocumentCommitKind.Redo));
    }

    private void Restore(string json, string pageId, IEnumerable<string> selection)
    {
        Document = DocumentCodec.Load(json);
        ActivePageId = Document.Pages.Any(p => p.Id == pageId) ? pageId : Document.Pages[0].Id;
        var selected = selection.ToArray(); Selection.Clear();
        var valid = Page.Shapes.Select(s => s.Id).Concat(Page.Connectors.Select(c => c.Id)).ToHashSet();
        foreach (var id in selected.Where(valid.Contains)) Selection.Add(id);
        FormulaDiagnostics = [];
        PublishDocument(json);
    }

    public void ApplyRemote(DiagramDocument document)
    {
        if (IsInteracting) throw new InvalidOperationException("Queue remote operations until the current gesture has committed or cancelled.");
        DocumentCodec.Validate(document);
        if (document.Id != Document.Id) throw new InvalidOperationException("A remote document must have the same identity as this session.");
        Restore(DocumentCodec.Save(document), ActivePageId, Selection);
    }

    public void Load(DiagramDocument document)
    {
        DocumentCodec.Validate(document);
        if (IsInteracting) Cancel();
        Document = document; ActivePageId = document.Pages.FirstOrDefault(p => !p.IsBackground)?.Id ?? document.Pages[0].Id;
        Selection.Clear(); _history.Clear(); _historyIndex = 0; FormulaDiagnostics = [];
        _saved = DocumentCodec.Save(document);
        PublishDocument(_saved);
    }

    public void MarkSaved() { _saved = _currentSnapshot = DocumentCodec.Save(Document); Notify(ChangeKind.Selection); }
    public void SwitchPage(string id)
    {
        if (!Document.Pages.Any(p => p.Id == id)) return;
        if (IsInteracting) Cancel();
        ActivePageId = id; Selection.Clear(); Notify(ChangeKind.Document);
    }
    public DiagramPage AddPage()
    {
        var page = new DiagramPage { Name = $"Page-{Document.Pages.Count + 1}" };
        Execute("Insert page", () => { Document.Pages.Add(page); ActivePageId = page.Id; Selection.Clear(); });
        return page;
    }
    public void DeletePage()
    {
        if (Document.Pages.Count == 1) return;
        Execute("Delete page", () =>
        {
            var id = ActivePageId; Document.Pages.Remove(Page);
            foreach (var page in Document.Pages.Where(p => p.BackgroundPageId == id)) page.BackgroundPageId = null;
            ActivePageId = Document.Pages[0].Id; Selection.Clear();
        });
    }
}
