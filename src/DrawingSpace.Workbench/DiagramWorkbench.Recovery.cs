namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private readonly TextBlock _recoveryStatus = OfficeTheme.Text("", 11, OfficeTheme.Secondary);
    private readonly SemaphoreSlim _recoveryWriteGate = new(1, 1);
    private bool _savingRecovery, _pendingRecovery;
    private DiagramDocument? _recoveryDocument;
    public string RecoveryStatusText => _recoveryStatus.Text;
    public long RecoveryWriteCount { get; private set; }
    public long RecoveryRevision { get; private set; } = -1;
    public bool IsRecoveryCurrent => ReferenceEquals(_recoveryDocument, Session.Document)
        && RecoveryRevision == Session.Revision && !Session.IsInteracting;

    private void ShowRecoveryStatus(string text, bool error = false)
    {
        if (_disposed) return;
        _recoveryStatus.Text = text;
        _recoveryStatus.Foreground = OfficeTheme.Brush(error ? "#B42318" : OfficeTheme.Secondary);
        StateChanged?.Invoke();
    }

    private async Task SaveRecoverySnapshotAsync(DiagramDocument document, long revision, string json)
    {
        // Explicit file saves and debounced autosaves share the same writer. A
        // queued older snapshot cannot replace a newer revision or another document.
        await _recoveryWriteGate.WaitAsync();
        try
        {
            if (_disposed || !ReferenceEquals(document, Session.Document) || revision != Session.Revision || Session.IsInteracting) return;
            ShowRecoveryStatus("Saving recovery…");
            await _storage.SaveRecoveryAsync(json);
            _recoveryDocument = document; RecoveryRevision = revision; RecoveryWriteCount++;
            ShowRecoveryStatus(IsRecoveryCurrent ? "Recovery saved" : "Recovery pending");
        }
        catch
        {
            ShowRecoveryStatus("Recovery failed", true);
            throw;
        }
        finally { _recoveryWriteGate.Release(); }
    }

    private async Task WriteRecoveryAsync()
    {
        if (_disposed) return;
        if (Session.IsInteracting)
        {
            // Never serialize a live gesture that may subsequently be cancelled.
            _autosave.Stop(); _autosave.Start();
            return;
        }
        if (_savingRecovery) { _pendingRecovery = true; return; }
        _savingRecovery = true;
        try
        {
            do
            {
                _pendingRecovery = false;
                if (Session.IsInteracting) { _autosave.Stop(); _autosave.Start(); break; }
                var document = Session.Document;
                var revision = Session.Revision;
                await SaveRecoverySnapshotAsync(document, revision, DocumentCodec.Save(document));
            } while (_pendingRecovery && !_disposed);
        }
        catch (Exception ex)
        {
            // The dedicated indicator reports recovery errors without erasing a
            // foreground command result. Explicit Save still propagates its errors.
            Console.Error.WriteLine(ex);
            ShowRecoveryStatus("Recovery failed", true);
        }
        finally { _savingRecovery = false; }
    }
}
