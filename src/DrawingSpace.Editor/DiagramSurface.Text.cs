using DrawingSpace.Controls;

namespace DrawingSpace.Editor;

public sealed partial class DiagramSurface
{
    public void BeginTextEdit(string objectId)
    {
        if (Session is not { } session) return;
        FinishTextEdit(true); CancelGesture();
        var shape = session.Page.Find(objectId); var edge = session.Page.Connectors.FirstOrDefault(c => c.Id == objectId);
        if (shape is null && edge is null || shape is not null && session.Page.IsLocked(shape) || edge is not null && (session.Page.Layers.FirstOrDefault(l => l.Id == edge.LayerId)?.Locked ?? false)) return;
        session.Select(objectId);
        var bounds = shape?.WorldBounds ?? new RectD(Renderer.Routes(session.Page, session.Revision)[objectId].Midpoint.X - 75, Renderer.Routes(session.Page, session.Revision)[objectId].Midpoint.Y - 20, 150, 40);
        var origin = session.Viewport.ToScreen(new(bounds.X, bounds.Y));
        var editor = OfficeTheme.Field(shape?.Text ?? edge!.Text, "Shape text");
        editor.AcceptsReturn = true; editor.TextWrapping = TextWrapping.Wrap;
        editor.Width = Math.Max(100, bounds.Width * session.Viewport.Zoom); editor.Height = Math.Max(42, bounds.Height * session.Viewport.Zoom);
        editor.FontSize = Math.Max(10, (shape?.Style.FontSize ?? 14) * session.Viewport.Zoom); editor.Padding = new Thickness(6);
        editor.BorderBrush = OfficeTheme.Brush("#5B9BD5"); editor.BorderThickness = new Thickness(2);
        _textObjectId = objectId; _textEditor = editor; _overlay.Children.Add(editor);
        Canvas.SetLeft(editor, Math.Max(23, origin.X)); Canvas.SetTop(editor, Math.Max(23, origin.Y));
        editor.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { FinishTextEdit(false); e.Handled = true; }
            else if (e.Key == VirtualKey.Enter && !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) { FinishTextEdit(true); e.Handled = true; }
        };
        editor.LostFocus += (_, _) => { if (_textEditor == editor) FinishTextEdit(true); };
        editor.Focus(FocusState.Programmatic); editor.SelectAll();
    }
    public void FinishTextEdit(bool commit)
    {
        if (_textEditor is null) return;
        var editor = _textEditor; var id = _textObjectId; _textEditor = null; _textObjectId = null; _overlay.Children.Remove(editor);
        if (commit && Session is { } session && id is not null)
        {
            var shape = session.Page.Find(id); var edge = session.Page.Connectors.FirstOrDefault(c => c.Id == id);
            if (shape is not null && shape.Text != editor.Text || edge is not null && edge.Text != editor.Text)
                session.Execute("Edit text", () => { if (shape is not null) shape.Text = editor.Text; else if (edge is not null) edge.Text = editor.Text; });
        }
        Invalidate();
    }
}
