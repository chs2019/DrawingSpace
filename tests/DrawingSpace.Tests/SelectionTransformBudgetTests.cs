using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;

namespace DrawingSpace.Tests;

public sealed class SelectionTransformBudgetTests
{
    private static Shape Make(string id, double shear = 0) => new()
    { Id = id, X = 20, Y = 40, Width = 120, Height = 60, ShearX = shear };

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void PreviewAcceptsTheSameShearBoundaryAsDocumentValidation(double shear)
    {
        var shape = Make("shape");
        var page = new DiagramPage { Shapes = [shape] };
        var document = new DiagramDocument { Pages = [page] };
        var snapshot = SelectionTransformSnapshot.Capture(page, [shape]);
        snapshot.Apply(new MatrixD(1, 0, shear, 1, 0, 0));
        Assert.Equal(shear, shape.ShearX, 7);
        DocumentCodec.Validate(document);
    }

    [Theory]
    [InlineData(-1001)]
    [InlineData(1001)]
    public void ExcessiveShearIsRejectedBeforeItCanReachTheRenderer(double shear)
    {
        var shape = Make("shape"); var page = new DiagramPage { Shapes = [shape] };
        var before = shape.WorldMatrix;
        var snapshot = SelectionTransformSnapshot.Capture(page, [shape]);
        Assert.Throws<InvalidOperationException>(() => snapshot.Apply(new MatrixD(1, 0, shear, 1, 0, 0)));
        Assert.Equal(before, shape.WorldMatrix);
        Assert.Equal(MatrixD.Identity, snapshot.LastTransform);
    }

    [Fact]
    public void AHighShearLaterTargetDoesNotLeaveAnEarlierTargetPartiallyUpdated()
    {
        var first = Make("first"); var second = Make("second", 800);
        var page = new DiagramPage { Shapes = [first, second] };
        var document = new DiagramDocument { Pages = [page] };
        DocumentCodec.Validate(document);
        var before = DocumentCodec.Save(document);
        var snapshot = SelectionTransformSnapshot.Capture(page, [first, second]);
        // The first shape would have valid shear 500; the second would have 1300.
        Assert.Throws<InvalidOperationException>(() => snapshot.Apply(new MatrixD(1, 0, 500, 1, 0, 0)));
        Assert.Equal(before, DocumentCodec.Save(document));
        Assert.Equal(MatrixD.Identity, snapshot.LastTransform);
    }
}
