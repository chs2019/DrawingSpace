using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class DataCreationTests
{
    private static CsvDataTable Source(string progress = "10")
        => CsvDataTable.Parse("Id,Name,Progress\n0001,Pump A," + progress + "\n0002,Pump B,90", "Assets", "Id");

    [Fact]
    public void CreationCopiesTheRowAndBaselineAndSupportsOneStepUndoRedo()
    {
        var session = new EditorSession();
        var existing = new Shape { Id = "existing" }; session.Page.Shapes.Add(existing);
        session.Select(existing.Id);
        var source = Source(); var before = DocumentCodec.Save(session.Document);
        var shape = session.CreateShapeFromDataRow(source, "0001", new(200, 150), "Name");
        Assert.Equal(new PointD(200, 150), shape.Bounds.Center);
        Assert.Equal("Pump A", shape.Text);
        Assert.Equal("0001", shape.Data["Id"]);
        Assert.Equal("0001", shape.DataBinding!.RowKey);
        Assert.Equal("Assets", shape.DataBinding.SourceId);
        Assert.Equal("10", shape.DataBinding.Baseline["Progress"]);
        Assert.NotSame(shape.Data, shape.DataBinding.Baseline);
        Assert.Equal(shape.Id, Assert.Single(session.Selection));
        Assert.Equal("Create linked shape", session.UndoName);
        var after = DocumentCodec.Save(session.Document);
        session.Undo(); Assert.Equal(before, DocumentCodec.Save(session.Document));
        Assert.Equal("existing", Assert.Single(session.Selection));
        session.Redo(); Assert.Equal(after, DocumentCodec.Save(session.Document));
        Assert.Equal(shape.Id, Assert.Single(session.Selection));
    }

    [Fact]
    public void MultiplePlacementsUseOneTransactionAndIndependentValueMaps()
    {
        var session = new EditorSession(); var source = Source();
        var created = session.CreateShapesFromDataRows(source,
            [new("0001", new(100, 100)), new("0001", new(300, 100)), new("0002", new(500, 100))]);
        Assert.Equal(3, created.Count); Assert.Equal(3, session.Selection.Count);
        Assert.Equal(3, created.Select(s => s.Id).Distinct().Count());
        Assert.Equal("0001", created[0].Text);
        created[0].Data["Progress"] = "changed";
        Assert.Equal("10", created[1].Data["Progress"]);
        Assert.Equal("10", created[0].DataBinding!.Baseline["Progress"]);
        Assert.True(source.TryGetRow("0001", out var row)); Assert.Equal("10", row["Progress"]);
        session.Undo(); Assert.Empty(session.Page.Shapes);
    }

    [Fact]
    public void CreatedBindingRefreshesByKeyAfterLabelChangesAndRoundTrip()
    {
        var session = new EditorSession();
        var created = session.CreateShapeFromDataRow(Source(), "0001", new(100, 100));
        session.Execute("Rename", () => created.Text = "Renamed equipment");
        var reopened = new EditorSession(DocumentCodec.Clone(session.Document));
        var plan = reopened.PreviewDataRefresh(Source("65"));
        Assert.True(reopened.ApplyDataRefresh(plan));
        Assert.Equal("65", reopened.Page.Shapes[0].Data["Progress"]);
        Assert.Equal("Renamed equipment", reopened.Page.Shapes[0].Text);
    }

    [Fact]
    public void UnknownLaterKeyCannotPartiallyCreateShapes()
    {
        var session = new EditorSession(); var document = session.Document; var revision = session.Revision;
        Assert.Throws<ArgumentException>(() => session.CreateShapesFromDataRows(Source(),
            [new("0001", new(100, 100)), new("missing", new(300, 100))]));
        Assert.Same(document, session.Document); Assert.Empty(session.Page.Shapes);
        Assert.Equal(revision, session.Revision); Assert.False(session.CanUndo);
    }

    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.PositiveInfinity)]
    [InlineData(1000000, 100)]
    [InlineData(100, -1000000)]
    public void InvalidCentersDoNotMutate(double x, double y)
    {
        var session = new EditorSession(); var document = session.Document;
        Assert.Throws<ArgumentOutOfRangeException>(() => session.CreateShapeFromDataRow(Source(), "0001", new(x, y)));
        Assert.Same(document, session.Document); Assert.Empty(session.Page.Shapes); Assert.False(session.CanUndo);
    }

    [Fact]
    public void LockedHiddenAndMissingLayersCannotReceiveCreatedShapes()
    {
        var session = new EditorSession();
        session.Page.Layers[0].Locked = true;
        Assert.Throws<InvalidOperationException>(() => session.CreateShapeFromDataRow(Source(), "0001", new(100, 100)));
        session.Page.Layers[0].Locked = false; session.Page.Layers[0].Visible = false;
        Assert.Throws<InvalidOperationException>(() => session.CreateShapeFromDataRow(Source(), "0001", new(100, 100)));
        Assert.Throws<InvalidOperationException>(() => session.CreateShapeFromDataRow(Source(), "0001", new(100, 100), layerId: "missing"));
        Assert.Empty(session.Page.Shapes);
    }

    [Fact]
    public void DefaultLayerSelectionChoosesAVisibleUnlockedLayer()
    {
        var session = new EditorSession(); session.Page.Layers[0].Locked = true;
        session.Page.Layers.Add(new() { Id = "editable", Name = "Editable" });
        Assert.Equal("editable", session.CreateShapeFromDataRow(Source(), "0001", new(100, 100)).LayerId);
    }

    [Fact]
    public void EmptyBatchIsANoOpAndInvalidLabelsAndBudgetsRejectBeforeMutation()
    {
        var session = new EditorSession(); var source = Source(); var revision = session.Revision;
        Assert.Empty(session.CreateShapesFromDataRows(source, Array.Empty<DataRowPlacement>()));
        Assert.Equal(revision, session.Revision); Assert.False(session.CanUndo);
        Assert.Throws<ArgumentException>(() => session.CreateShapeFromDataRow(source, "0001", new(100, 100), "Missing"));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.CreateShapesFromDataRows(source,
            new DataRowPlacement[EditorSession.MaximumDataRowPlacements + 1]));
        Assert.Empty(session.Page.Shapes);
    }

    [Fact]
    public void ActiveGestureIsNotCommittedOrCancelledByCreation()
    {
        var session = new EditorSession(); session.Begin("Gesture");
        Assert.Throws<InvalidOperationException>(() => session.CreateShapeFromDataRow(Source(), "0001", new(100, 100)));
        Assert.True(session.IsInteracting); Assert.Empty(session.Page.Shapes); session.Cancel();
    }
}
