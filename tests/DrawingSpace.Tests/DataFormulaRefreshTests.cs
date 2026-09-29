using DrawingSpace.Documents;
using DrawingSpace.Editing;

namespace DrawingSpace.Tests;

public sealed class DataFormulaRefreshTests
{
    private static EditorSession Session()
    {
        var document = new DiagramDocument();
        document.Pages[0].Shapes.Add(new() { Id = "shape", Text = "0001" });
        return new(document);
    }
    private static CsvDataTable Source(string progress)
        => CsvDataTable.Parse("Id,Progress\n0001," + progress, "Assets", "Id");

    [Fact]
    public void LinkedDataDrivesGeometryFormulasAndUndoRestoresBoth()
    {
        var session = Session();
        var shape = session.Page.Find("shape")!;
        shape.Cells["Width"] = new() { Formula = "(Prop.Progress / 100) * 2 in" };
        session.ApplyDataRefresh(session.PreviewDataRefresh(Source("20")));
        Assert.Equal(38.4, session.Page.Find("shape")!.Width, 8);
        session.ApplyDataRefresh(session.PreviewDataRefresh(Source("90")));
        Assert.Equal(172.8, session.Page.Find("shape")!.Width, 8);
        Assert.Empty(session.FormulaDiagnostics);
        session.Undo();
        Assert.Equal("20", session.Page.Find("shape")!.Data["Progress"]);
        Assert.Equal(38.4, session.Page.Find("shape")!.Width, 8);
    }

    [Theory]
    [InlineData("Prop.Progress", "GUARD(42)")]
    [InlineData("Prop.Progress", "42")]
    [InlineData("Prop.Progress.Value", "42")]
    [InlineData("prop.progress", "42")]
    public void CellOwnedPropertyIsReportedRatherThanSilentlyReplaced(string name, string formula)
    {
        var session = Session();
        var shape = session.Page.Find("shape")!;
        shape.Data["Progress"] = "42";
        shape.Cells[name] = new() { Formula = formula, Value = "42" };
        var plan = session.PreviewDataRefresh(Source("90"), overwriteConflicts: true);
        Assert.Single(plan.Issues, issue => issue.Field == "Progress" && issue.Reason.StartsWith("ShapeSheet"));
        session.ApplyDataRefresh(plan);
        Assert.Equal("42", session.Page.Find("shape")!.Data["Progress"]);
        Assert.False(session.Page.Find("shape")!.DataBinding!.Baseline.ContainsKey("Progress"));
    }

    [Fact]
    public void InheritedPropertyCellIsAlsoProtected()
    {
        var session = Session();
        var master = new DiagramMaster { Id = "master", Shape = new() { Id = "master-shape" } };
        master.Shape.Cells["Prop.Progress"] = new() { Formula = "GUARD(42)", Value = "42" };
        session.Document.Masters.Add(master);
        var shape = session.Page.Find("shape")!;
        shape.MasterId = master.Id; shape.Data["Progress"] = "42";
        var plan = session.PreviewDataRefresh(Source("90"), overwriteConflicts: true);
        Assert.Single(plan.Issues, issue => issue.Field == "Progress");
        session.ApplyDataRefresh(plan);
        Assert.Equal("42", session.Page.Find("shape")!.Data["Progress"]);
    }

    [Fact]
    public void NewlyIntroducedPropertyCellRejectsAStaleRefreshBeforeAnyWrite()
    {
        var session = Session();
        var plan = session.PreviewDataRefresh(Source("90"));
        session.Page.Find("shape")!.Cells["Prop.Progress"] = new() { Formula = "GUARD(42)", Value = "42" };
        Assert.Throws<InvalidOperationException>(() => session.ApplyDataRefresh(plan));
        Assert.Empty(session.Page.Find("shape")!.Data);
        Assert.Null(session.Page.Find("shape")!.DataBinding);
    }
}
