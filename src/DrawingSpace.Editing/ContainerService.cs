using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public static class ContainerService
{
    public static bool Contains(Shape container, Shape shape)
    {
        if (container.Id == shape.Id || !container.WorldMatrix.TryInvert(out var inverse)) return false;
        const double tolerance = 1e-8;
        return shape.WorldCorners.Select(inverse.Map).All(p => p.X >= -tolerance && p.X <= 1 + tolerance && p.Y >= -tolerance && p.Y <= 1 + tolerance);
    }

    public static Shape? FindContainer(DiagramPage page, Shape shape)
    {
        var descendants = page.Descendants(shape.Id).Select(s => s.Id).ToHashSet();
        return page.Shapes.Where(c => c.Id != shape.Id && !descendants.Contains(c.Id) && (c.Container is not null || c.Kind == ShapeKind.Container)
                && PageAllowsMembership(page, c) && Contains(c, shape))
            .OrderBy(c => c.Width * c.Height).FirstOrDefault();
    }

    private static bool PageAllowsMembership(DiagramPage page, Shape container) => page.IsVisible(container.LayerId) && !page.IsLocked(container) && container.Container?.LockedMembership != true;

    public static void Assign(DiagramPage page, Shape shape, string? containerId)
    {
        if (shape.ContainerId == containerId) return;
        if (page.Find(shape.ContainerId)?.Container?.LockedMembership == true) throw new InvalidOperationException("The current container has locked membership.");
        if (containerId is not null)
        {
            var container = page.Find(containerId) ?? throw new ArgumentException("The target container does not exist.", nameof(containerId));
            if (container.Container is null && container.Kind != ShapeKind.Container) throw new ArgumentException("The target shape is not a container.", nameof(containerId));
            if (!PageAllowsMembership(page, container)) throw new InvalidOperationException("The target container does not accept membership changes.");
            if (shape.Id == containerId || page.Descendants(shape.Id).Any(s => s.Id == containerId)) throw new InvalidOperationException("Container membership cannot contain a cycle.");
        }
        shape.ContainerId = containerId;
    }

    public static IReadOnlyList<Shape> TransformClosure(DiagramPage page, IEnumerable<Shape> roots)
    {
        var result = new Dictionary<string, Shape>();
        foreach (var root in roots)
        {
            if (page.IsLocked(root)) continue;
            result.TryAdd(root.Id, root);
            foreach (var member in page.Descendants(root.Id))
            {
                if (page.IsLocked(member)) throw new InvalidOperationException("A locked container member prevents this transform.");
                result.TryAdd(member.Id, member);
            }
        }
        return result.Values.ToArray();
    }

    public static void Fit(DiagramPage page, Shape container)
    {
        var options = container.Container ??= new();
        var children = page.Shapes.Where(s => s.ContainerId == container.Id).ToArray();
        if (children.Length == 0) return;
        if (Math.Abs(container.Rotation) > 1e-8 || Math.Abs(container.ShearX) > 1e-8) return;
        var bounds = children.Select(s => s.WorldBounds).Aggregate(RectD.Union);
        container.X = bounds.Left - options.Padding;
        container.Y = bounds.Top - options.Padding - options.HeaderHeight;
        container.Width = Math.Max(1, bounds.Width + options.Padding * 2);
        container.Height = Math.Max(1, bounds.Height + options.Padding * 2 + options.HeaderHeight);
    }

    public static void LayoutLanes(DiagramPage page, Shape container)
    {
        var options = container.Container;
        if (options is null || options.Layout == ContainerLayout.Free) return;
        var lanes = page.Shapes.Where(s => s.ContainerId == container.Id && s.Container is not null).ToArray();
        if (lanes.Length == 0) return;
        var inner = new RectD(container.X + options.Padding, container.Y + options.HeaderHeight + options.Padding,
            Math.Max(1, container.Width - 2 * options.Padding), Math.Max(1, container.Height - options.HeaderHeight - 2 * options.Padding));
        for (var index = 0; index < lanes.Length; index++)
        {
            var lane = lanes[index];
            var target = options.Layout == ContainerLayout.HorizontalLanes
                ? new RectD(inner.X, inner.Y + index * inner.Height / lanes.Length, inner.Width, inner.Height / lanes.Length)
                : new RectD(inner.X + index * inner.Width / lanes.Length, inner.Y, inner.Width / lanes.Length, inner.Height);
            var delta = new PointD(target.X - lane.X, target.Y - lane.Y);
            foreach (var child in page.Descendants(lane.Id)) { child.X += delta.X; child.Y += delta.Y; }
            lane.X = target.X; lane.Y = target.Y; lane.Width = Math.Max(1, target.Width); lane.Height = Math.Max(1, target.Height);
        }
    }

    public static void AutoFit(DiagramPage page)
    {
        var containers = page.Shapes.Where(s => s.Container is { AutoResize: true, Layout: ContainerLayout.Free }).ToArray();
        int Depth(Shape shape)
        {
            var result = 0; var seen = new HashSet<string>();
            while (shape.ContainerId is { } id && seen.Add(id) && page.Find(id) is { } parent) { result++; shape = parent; }
            return result;
        }
        foreach (var container in containers.OrderByDescending(Depth)) Fit(page, container);
    }
}
