using DrawingSpace.Core;

namespace DrawingSpace.Tests;

public sealed class SpatialBoundsIndexTests
{
    [Fact] public void RandomQueriesMatchBruteForceIncludingDegenerateBounds()
    {
        var random = new Random(7319);
        var bounds = Enumerable.Range(0, 2049).Select(i => new RectD(random.Next(-1000, 1000), random.Next(-1000, 1000),
            i % 9 == 0 ? 0 : random.Next(1, 400), i % 13 == 0 ? 0 : random.Next(1, 400))).ToArray();
        var index = new SpatialBoundsIndex(bounds); var actual = new List<int>();
        for (var i = 0; i < 200; i++)
        {
            var query = new RectD(random.Next(-1400, 1400), random.Next(-1400, 1400), random.Next(400), random.Next(400));
            actual.Clear(); index.Query(query, actual); actual.Sort();
            Assert.Equal(Enumerable.Range(0, bounds.Length).Where(j => bounds[j].Intersects(query)), actual);
        }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(8)] [InlineData(9)] [InlineData(17)] [InlineData(65)] [InlineData(129)] [InlineData(8193)]
    public void EveryLeafCapacityBoundaryReturnsEachItemOnce(int count)
    {
        var index = new SpatialBoundsIndex(Enumerable.Repeat(new RectD(0, 0, 1, 1), count));
        var hits = new List<int>(); index.Query(new(1, 1, 0, 0), hits);
        Assert.Equal(count, hits.Distinct().Count()); Assert.Equal(count, hits.Count);
    }

    [Fact] public void InputMutationDoesNotChangeSnapshotAndResultsAreAppended()
    {
        RectD[] bounds = [new(0, 0, 1, 1)]; var index = new SpatialBoundsIndex(bounds); bounds[0] = new(10, 10, 1, 1);
        var hits = new List<int> { 42 }; index.Query(new(0, 0, 1, 1), hits); Assert.Equal([42, 0], hits);
    }

    [Fact] public void RejectsNonFiniteOrNegativeBounds()
    {
        Assert.Throws<ArgumentException>(() => new SpatialBoundsIndex([new(double.NaN, 0, 1, 1)]));
        Assert.Throws<ArgumentException>(() => new SpatialBoundsIndex([new(0, 0, -1, 1)]));
        var index = new SpatialBoundsIndex([]);
        Assert.Throws<ArgumentException>(() => index.Query(new(0, 0, double.PositiveInfinity, 1), []));
    }

    [Fact] public void ReusedQueryStorageAndAffineBoundsMappingDoNotAllocate()
    {
        var index = new SpatialBoundsIndex(Enumerable.Range(0, 1000).Select(i => new RectD(i * 2, 0, 1, 1)));
        var hits = new List<int>(1000); var query = new RectD(0, 0, 100, 10);
        var matrix = MatrixD.Rotation(33) * MatrixD.Scale(2, .5); var box = new RectD(0, 0, 40, 60);
        for (var i = 0; i < 100; i++) { hits.Clear(); index.Query(query, hits); _ = matrix.Map(box); }
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) { hits.Clear(); index.Query(query, hits); _ = matrix.Map(box); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - bytes);
    }

    [Fact] public void AffineBoundsExactlyMatchFourCornerMapping()
    {
        var random = new Random(987);
        for (var i = 0; i < 100; i++)
        {
            var matrix = new MatrixD(random.NextDouble(), random.NextDouble(), -random.NextDouble(), random.NextDouble(), 34, -97);
            var bounds = new RectD(-20, -30, random.Next(200), random.Next(100));
            Assert.Equal(RectD.Bounds(bounds.Corners.Select(matrix.Map)), matrix.Map(bounds));
        }
    }
}
