using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Tests;

public class FormulaReferenceRewriterTests
{
    [Theory]
    [InlineData("Sheet.old!Width", "Sheet.new!Width")]
    [InlineData("  GUARD(Sheet.old!Width/2)  ", "  GUARD(Sheet.new!Width/2)  ")]
    [InlineData("\"Sheet.old!Width\"&Sheet.old!Width", "\"Sheet.old!Width\"&Sheet.new!Width")]
    [InlineData("\"a\"\"Sheet.old!Width\"&Sheet.old!Width", "\"a\"\"Sheet.old!Width\"&Sheet.new!Width")]
    [InlineData("User.Sheet.old!Width+Sheet.older!Width", "User.Sheet.old!Width+Sheet.older!Width")]
    [InlineData("'Sheet.old!Width'", "'Sheet.old!Width'")]
    public void RemappingPreservesLiteralsAndTokenBoundaries(string input, string expected)
        => Assert.Equal(expected, FormulaReferenceRewriter.RemapSheets(input, new Dictionary<string, string> { ["old"] = "new" }));

    [Fact] public void IdentityCyclesAreRewrittenOnceRatherThanCascaded()
        => Assert.Equal("Sheet.b!Width+Sheet.a!Height", FormulaReferenceRewriter.RemapSheets("Sheet.a!Width+Sheet.b!Height",
            new Dictionary<string, string> { ["a"] = "b", ["b"] = "a" }));

    [Theory] [InlineData(1, 0, 0)] [InlineData(0, 1, 0)] [InlineData(0, 0, 1)] [InlineData(1, 0, -1)]
    public void MaterializedNumbersRetainDimensions(int length, int angle, int time)
    {
        var value = FormulaValue.Number(2.5, new(length, angle, time));
        Assert.Equal(value, new FormulaEngine().Evaluate(FormulaReferenceRewriter.Literal(value)));
    }

    [Fact] public void MaterializedTextEscapesQuotes()
    {
        var value = FormulaValue.Text("The \"quoted\" name");
        Assert.Equal(value, new FormulaEngine().Evaluate(FormulaReferenceRewriter.Literal(value)));
    }

    [Fact] public void ReplacementsRespectTheFormulaInputBudget()
        => Assert.Throws<FormatException>(() => FormulaReferenceRewriter.Rewrite("Sheet.a!Width", _ => new string('x', FormulaParser.MaximumLength + 1)));
}
