using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

/// <summary>UI-independent editor. Live gestures are one transaction and never create per-frame undo entries.</summary>
public sealed partial class EditorSession
{
    private readonly List<HistoryEntry> _history = [];
    private int _historyIndex;
    private string? _before;
    private string _transactionName = "";
    private string _pageBefore = "";
    private string[] _selectionBefore = [];
    private EditorTool _tool;
    private string _saved;
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
    public bool IsDirty => DocumentCodec.Save(Document) != _saved;
    public IReadOnlyList<Shape> SelectedShapes => Page.Shapes.Where(s => Selection.Contains(s.Id)).ToArray();
    public IReadOnlyList<Shape> EditableShapes => SelectedShapes.Where(s => !Page.IsLocked(s)).ToArray();
    public IReadOnlyList<Connector> SelectedConnectors => Page.Connectors.Where(c => Selection.Contains(c.Id)).ToArray();
    public EditorTool Tool
    {
        get => _tool;
        set { if (_tool == value) return; if (IsInteracting) Cancel(); _tool = value; Notify(ChangeKind.Tool); }
    }
    public event Action<ChangeKind>? Changed;

    public EditorSession(DiagramDocument? document = null)
    {
        Document = document ?? new();
        DocumentCodec.Validate(Document);
        ActivePageId = Document.Pages[0].Id;
        _saved = DocumentCodec.Save(Document);
    }
    public void Notify(ChangeKind kind)
    {
        if (kind is ChangeKind.Document or ChangeKind.Preview) Revision++;
        Changed?.Invoke(kind);
    }
    public void Select(string? id, bool additive = false)
    {
        if (!additive) Selection.Clear();
        if (id is not null)
        {
            var shape = Page.Find(id);
            var ids = shape?.GroupId is { } group ? Page.Shapes.Where(s => s.GroupId == group && Page.IsVisible(s.LayerId)).Select(s => s.Id).ToArray() : new[] { id };
            var remove = additive && ids.All(Selection.Contains);
            foreach (var selected in ids) { if (remove) Selection.Remove(selected); else Selection.Add(selected); }
        }
        Notify(ChangeKind.Selection);
    }
    public void SelectAll()
    {
        Selection.Clear();
        foreach (var s in Page.Shapes.Where(s => Page.IsVisible(s.LayerId))) Selection.Add(s.Id);
        foreach (var c in Page.Connectors.Where(c => Page.IsVisible(c.LayerId))) Selection.Add(c.Id);
        Notify(ChangeKind.Selection);
    }
    public void Begin(string name)
    {
        if (IsInteracting) throw new InvalidOperationException("A diagram transaction is already active.");
        _before = DocumentCodec.Save(Document); _transactionName = name;
        _pageBefore = ActivePageId; _selectionBefore = [.. Selection];
    }
    public void Preview() => Notify(ChangeKind.Preview);
    public void Commit()
    {
        if (_before is null) return;
        try { DocumentCodec.Validate(Document); }
        catch { Cancel(); throw; }
        var after = DocumentCodec.Save(Document);
        if (_before != after)
        {
            if (_historyIndex < _history.Count) _history.RemoveRange(_historyIndex, _history.Count - _historyIndex);
            _history.Add(new(_transactionName, _before, after, _pageBefore, ActivePageId, _selectionBefore, [.. Selection]));
            while (_history.Count > 1 && (_history.Count > 200 || _history.Sum(h => h.EstimatedBytes) > 32 * 1024 * 1024)) _history.RemoveAt(0);
            _historyIndex = _history.Count;
        }
        _before = null;
        Notify(ChangeKind.Document);
    }
    public void Cancel()
    {
        if (_before is null) return;
        var json = _before; _before = null;
        Restore(json, _pageBefore, _selectionBefore);
    }
    public void Execute(string name, Action action)
    {
        Begin(name);
        try { action(); Commit(); }
        catch { Cancel(); throw; }
    }
    public void Undo()
    {
        if (!CanUndo) return;
        var entry = _history[--_historyIndex];
        Restore(entry.Before, entry.PageBefore, entry.SelectionBefore);
    }
    public void Redo()
    {
        if (!CanRedo) return;
        var entry = _history[_historyIndex++];
        Restore(entry.After, entry.PageAfter, entry.SelectionAfter);
    }
    private void Restore(string json, string pageId, IEnumerable<string> selection)
    {
        Document = DocumentCodec.Load(json);
        ActivePageId = Document.Pages.Any(p => p.Id == pageId) ? pageId : Document.Pages[0].Id;
        Selection.Clear();
        var valid = Page.Shapes.Select(s => s.Id).Concat(Page.Connectors.Select(c => c.Id)).ToHashSet();
        foreach (var id in selection.Where(valid.Contains)) Selection.Add(id);
        Notify(ChangeKind.Document);
    }
    public void Load(DiagramDocument document)
    {
        DocumentCodec.Validate(document);
        if (IsInteracting) Cancel();
        Document = document; ActivePageId = document.Pages[0].Id;
        Selection.Clear(); _history.Clear(); _historyIndex = 0;
        _saved = DocumentCodec.Save(document);
        Notify(ChangeKind.Document);
    }
    public void MarkSaved() { _saved = DocumentCodec.Save(Document); Notify(ChangeKind.Selection); }
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
        Execute("Delete page", () => { Document.Pages.Remove(Page); ActivePageId = Document.Pages[0].Id; Selection.Clear(); });
    }
}
