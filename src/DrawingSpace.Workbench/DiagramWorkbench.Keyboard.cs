namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private static bool Down(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    private void WorkbenchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox or PasswordBox || Surface.IsTextEditing) return;
        var control = Down(VirtualKey.Control) || Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows);
        var shift = Down(VirtualKey.Shift);
        if (e.Key == VirtualKey.Space) { Surface.IsSpaceDown = true; e.Handled = true; return; }
        if (control)
        {
            switch (e.Key)
            {
                case VirtualKey.S: RunAsync(SaveAsync); break;
                case VirtualKey.O: RunAsync(OpenAsync); break;
                case VirtualKey.N: RunAsync(() => NewAsync("blank")); break;
                case VirtualKey.Z: if (shift) Session.Redo(); else Session.Undo(); break;
                case VirtualKey.Y: Session.Redo(); break;
                case VirtualKey.C: RunAsync(CopyAsync); break;
                case VirtualKey.X: RunAsync(CutAsync); break;
                case VirtualKey.V: RunAsync(PasteAsync); break;
                case VirtualKey.D: Session.Duplicate(); break;
                case VirtualKey.A: Session.SelectAll(); break;
                case VirtualKey.G: if (shift) Session.Ungroup(); else Session.Group(); break;
                case VirtualKey.U when shift: Session.Ungroup(); break;
                case VirtualKey.B: Session.Format(s => s.Bold = !s.Bold); break;
                case VirtualKey.I: Session.Format(s => s.Italic = !s.Italic); break;
                case VirtualKey.F: RunAsync(FindAsync); break;
                case VirtualKey.Number1: Session.Tool = EditorTool.Pointer; break;
                case VirtualKey.Number2: Session.Tool = EditorTool.Text; break;
                case VirtualKey.Number3: Session.Tool = EditorTool.Connector; break;
                default: return;
            }
            e.Handled = true; return;
        }
        var distance = shift ? 10 : 1;
        switch (e.Key)
        {
            case VirtualKey.Delete:
            case VirtualKey.Back: Session.DeleteSelection(); break;
            case VirtualKey.Escape: Surface.CancelGesture(); Surface.FinishTextEdit(false); Session.Tool = EditorTool.Pointer; Session.Select(null); EndStencilDrag(); break;
            case VirtualKey.Left: Session.MoveSelection(new(-distance, 0)); break;
            case VirtualKey.Right: Session.MoveSelection(new(distance, 0)); break;
            case VirtualKey.Up: Session.MoveSelection(new(0, -distance)); break;
            case VirtualKey.Down: Session.MoveSelection(new(0, distance)); break;
            case VirtualKey.F2: EditText(); break;
            default: return;
        }
        e.Handled = true;
    }
}
