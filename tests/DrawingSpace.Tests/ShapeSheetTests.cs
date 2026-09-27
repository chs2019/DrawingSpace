using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Tests;

public sealed class ShapeSheetTests
{
    private readonly FormulaEngine _engine = new();
    [Theory]
    [InlineData("=2+3*4", 14)] [InlineData("(2+3)*4", 20)] [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)] [InlineData("25.4 mm", 1)] [InlineData("72 pt", 1)]
    [InlineData("SIN(90 deg)", 1)] [InlineData("IF(FALSE,1/0,7)", 7)]
    [InlineData("IFERROR(1/0,12)", 12)] [InlineData("GUARD(2 in)", 2)]
    [InlineData("MIN(7,4,9)", 4)] [InlineData("SUM(1,2,3)", 6)]
    [InlineData("MOD(-3,2)", 1)] [InlineData("ROUND(1.25,1)", 1.3)]
    [InlineData("2.5e2/10", 25)] [InlineData("50% * 4", 2)]
    public void EvaluatesDeterministically(string formula, double expected) { var result = _engine.Evaluate(formula); Assert.False(result.IsError, result.ToString()); Assert.Equal(expected, result.Numeric, 9); }
    [Fact] public void ReferencesAreResolvedAndDiscovered()
    {
        var parsed = FormulaParser.Parse("Width+Sheet.2!Width+ThePage!PageWidth");
        Assert.Equal(3, parsed.References.Count);
        Assert.Equal(6, _engine.Evaluate(parsed, _ => FormulaValue.Number(2, new(Length: 1))).Numeric);
    }
    [Fact] public void DimensionsArePreserved()
    {
        Assert.Equal(new FormulaDimension(Length: 2), _engine.Evaluate("2 in * 3 in").Dimension);
        Assert.Equal(default, _engine.Evaluate("2 in / 4 in").Dimension);
        Assert.True(_engine.Evaluate("2 in + 3 deg").IsError);
    }
    [Fact] public void StringsEscapeQuotesAndConcatenate() => Assert.Equal("a\"b7", _engine.Evaluate("\"a\"\"b\" & 7").ToString());
    [Fact] public void UnsupportedFunctionsAreExplicitErrors() => Assert.StartsWith("#NAME?", _engine.Evaluate("RUNADDON(1)").ToString());
    [Fact] public void LazyFunctionsDoNotResolveUnusedReferences() => Assert.False(_engine.Evaluate("AND(FALSE,Missing)", _ => throw new InvalidOperationException()).IsTrue);
    [Fact] public void MissingReferenceIsNotZero() => Assert.StartsWith("#REF!", _engine.Evaluate("Sheet.2!Width").ToString());
    [Theory] [InlineData("1+")] [InlineData("\"unterminated")] [InlineData("2 3")] [InlineData("MIN(1,)")]
    public void InvalidSyntaxDoesNotCrash(string formula) => Assert.True(_engine.Evaluate(formula).IsError);
    [Fact] public void ParserHasDepthAndLengthLimits()
    {
        Assert.True(_engine.Evaluate(new string('(', 1000) + "1" + new string(')', 1000)).IsError);
        Assert.True(_engine.Evaluate(new string('1', FormulaParser.MaximumLength + 1)).IsError);
    }
}
