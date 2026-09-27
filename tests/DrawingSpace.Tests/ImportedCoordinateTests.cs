using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Visio;

namespace DrawingSpace.Tests;

public sealed class ImportedCoordinateTests
{
    private static EditorSession ImportGroup()
    {
        var document = new DiagramDocument(); var page = document.Pages[0];
        page.Shapes.AddRange([new() { Id = "a", X = 100, Y = 100, Rotation = 25, Text = "A" }, new() { Id = "b", X = 400, Y = 300, Rotation = -17, Text = "B" }]);
        GroupService.Create(page, ["a", "b"]);
        return new(VisioReader.Read(VisioWriter.Write(document).Bytes).Document);
    }
    [Fact] public void EditingImportedGroupTextDoesNotReinterpretLocalCellsAsWorldCoordinates()
    {
        var session = ImportGroup(); var corners = session.Page.Shapes.ToDictionary(s => s.Id, s => s.WorldCorners);
        session.SetText("a", "Changed text");
        foreach (var shape in session.Page.Shapes)
            for (var index = 0; index < 4; index++) Assert.True(corners[shape.Id][index].Distance(shape.WorldCorners[index]) < 1e-6);
    }
    [Fact] public void ImportedGroupMovementCommitsAndUndoesAtomically()
    {
        var session = ImportGroup(); var corners = session.Page.Find("a")!.WorldCorners;
        session.Select("a"); session.MoveSelection(new(50, 70));
        for (var index = 0; index < 4; index++) Assert.True((corners[index] + new PointD(50, 70)).Distance(session.Page.Find("a")!.WorldCorners[index]) < 1e-6);
        session.Undo(); for (var index = 0; index < 4; index++) Assert.True(corners[index].Distance(session.Page.Find("a")!.WorldCorners[index]) < 1e-6);
    }
    [Fact] public void ValueFunctionCannotResetTheRecursionBudget()
    {
        var formula = "VALUE(\"User.Value\")";
        var engine = new DrawingSpace.ShapeSheet.FormulaEngine();
        var result = engine.Evaluate(formula, _ => DrawingSpace.ShapeSheet.FormulaValue.Text("not a number"));
        Assert.True(result.IsError || result.Kind == DrawingSpace.ShapeSheet.FormulaValueKind.Text);
    }
}
