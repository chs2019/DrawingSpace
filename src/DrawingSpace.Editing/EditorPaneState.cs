using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

/// <summary>
/// Tracks the document revision, active page and exact selection represented by a
/// task pane. Capture only after a successful build. Viewport/tool notifications
/// and saving an unchanged selection do not invalidate the pane.
/// </summary>
public sealed class EditorPaneState
{
    private DiagramDocument? _document;
    private long _revision = -1;
    private string? _page, _pane;
    private readonly HashSet<string> _selection = new();

    public bool NeedsRebuild(EditorSession session, string pane)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(pane);
        return !ReferenceEquals(_document, session.Document) || _revision != session.Revision
            || _page != session.ActivePageId || _pane != pane || !_selection.SetEquals(session.Selection);
    }

    public void Capture(EditorSession session, string pane)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(pane);
        _document = session.Document;
        _revision = session.Revision;
        _page = session.ActivePageId;
        _pane = pane;
        _selection.Clear();
        _selection.UnionWith(session.Selection);
    }

    public void Invalidate()
    {
        _document = null;
        _revision = -1;
        _page = _pane = null;
        _selection.Clear();
    }
}
