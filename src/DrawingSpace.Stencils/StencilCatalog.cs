using DrawingSpace.Core;

namespace DrawingSpace.Stencils;

public static class StencilCatalog
{
    public static IReadOnlyList<Stencil> All { get; } =
    [
        new("flowchart", "Basic Flowchart Shapes", [
            new("process", "Process", ShapeKind.Rectangle),
            new("decision", "Decision", ShapeKind.Decision, 144, 96),
            new("subprocess", "Subprocess", ShapeKind.PredefinedProcess),
            new("terminator", "Start/End", ShapeKind.RoundedRectangle, 144, 48),
            new("document", "Document", ShapeKind.Document, 144, 80),
            new("data", "Data", ShapeKind.Data),
            new("database", "Database", ShapeKind.Cylinder, 112, 96),
            new("preparation", "Preparation", ShapeKind.Preparation),
            new("manual-input", "Manual input", ShapeKind.ManualInput),
            new("manual-operation", "Manual operation", ShapeKind.ManualOperation),
            new("delay", "Delay", ShapeKind.Delay),
            new("display", "Display", ShapeKind.Display),
            new("off-page", "Off-page reference", ShapeKind.OffPage, 72, 80),
            new("on-page", "On-page reference", ShapeKind.Ellipse, 48, 48)
        ]),
        new("basic", "Basic Shapes", [
            new("rectangle", "Rectangle", ShapeKind.Rectangle),
            new("rounded", "Rounded rectangle", ShapeKind.RoundedRectangle),
            new("ellipse", "Ellipse", ShapeKind.Ellipse, 144, 88),
            new("circle", "Circle", ShapeKind.Ellipse, 88, 88),
            new("triangle", "Triangle", ShapeKind.Triangle, 100, 88),
            new("hexagon", "Hexagon", ShapeKind.Hexagon, 120, 88),
            new("pentagon", "Pentagon", ShapeKind.Pentagon, 104, 96),
            new("star", "Star", ShapeKind.Star, 96, 96),
            new("cross", "Cross", ShapeKind.Cross, 88, 88),
            new("ring", "Ring", ShapeKind.Ring, 88, 88),
            new("arrow", "Block arrow", ShapeKind.Arrow, 128, 64),
            new("text", "Text", ShapeKind.Text, 160, 40)
        ]),
        new("network", "Network and Computers", [
            new("cloud", "Cloud", ShapeKind.Cloud, 160, 104),
            new("server", "Server", ShapeKind.Server, 80, 120),
            new("storage", "Storage", ShapeKind.Cylinder, 112, 96),
            new("endpoint", "Workstation", ShapeKind.Display, 136, 88),
            new("network-user", "User", ShapeKind.Person, 64, 104),
            new("network-zone", "Network zone", ShapeKind.Container, 420, 280)
        ]),
        new("organization", "Organization Chart", [
            new("executive", "Executive", ShapeKind.RoundedRectangle, 176, 80),
            new("manager", "Manager", ShapeKind.Rectangle, 176, 72),
            new("position", "Position", ShapeKind.Rectangle, 160, 64),
            new("person", "Person", ShapeKind.Person, 64, 104),
            new("team", "Team frame", ShapeKind.Container, 400, 240)
        ]),
        new("annotations", "Containers and Callouts", [
            new("container", "Container", ShapeKind.Container, 400, 260),
            new("lane", "Swimlane", ShapeKind.Container, 880, 160),
            new("callout", "Callout", ShapeKind.Callout, 184, 104),
            new("note", "Note", ShapeKind.Note, 152, 128),
            new("annotation", "Annotation", ShapeKind.Annotation, 160, 88),
            new("label", "Label", ShapeKind.Text, 200, 40)
        ])
    ];
    public static IEnumerable<StencilMaster> Search(string query) => All.SelectMany(s => s.Masters).Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    public static StencilMaster Find(string id) => All.SelectMany(s => s.Masters).First(m => m.Id == id);
}
