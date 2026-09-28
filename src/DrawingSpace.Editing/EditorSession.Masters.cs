using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public sealed partial class EditorSession
{
    public DiagramMaster CreateMasterFromSelection(string name)
    {
        var master = MasterAuthoring.Capture(Document, Page, Selection, name);
        Execute("Create master from selection", () => Document.Masters.Add(master));
        return master;
    }

    public void UpdateMasterComponent(string masterId, string componentId, Action<Shape> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        UpdateMaster(masterId, master =>
        {
            var component = master.Children.Prepend(master.Shape).FirstOrDefault(s => s.Id == componentId)
                ?? throw new ArgumentException("Master component not found.", nameof(componentId));
            update(component);
        });
    }

    public DiagramMaster DuplicateMaster(string masterId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var original = Document.Masters.FirstOrDefault(m => m.Id == masterId) ?? throw new ArgumentException("Master not found.", nameof(masterId));
        DiagramMaster? copy = null;
        Execute("Duplicate master", () => { copy = MasterService.Import(Document, [original])[0]; copy.Name = name; });
        return copy!;
    }

    public void DetachMasterInstance(string shapeId)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        if (shape.MasterId is null) return;
        var members = shape.MasterInstanceId is { } instance
            ? Page.Shapes.Where(s => s.MasterId == shape.MasterId && s.MasterInstanceId == instance).ToArray() : [shape];
        Execute("Detach master instance", () => MasterAuthoring.Detach(Document, Page, members));
    }

    /// <summary>Rejects removal of an in-use master unless explicit detachment is requested.</summary>
    public void DeleteMaster(string masterId, bool detachInstances = false)
    {
        var master = Document.Masters.FirstOrDefault(m => m.Id == masterId) ?? throw new ArgumentException("Master not found.", nameof(masterId));
        Execute("Delete master", () =>
        {
            foreach (var page in Document.Pages)
            {
                var members = page.Shapes.Where(s => s.MasterId == masterId).ToArray();
                if (members.Length == 0) continue;
                if (!detachInstances) throw new InvalidOperationException("This master has live instances. Detach them explicitly before deleting it.");
                MasterAuthoring.Detach(Document, page, members);
            }
            Document.Masters.Remove(master);
        });
    }
}
