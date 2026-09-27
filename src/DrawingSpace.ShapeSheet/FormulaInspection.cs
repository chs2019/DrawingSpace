namespace DrawingSpace.ShapeSheet;

public static class FormulaInspection
{
    public static bool IsGuarded(string formula)
    {
        try { return Nodes(FormulaParser.Parse(formula).Root).OfType<CallNode>().Any(c => c.Name is "GUARD" or "THEMEGUARD"); }
        catch (FormatException) { return false; }
    }
    public static string? AssignmentTarget(string formula)
    {
        try { return FormulaParser.Parse(formula).Root is CallNode { Name: "SETATREF", Arguments.Count: 1 } call && call.Arguments[0] is ReferenceNode reference ? reference.Name : null; }
        catch (FormatException) { return null; }
    }
    private static IEnumerable<FormulaNode> Nodes(FormulaNode root)
    {
        var stack = new Stack<FormulaNode>(); stack.Push(root);
        while (stack.TryPop(out var node))
        {
            yield return node;
            switch (node)
            {
                case UnaryNode unary: stack.Push(unary.Operand); break;
                case BinaryNode binary: stack.Push(binary.Right); stack.Push(binary.Left); break;
                case CallNode call: foreach (var argument in call.Arguments) stack.Push(argument); break;
            }
        }
    }
}
