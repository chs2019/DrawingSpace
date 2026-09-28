using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class SpatialPrefixQueryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(256)]
    [InlineData(257)]
    public void PrefixUsesOriginalOrdinalsAndRetainsExistingResults(int limit)
    {
        var boxes = Enumerable.Range(0, 257)
            .Select(i => new RectD((i * 73 % 257) * 3, i % 7, 2, 0)).ToArray();
        var index = new SpatialBoundsIndex(boxes);
        var results = new List<int> { -1 };
        index.Query(new RectD(-1, -1, 1000, 20), results, limit);
        Assert.Equal(-1, results[0]);
        Assert.Equal(Enumerable.Range(0, limit).ToArray(), results.Skip(1).OrderBy(i => i).ToArray());
    }

    [Theory]
    [InlineData(17)]
    [InlineData(123456)]
    public void PrefixQueriesMatchExhaustiveInclusiveIntersection(int seed)
    {
        var random = new Random(seed);
        var boxes = Enumerable.Range(0, 512).Select(_ => new RectD(random.Next(-200, 201),
            random.Next(-200, 201), random.Next(0, 41), random.Next(0, 41))).ToArray();
        var index = new SpatialBoundsIndex(boxes);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var area = new RectD(random.Next(-220, 221), random.Next(-220, 221), random.Next(0, 100), random.Next(0, 100));
            var limit = random.Next(boxes.Length + 1);
            var actual = new List<int>();
            index.Query(area, actual, limit);
            var expected = Enumerable.Range(0, limit).Where(i => boxes[i].Intersects(area)).ToArray();
            Assert.Equal(expected, actual.OrderBy(i => i).ToArray());
        }
    }

    [Fact]
    public void InvalidPrefixFailsBeforeAppendingResults()
    {
        var index = new SpatialBoundsIndex(new[] { new RectD(0, 0, 10, 10) });
        var results = new List<int> { 42 };
        var area = new RectD(0, 0, 10, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Query(area, results, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Query(area, results, 2));
        Assert.Equal(new[] { 42 }, results.ToArray());
    }

    [Fact]
    public void EmptyIndexAcceptsEmptyPrefix()
    {
        var index = new SpatialBoundsIndex(Array.Empty<RectD>());
        var results = new List<int>();
        index.Query(new RectD(0, 0, 0, 0), results, 0);
        Assert.Empty(results);
    }

    [Fact]
    public void SelfOverlappingRouteWithoutEarlierSegmentsNeedsNoQueries()
    {
        var missing = new Connector { Id = "missing" };
        var owner = new Connector { Id = "owner", LineJumps = LineJumpStyle.Arc };
        var points = Enumerable.Range(0, 4097)
            .Select(i => i % 2 == 0 ? new PointD(0, 0) : new PointD(100, 100)).ToArray();
        var routes = new Dictionary<string, RouteResult> { [owner.Id] = new(points, true) };
        var result = LineJumpService.Analyze(new[] { missing, owner }, routes, maximumComparisons: 1);
        Assert.False(result.BudgetExceeded);
        Assert.Equal(4096, result.IndexedSegments);
        Assert.Equal(0, result.SpatialQueries);
        Assert.Equal(0, result.Comparisons);
        Assert.Equal(0L, result.Candidates);
        Assert.Empty(result.Jumps[owner.Id]);
    }

    [Fact]
    public void QueriesNeverCollectSelfOrLaterOwnerCandidates()
    {
        var edges = new[]
        {
            new Connector { Id = "first", LineJumps = LineJumpStyle.Arc },
            new Connector { Id = "second", LineJumps = LineJumpStyle.Arc }
        };
        var routes = new Dictionary<string, RouteResult>
        {
            ["first"] = new(new PointD[] { new(0, 50), new(100, 50) }, true),
            ["second"] = new(new PointD[] { new(50, 0), new(50, 100) }, true)
        };
        var result = LineJumpService.Analyze(edges, routes);
        Assert.Equal(1, result.SpatialQueries);
        Assert.Equal(1L, result.Candidates);
        Assert.Equal(1, result.Comparisons);
        Assert.Single(result.Jumps["second"]);
    }
}
