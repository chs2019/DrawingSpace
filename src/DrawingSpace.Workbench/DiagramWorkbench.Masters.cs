namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private string? _activeMasterShapeId;

    private void BuildMastersPane()
    {
        _properties.Children.Add(Command("Create master from shape", OfficeIcon.Add, () => RunAsync(() => CreateSelectionMasterAsync(single: true))));
        _properties.Children.Add(Command("Create master from selection", OfficeIcon.Group, () => RunAsync(() => CreateSelectionMasterAsync(single: false))));
        Paragraph("Capture selected shapes, groups, containers and internal connectors as one reusable master. Opening VSSX adds a library without replacing this drawing.");
        if (Session.Document.Masters.Count == 0) { Paragraph("This document has no masters yet."); return; }
        foreach (var master in Session.Document.Masters.Take(128))
        {
            var id = master.Id;
            var select = new OfficeButton(master.Name, OfficeIcon.App, action: () => { _activeMasterId = id; _activeMasterShapeId = null; Refresh(); }) { IsSelected = id == _activeMasterId };
            var insert = new OfficeButton("Insert", OfficeIcon.Add, action: () => Guard(() => { Session.InsertMaster(id, Surface.ViewCenter); Surface.FocusCanvas(); }));
            AutomationProperties.SetName(insert, "Insert master " + master.Name);
            _properties.Children.Add(OfficeTheme.Row(select, insert));
        }
        var active = Session.Document.Masters.FirstOrDefault(m => m.Id == _activeMasterId);
        if (active is null) return;
        Section("Edit master: " + active.Name);
        if (active.Children.Count > 0)
        {
            Paragraph($"{active.Children.Count} component shapes · {active.Connectors.Count} internal connectors. Select a component without ungrouping the master.");
            foreach (var member in active.Children.Prepend(active.Shape).Take(128))
            {
                var id = member.Id;
                var button = new OfficeButton(member.Name, OfficeIcon.App, action: () => { _activeMasterShapeId = id; Refresh(); })
                    { IsSelected = id == (_activeMasterShapeId ?? active.Shape.Id) };
                AutomationProperties.SetName(button, "Edit master component " + member.Name); _properties.Children.Add(button);
            }
        }
        var component = active.Children.Prepend(active.Shape).FirstOrDefault(s => s.Id == _activeMasterShapeId) ?? active.Shape;
        var componentId = component.Id;
        Section(component == active.Shape ? "Master root" : "Component: " + component.Name);
        var label = OfficeTheme.Field(component.Text, "Master text"); _properties.Children.Add(label);
        void Edit(Action<Shape> update) => Session.UpdateMasterComponent(active.Id, componentId, update);
        _properties.Children.Add(Command("Apply master text", OfficeIcon.Check, () => Edit(shape => RichTextOperations.ReplaceAll(shape, label.Text))));
        Number("Master width", component.Width, value => Edit(shape => shape.Width = value), 1, 100000);
        Number("Master height", component.Height, value => Edit(shape => shape.Height = value), 1, 100000);
        if (component != active.Shape)
        {
            Number("Master component X", component.X, value => Edit(shape => shape.X = value), -1000000, 1000000);
            Number("Master component Y", component.Y, value => Edit(shape => shape.Y = value), -1000000, 1000000);
            Number("Master component rotation", component.Rotation, value => Edit(shape => shape.Rotation = value), -36000, 36000);
        }
        var palette = new ColorPalette(); palette.ColorSelected += color => Guard(() => Edit(shape => shape.Style.Fill = color)); _properties.Children.Add(palette);
        Section("Master library");
        _properties.Children.Add(Command("Rename master", OfficeIcon.Text, () => RunAsync(async () =>
        {
            var name = await PromptAsync("Rename master", "Master name", active.Name);
            if (!string.IsNullOrWhiteSpace(name)) Session.UpdateMaster(active.Id, m => m.Name = name.Trim());
        })));
        _properties.Children.Add(Command("Duplicate master", OfficeIcon.Copy, () => RunAsync(async () =>
        {
            var name = await PromptAsync("Duplicate master", "Master name", active.Name + " copy");
            if (!string.IsNullOrWhiteSpace(name)) { _activeMasterId = Session.DuplicateMaster(active.Id, name.Trim()).Id; _activeMasterShapeId = null; Refresh(); }
        })));
        _properties.Children.Add(Command("Delete unused master", OfficeIcon.Delete, () => { Session.DeleteMaster(active.Id); _activeMasterId = null; Refresh(); }));
        _properties.Children.Add(Command("Detach instances and delete master", OfficeIcon.Delete, () => RunAsync(async () =>
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Delete master?",
                Content = "All instances on all pages will keep their current geometry and materialized formulas, but stop inheriting this master. This can be undone.",
                PrimaryButtonText = "Detach and delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) { Session.DeleteMaster(active.Id, true); _activeMasterId = null; Refresh(); }
        })));
        var instance = Session.SelectedShapes.FirstOrDefault(s => s.MasterId == active.Id);
        if (instance is not null)
        {
            Section("Selected instance");
            _properties.Children.Add(Command("Detach selected master instance", OfficeIcon.Ungroup, () => Session.DetachMasterInstance(instance.Id)));
            if (Session.SelectedShapes.Count == 1)
                foreach (var property in instance.LocalOverrides.ToArray())
                    _properties.Children.Add(Command("Reset " + property, OfficeIcon.Undo, () => Session.ResetMasterOverride(instance.Id, property)));
        }
    }

    private async Task CreateSelectionMasterAsync(bool single)
    {
        if (Session.SelectedShapes.Count == 0 || single && Session.SelectedShapes.Count != 1)
        { ShowStatus(single ? "Select one shape to create a master." : "Select shapes to create a reusable master graph."); return; }
        var selected = Session.Selection.ToArray();
        var pageId = Session.ActivePageId; var documentId = Session.Document.Id;
        var name = await PromptAsync("Create master", "Master name", single ? Session.SelectedShapes[0].Name : "Selection master");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Session.Document.Id != documentId || Session.ActivePageId != pageId || !Session.Selection.SetEquals(selected))
            throw new InvalidOperationException("The selection changed while naming the master. Select the intended objects again.");
        _activeMasterId = single ? Session.CreateMaster(Session.SelectedShapes[0].Id, name.Trim()).Id : Session.CreateMasterFromSelection(name.Trim()).Id;
        _activeMasterShapeId = null; Refresh();
    }
}
