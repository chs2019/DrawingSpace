using System.Text;

namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private async Task<bool> ConfirmReplaceAsync()
    {
        Surface.FinishTextEdit(true);
        if (!Session.IsDirty) return true;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Save your drawing?", Content = "This drawing has unsaved changes. Save a file before replacing it, or discard the changes. The local recovery copy will be replaced by the new drawing.", PrimaryButtonText = "Save", SecondaryButtonText = "Discard", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return false;
        if (result == ContentDialogResult.Primary) await SaveAsync();
        return true;
    }
    private async Task NewAsync(string template)
    {
        if (!await ConfirmReplaceAsync()) return;
        Session.Load(SampleDiagrams.Create(template)); Session.Tool = EditorTool.Pointer; Surface.Fit(); ShowStatus("New drawing");
    }
    private async Task OpenAsync()
    {
        // Open the picker before replacing the current document; cancellation is non-destructive.
        var file = await _storage.OpenAsync(); if (file is null) return;
        var document = DocumentCodec.Load(file.Value.Text);
        if (!await ConfirmReplaceAsync()) return;
        Session.Load(document); Surface.Fit(); ShowStatus("Opened " + file.Value.Name);
    }
    private async Task SaveAsync()
    {
        Surface.FinishTextEdit(true);
        var json = DocumentCodec.Save(Session.Document);
        await _storage.SaveAsync(SafeName(Session.Document.Title) + ".drawingspace.json", Encoding.UTF8.GetBytes(json), "application/json");
        await _storage.SaveRecoveryAsync(json); Session.MarkSaved(); ShowStatus("Drawing saved");
    }
    private async Task ExportAsync(string format)
    {
        Surface.FinishTextEdit(true);
        var renderer = Surface.Renderer;
        var bytes = format switch
        {
            "svg" => Encoding.UTF8.GetBytes(renderer.ExportSvg(Session.Page)),
            "png" => renderer.ExportPng(Session.Page),
            "pdf" => renderer.ExportPdf(Session.Document),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var contentType = format == "svg" ? "image/svg+xml" : format == "png" ? "image/png" : "application/pdf";
        await _storage.SaveAsync(SafeName(Session.Document.Title) + "." + format, bytes, contentType); ShowStatus("Exported " + format.ToUpperInvariant());
    }
    private static string SafeName(string name)
    {
        var result = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_').Take(100).ToArray()).Trim();
        return string.IsNullOrEmpty(result) ? "Drawing" : result;
    }
    private async Task CopyAsync()
    {
        if (Session.Selection.Count == 0) return;
        _clipboard = Session.CopySelection();
        try { await _storage.WriteClipboardAsync(_clipboard); ShowStatus("Copied selection"); }
        catch { ShowStatus("Copied within DrawingSpace; system clipboard permission was not granted."); }
    }
    private async Task CutAsync() { await CopyAsync(); if (_clipboard is not null) Session.DeleteSelection(); }
    private async Task PasteAsync()
    {
        string? text = null;
        try { text = await _storage.ReadClipboardAsync(); } catch { }
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('{')) text = _clipboard;
        if (string.IsNullOrWhiteSpace(text)) { ShowStatus("Copy shapes before pasting. Browser clipboard access may require permission."); return; }
        Session.Paste(text, new(24, 24)); Surface.FocusCanvas();
    }
    private async Task RenameDocumentAsync()
    {
        var name = await PromptAsync("Rename drawing", "Drawing name", Session.Document.Title);
        if (!string.IsNullOrWhiteSpace(name)) Session.Execute("Rename drawing", () => Session.Document.Title = name.Trim());
    }
    private async Task RenamePageAsync(string id)
    {
        var page = Session.Document.Pages.First(p => p.Id == id);
        var name = await PromptAsync("Rename page", "Page name", page.Name);
        if (!string.IsNullOrWhiteSpace(name)) Session.Execute("Rename page", () => page.Name = name.Trim());
    }
    private async Task DeletePageAsync()
    {
        if (Session.Document.Pages.Count < 2) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Delete page?", Content = $"Delete “{Session.Page.Name}” and its objects? This can be undone.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) { Session.DeletePage(); Surface.Fit(); }
    }
    private async Task FindAsync()
    {
        var query = await PromptAsync("Find shape", "Search name, label or data", "");
        if (string.IsNullOrWhiteSpace(query)) return;
        var match = Session.Page.Shapes.FirstOrDefault(s => Session.Page.IsVisible(s.LayerId) && (s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Text.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Data.Any(d => d.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || d.Value.Contains(query, StringComparison.OrdinalIgnoreCase))));
        if (match is null) ShowStatus("No matching shape found."); else { Session.Select(match.Id); Surface.Fit(true); ShowStatus("Found " + match.Name); }
    }
    private async Task AddDataAsync()
    {
        if (Session.EditableShapes.Count == 0) return;
        var ids = Session.EditableShapes.Select(s => s.Id).ToArray();
        var key = await PromptAsync("Add shape data", "Property name", "Owner"); if (string.IsNullOrWhiteSpace(key)) return;
        var value = await PromptAsync("Property value", key.Trim(), ""); if (value is null) return;
        Session.Execute("Add shape data", () => { foreach (var id in ids) if (Session.Page.Find(id) is { } shape) shape.Data[key.Trim()] = value; });
        if (_pane != "data") ShowPane("data");
    }
    private async Task AddLayerAsync()
    {
        var name = await PromptAsync("New layer", "Layer name", "Layer " + (Session.Page.Layers.Count + 1));
        if (string.IsNullOrWhiteSpace(name)) return;
        Session.Execute("Add layer", () => Session.Page.Layers.Add(new() { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() }));
        if (_pane != "layers") ShowPane("layers");
    }
    private async Task AddCommentAsync()
    {
        if (Session.SelectedShapes.Count != 1) return;
        var id = Session.SelectedShapes[0].Id;
        var text = await PromptAsync("New comment", "Comment", "", true);
        if (string.IsNullOrWhiteSpace(text)) return;
        if (Session.Page.Find(id) is { } shape) Session.Execute("Add comment", () => shape.Comments.Add(text.Trim()));
        if (_pane != "comments") ShowPane("comments");
    }
    private async Task ExportDataAsync()
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var keys = Session.Page.Shapes.SelectMany(s => s.Data.Keys).Distinct().Order(StringComparer.Ordinal).ToArray();
        var text = new StringBuilder(); text.AppendLine(string.Join(",", new[] { "Id", "Name", "Text", "Kind" }.Concat(keys).Select(Quote)));
        foreach (var shape in Session.Page.Shapes)
            text.AppendLine(string.Join(",", new[] { shape.Id, shape.Name, shape.Text, shape.Kind.ToString() }.Concat(keys.Select(k => shape.Data.GetValueOrDefault(k, ""))).Select(value => Quote(value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value))));
        await _storage.SaveAsync(SafeName(Session.Document.Title) + "-data.csv", Encoding.UTF8.GetBytes(text.ToString()), "text/csv;charset=utf-8"); ShowStatus("Exported shape data");
    }
    private async Task<string?> PromptAsync(string title, string label, string value, bool multiline = false)
    {
        var field = OfficeTheme.Field(value, label, 360); field.Header = label; field.AcceptsReturn = multiline; field.TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap; field.MaxLength = multiline ? 10000 : 512;
        if (multiline) field.Height = 140;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = field, PrimaryButtonText = "OK", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.Opened += (_, _) => { field.Focus(FocusState.Programmatic); field.SelectAll(); };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? field.Text : null;
    }
    private async Task ShowMessageAsync(string title, string message)
    {
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, FontFamily = OfficeTheme.Font };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = new ScrollViewer { Content = text, MaxHeight = 500, MaxWidth = 540 }, CloseButtonText = "Close" };
        await dialog.ShowAsync();
    }
}
