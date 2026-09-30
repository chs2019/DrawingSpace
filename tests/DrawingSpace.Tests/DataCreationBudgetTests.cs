using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class DataCreationBudgetTests
{
    [Fact]
    public void RepeatedLargeRowsAreChargedPerAppearanceBeforeMutation()
    {
        var source = CsvDataTable.Parse("Id,Value\n0001," + new string('x', 4096), "Assets", "Id");
        var session = new EditorSession(); var document = session.Document; var page = session.Page;
        var before = DocumentCodec.Save(document); var revision = session.Revision;
        var placements = Enumerable.Repeat(new DataRowPlacement("0001", new(100, 100)), 256).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.CreateShapesFromDataRows(source, placements));
        Assert.Same(document, session.Document); Assert.Same(page, session.Page);
        Assert.Equal(before, DocumentCodec.Save(document)); Assert.Equal(revision, session.Revision);
        Assert.False(session.CanUndo); Assert.Empty(session.Selection);
    }

    [Fact]
    public void ARepeatedRowIsSupportedBelowTheTextBudget()
    {
        var source = CsvDataTable.Parse("Id,Value\n0001," + new string('x', 4096), "Assets", "Id");
        var session = new EditorSession();
        var shapes = session.CreateShapesFromDataRows(source,
            Enumerable.Repeat(new DataRowPlacement("0001", new(100, 100)), 16).ToArray());
        Assert.Equal(16, shapes.Count);
        Assert.All(shapes, shape => Assert.Equal(4096, shape.DataBinding!.Baseline["Value"].Length));
        session.Undo(); Assert.Empty(session.Page.Shapes);
    }

    [Fact]
    public void NullPlacementKeyAndUnknownCasePreserveSelectionAndDocumentIdentity()
    {
        var source = CsvDataTable.Parse("Id,Value\nAsset,10", "Assets", "Id");
        var session = new EditorSession(); var shape = new Shape(); session.Page.Shapes.Add(shape); session.Select(shape.Id);
        var document = session.Document; var revision = session.Revision;
        Assert.Throws<ArgumentException>(() => session.CreateShapeFromDataRow(source, null!, new(100, 100)));
        Assert.Throws<ArgumentException>(() => session.CreateShapeFromDataRow(source, "asset", new(100, 100)));
        Assert.Same(document, session.Document); Assert.Same(shape, Assert.Single(session.Page.Shapes));
        Assert.Equal(shape.Id, Assert.Single(session.Selection)); Assert.Equal(revision, session.Revision);
    }
}
