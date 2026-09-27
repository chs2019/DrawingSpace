using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Stencils;

public static class SampleDiagrams
{
    public static DiagramDocument Create(string template = "flowchart") => template switch
    {
        "organization" => Organization(), "network" => Network(), "blank" => new() { Title = "Drawing1" }, _ => Flowchart()
    };
    private static Shape Node(DiagramPage page, string name, ShapeKind kind, double x, double y, double width = 152, double height = 64, string fill = "#E8F0FA")
    {
        var shape = new Shape { Name = name.Replace('\n', ' '), Text = name, Kind = kind, X = x, Y = y, Width = width, Height = height, Style = new() { Fill = fill, Stroke = "#4672C4", TextColor = "#243A5A" } };
        page.Shapes.Add(shape);
        return shape;
    }
    private static void Link(DiagramPage page, Shape a, Shape b, string text = "", PortSide from = PortSide.Auto, PortSide to = PortSide.Auto)
        => page.Connectors.Add(new() { SourceId = a.Id, TargetId = b.Id, Text = text, SourcePort = from, TargetPort = to, Color = "#4672C4", Width = 1.7 });
    private static void Title(DiagramPage page, string title, string subtitle)
    {
        var heading = Node(page, title, ShapeKind.Text, 80, 42, 960, 44, "#00FFFFFF");
        heading.Style.Stroke = "#00FFFFFF"; heading.Style.FontSize = 28; heading.Style.Bold = true;
        var description = Node(page, subtitle, ShapeKind.Text, 80, 92, 960, 28, "#00FFFFFF");
        description.Style.Stroke = "#00FFFFFF"; description.Style.FontSize = 13; description.Style.TextColor = "#65758A";
    }
    public static DiagramDocument Flowchart()
    {
        var page = new DiagramPage { Name = "Purchase approval" };
        Title(page, "Purchase approval workflow", "OPERATIONS  /  PROCUREMENT     •     A simple, connected process from request to delivery");
        var start = Node(page, "Purchase request", ShapeKind.RoundedRectangle, 116, 162, 152, 48, "#4672C4"); start.Style.TextColor = "#FFFFFF";
        var request = Node(page, "Submit request", ShapeKind.Rectangle, 116, 274);
        request.Data = new() { ["Owner"] = "Requestor", ["Department"] = "Operations", ["Status"] = "In progress" };
        var decision = Node(page, "Over €5,000?", ShapeKind.Decision, 380, 264, 148, 88, "#FFF5DE"); decision.Style.Stroke = "#D6A540";
        var manager = Node(page, "Manager review", ShapeKind.Rectangle, 640, 274);
        manager.Data = new() { ["Owner"] = "Department manager", ["SLA"] = "2 business days" };
        var finance = Node(page, "Finance approval", ShapeKind.PredefinedProcess, 640, 424);
        var order = Node(page, "Create order", ShapeKind.Rectangle, 378, 424);
        var receive = Node(page, "Receive goods", ShapeKind.Rectangle, 116, 424);
        var finish = Node(page, "Complete", ShapeKind.RoundedRectangle, 116, 586, 152, 48, "#E7F1E8"); finish.Style.Stroke = "#6C9870";
        var invoice = Node(page, "Invoice", ShapeKind.Document, 378, 578, 152, 72);
        var archive = Node(page, "Records", ShapeKind.Cylinder, 656, 564, 120, 96);
        var note = Node(page, "Approval policy\n\nRequests over €5,000 require manager and finance approval.", ShapeKind.Note, 868, 263, 178, 170, "#FFFBE9");
        note.Style.Stroke = "#D2BD71"; note.Style.FontSize = 13;
        Link(page, start, request); Link(page, request, decision);
        Link(page, decision, manager, "Yes", PortSide.East, PortSide.West);
        Link(page, decision, order, "No", PortSide.South, PortSide.North);
        Link(page, manager, finance); Link(page, finance, order); Link(page, order, receive);
        Link(page, receive, finish); Link(page, order, invoice); Link(page, invoice, archive);
        return new() { Title = "Purchase approval workflow", Pages = [page] };
    }
    public static DiagramDocument Organization()
    {
        var page = new DiagramPage { Name = "Organization" };
        Title(page, "Organization chart", "PEOPLE  /  TEAMS     •     An editable, connected organization");
        var ceo = Node(page, "Alex Morgan\nChief Executive Officer", ShapeKind.RoundedRectangle, 455, 166, 212, 80, "#4672C4"); ceo.Style.TextColor = "#FFFFFF";
        var names = new[] { "Jamie Chen\nOperations", "Taylor Reed\nEngineering", "Jordan Patel\nCustomer experience" };
        for (var i = 0; i < 3; i++)
        {
            var manager = Node(page, names[i], ShapeKind.Rectangle, 136 + i * 320, 340, 210, 80);
            Link(page, ceo, manager, "", PortSide.South, PortSide.North);
            for (var j = 0; j < 2; j++)
            {
                var team = Node(page, $"Team {i * 2 + j + 1}\nSpecialists", ShapeKind.Rectangle, 84 + i * 320 + j * 164, 536, 152, 72, "#F3F6FA");
                Link(page, manager, team, "", PortSide.South, PortSide.North);
            }
        }
        return new() { Title = "Organization chart", Pages = [page] };
    }
    public static DiagramDocument Network()
    {
        var page = new DiagramPage { Name = "Network overview" };
        Title(page, "Network overview", "INFRASTRUCTURE     •     An original editable topology");
        var cloud = Node(page, "Internet", ShapeKind.Cloud, 462, 160, 196, 112);
        var gateway = Node(page, "Gateway", ShapeKind.Hexagon, 484, 364, 152, 88, "#FFF5DE");
        Link(page, cloud, gateway);
        var server = Node(page, "App server", ShapeKind.Server, 230, 540, 104, 136);
        var database = Node(page, "Database", ShapeKind.Cylinder, 494, 560, 132, 112);
        var workstation = Node(page, "Workstation", ShapeKind.Display, 746, 556, 168, 100);
        Link(page, gateway, server); Link(page, gateway, database); Link(page, gateway, workstation);
        foreach (var s in new[] { gateway, server, database, workstation }) s.Data = new() { ["Host"] = s.Name.ToLowerInvariant().Replace(' ', '-'), ["Status"] = "Online" };
        return new() { Title = "Network overview", Pages = [page] };
    }
}
