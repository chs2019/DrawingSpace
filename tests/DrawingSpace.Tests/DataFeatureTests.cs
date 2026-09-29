using System.Xml.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Skia;
using DrawingSpace.Visio;
using SkiaSharp;

namespace DrawingSpace.Tests;

public sealed class DataFeatureTests
{
    private static EditorSession Session()
    {
        var document = new DiagramDocument();
        document.Pages[0].Shapes.Add(new() { Id = "a", Name = "Asset", Text = "0001", X = 100, Y = 100, Width = 120, Height = 70 });
        return new(document);
    }
    private static CsvDataTable Source(string progress = "10")
        => CsvDataTable.Parse("Id,Progress,Owner\n0001," + progress + ",Alice\n", "Assets", "Id");
    private static void Refresh(EditorSession session, string value = "10")
        => session.ApplyDataRefresh(session.PreviewDataRefresh(Source(value)));

    [Fact] public void CsvPreservesLeadingZerosQuotedCommasQuotesAndNewlines()
    {
        var source = CsvDataTable.Parse("\uFEFFId,Value\r\n0001,\"A, \"\"quote\"\"\r\nline\"\r\n", "Assets", "Id");
        Assert.True(source.TryGetRow("0001", out var row));
        Assert.False(source.TryGetRow("1", out _));
        Assert.Equal("A, \"quote\"\r\nline", row["Value"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)row)["Value"] = "changed");
    }
    [Theory]
    [InlineData("Id,V\nx,1\nx,2")]
    [InlineData("Id,Id\nx,1")]
    [InlineData("Id,V\n,1")]
    [InlineData("Id,V\nx,\"unfinished")]
    [InlineData("Id,V\nx,\"closed\"junk")]
    [InlineData("Id,V\nx,1,extra")]
    [InlineData("Id,V\nx")]
    [InlineData("Id,V\nx,un\"quoted")]
    [InlineData("Id,V\nx,\0")]
    [InlineData("")]
    public void InvalidCsvFailsAtomically(string text)
        => Assert.Throws<InvalidDataException>(() => CsvDataTable.Parse(text, "Assets", "Id"));

    [Theory] [InlineData(';')] [InlineData('\t')]
    public void DelimitersAndEmptyTrailingFieldsAreSupported(char delimiter)
    {
        var source = CsvDataTable.Parse($"Id{delimiter}V\rx{delimiter}\ry{delimiter}\"a{delimiter}b\"", "Assets", "Id", delimiter);
        Assert.Equal("", source.Rows[0]["V"]); Assert.Equal($"a{delimiter}b", source.Rows[1]["V"]);
    }
    [Fact] public void ParserRejectsResourceExcess()
    {
        Assert.Throws<InvalidDataException>(() => CsvDataTable.Parse(new string('x', CsvDataTable.MaximumCharacters + 1), "A", "Id"));
        Assert.Throws<InvalidDataException>(() => CsvDataTable.Parse("Id,V\nx," + new string('v', 4097), "A", "Id"));
        Assert.Throws<InvalidDataException>(() => CsvDataTable.Parse("Id," + string.Join(",", Enumerable.Range(0, 128)), "A", "Id"));
        Assert.Throws<InvalidDataException>(() => CsvDataTable.Parse("Id\n" + string.Join("\n", Enumerable.Range(0, 10001)), "A", "Id"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CsvDataTable.Parse("Id\nx", "A", "Id", '|'));
    }

    [Fact] public void PreviewIsPureAndApplyUsesOneUndoTransaction()
    {
        var session = Session(); var original = DocumentCodec.Save(session.Document);
        var plan = session.PreviewDataRefresh(Source());
        Assert.Equal(original, DocumentCodec.Save(session.Document)); Assert.Equal(1, plan.ChangedShapes);
        Assert.True(session.ApplyDataRefresh(plan)); Assert.Equal("10", session.Page.Find("a")!.Data["Progress"]);
        Assert.Throws<InvalidOperationException>(() => session.ApplyDataRefresh(plan));
        session.Undo(); Assert.Equal(original, DocumentCodec.Save(session.Document));
        session.Redo(); Assert.Equal("Assets", session.Page.Find("a")!.DataBinding!.SourceId);
    }
    [Fact] public void StableKeyIgnoresRowOrderAndLabelChangesAndLinksMultipleShapes()
    {
        var session = Session(); session.AddShape(new() { Id = "b", Text = "0001" }); Refresh(session);
        session.SetText("a", "Renamed");
        var source = CsvDataTable.Parse("Id,Progress,Owner\n9999,99,Bob\n0001,20,Alice", "Assets", "Id");
        session.ApplyDataRefresh(session.PreviewDataRefresh(source));
        Assert.All(session.Page.Shapes, shape => Assert.Equal("20", shape.Data["Progress"]));
    }
    [Fact] public void LocalAndSourceEditsConflictButUnchangedSourceDoesNot()
    {
        var session = Session(); Refresh(session);
        session.Execute("Local", () => session.Page.Find("a")!.Data["Progress"] = "15");
        var unchanged = session.PreviewDataRefresh(Source());
        Assert.Empty(unchanged.Issues); Assert.False(session.ApplyDataRefresh(unchanged));
        var changed = session.PreviewDataRefresh(Source("20"));
        Assert.Contains(changed.Issues, i => i.Field == "Progress");
        Assert.False(session.ApplyDataRefresh(changed));
        Assert.Equal("15", session.Page.Find("a")!.Data["Progress"]);
        session.ApplyDataRefresh(session.PreviewDataRefresh(Source("20"), overwriteConflicts: true));
        Assert.Equal("20", session.Page.Find("a")!.Data["Progress"]);
        session.Undo(); Assert.Equal("15", session.Page.Find("a")!.Data["Progress"]);
    }
    [Fact] public void DeletedLocalPropertyIsNotConfusedWithMissingImportBaseline()
    {
        var session = Session(); Refresh(session);
        session.Execute("Delete local property", () => session.Page.Find("a")!.Data.Remove("Progress"));
        var unchanged = session.PreviewDataRefresh(Source()); Assert.Empty(unchanged.Issues);
        Assert.False(session.ApplyDataRefresh(unchanged));
        var changed = session.PreviewDataRefresh(Source("20"));
        Assert.Single(changed.Issues, i => i.Field == "Progress");
        Assert.False(session.ApplyDataRefresh(changed));
        Assert.False(session.Page.Find("a")!.Data.ContainsKey("Progress"));
    }
    [Fact] public void MissingRowAndRemovedColumnsRetainDataAndBaseline()
    {
        var session = Session(); Refresh(session);
        var missing = session.PreviewDataRefresh(CsvDataTable.Parse("Id,Progress\n", "Assets", "Id"));
        Assert.Single(missing.Issues); Assert.False(session.ApplyDataRefresh(missing));
        var removed = session.PreviewDataRefresh(CsvDataTable.Parse("Id\n0001", "Assets", "Id"));
        Assert.Equal(2, removed.TotalIssues); session.ApplyDataRefresh(removed);
        Assert.Equal("10", session.Page.Find("a")!.Data["Progress"]);
        Refresh(session, "20"); Assert.Equal("20", session.Page.Find("a")!.Data["Progress"]);
    }
    [Fact] public void DataKeyPropertyEditsDoNotRebindTheSourceIdentity()
    {
        var session = Session(); Refresh(session);
        session.Execute("Local key label", () => session.Page.Find("a")!.Data["Id"] = "9999");
        Refresh(session, "20");
        Assert.Equal("9999", session.Page.Find("a")!.Data["Id"]);
        Assert.Equal("0001", session.Page.Find("a")!.DataBinding!.RowKey);
        Assert.Equal("20", session.Page.Find("a")!.Data["Progress"]);
    }
    [Fact] public void InitialLocalConflictIsRetainedUntilExplicitOverwrite()
    {
        var session = Session(); session.Page.Find("a")!.Data["Progress"] = "Local";
        var plan = session.PreviewDataRefresh(Source()); Assert.Single(plan.Issues);
        session.ApplyDataRefresh(plan); Assert.Equal("Local", session.Page.Find("a")!.Data["Progress"]);
        Refresh(session, "20"); Assert.Equal("Local", session.Page.Find("a")!.Data["Progress"]);
        session.ApplyDataRefresh(session.PreviewDataRefresh(Source("20"), overwriteConflicts: true));
        Assert.Equal("20", session.Page.Find("a")!.Data["Progress"]);
    }
    [Fact] public void ForeignRecordsetsAndLockedShapesAreNotOverwritten()
    {
        var session = Session(); Refresh(session);
        var other = CsvDataTable.Parse("Id,Progress\n0001,99", "Other", "Id");
        Assert.False(session.ApplyDataRefresh(session.PreviewDataRefresh(other)));
        session.Execute("Lock", () => session.Page.Find("a")!.Locked = true);
        var locked = session.PreviewDataRefresh(Source("99")); Assert.Single(locked.Issues);
        Assert.False(session.ApplyDataRefresh(locked)); Assert.Equal("10", session.Page.Find("a")!.Data["Progress"]);
    }
    [Fact] public void MatchingExistingPropertyAndSelectedOnlyScopeWork()
    {
        var session = Session(); session.Page.Find("a")!.Data["AssetId"] = "0001";
        session.AddShape(new() { Id = "b", Text = "0001" }); session.Select("a");
        var plan = session.PreviewDataRefresh(Source(), "AssetId", selectedOnly: true);
        session.ApplyDataRefresh(plan);
        Assert.Equal("10", session.Page.Find("a")!.Data["Progress"]); Assert.Empty(session.Page.Find("b")!.Data);
    }
    [Theory] [InlineData("edit")] [InlineData("name")] [InlineData("text")] [InlineData("lock")]
    [InlineData("data")] [InlineData("selection")] [InlineData("add")]
    public void StalePreviewCannotPartiallyMutateTheDrawing(string change)
    {
        var session = Session(); session.Select("a");
        var plan = session.PreviewDataRefresh(Source(), selectedOnly: true); var shape = session.Page.Find("a")!;
        switch (change)
        {
            case "edit": session.SetText("a", "Different"); break;
            case "name": shape.Name = "Unnotified"; break;
            case "text": shape.Text = "Unnotified"; break;
            case "lock": shape.Locked = true; break;
            case "data": shape.Data["Owner"] = "Bob"; break;
            case "selection": session.Selection.Clear(); break;
            case "add": session.Page.Shapes.Add(new() { Id = "b" }); break;
        }
        Assert.Throws<InvalidOperationException>(() => session.ApplyDataRefresh(plan));
        Assert.False(shape.Data.ContainsKey("Progress"));
    }
    [Fact] public void UnchangedRefreshAndGraphicReapplyDoNotAddHistory()
    {
        var session = Session(); Refresh(session);
        var revision = session.Revision;
        Assert.False(session.ApplyDataRefresh(session.PreviewDataRefresh(Source()))); Assert.Equal(revision, session.Revision);
        session.Select("a"); var rule = new ShapeDataGraphic { Field = "Progress" };
        session.SetSelectedDataGraphic(rule); revision = session.Revision;
        session.SetSelectedDataGraphic(rule); Assert.Equal(revision, session.Revision);
    }
    [Fact] public void NativeJsonCloningUnlinkAndUndoRetainIndependentBaseline()
    {
        var session = Session(); Refresh(session); session.Select("a");
        session.SetSelectedDataGraphic(new() { Kind = DataGraphicKind.DataBar, Field = "Progress" });
        var shape = DocumentCodec.Clone(session.Document).Pages[0].Shapes[0]; var copy = shape.Clone(true);
        copy.DataBinding!.Baseline["Progress"] = "99"; copy.DataGraphics.Clear();
        Assert.Equal("10", shape.DataBinding!.Baseline["Progress"]); Assert.Single(shape.DataGraphics);
        session.UnlinkSelectedData(); Assert.Null(session.Page.Find("a")!.DataBinding);
        Assert.Equal("10", session.Page.Find("a")!.Data["Progress"]);
        session.Undo(); Assert.NotNull(session.Page.Find("a")!.DataBinding);
    }
    [Theory] [InlineData(-1,0)] [InlineData(0,0)] [InlineData(33,0)] [InlineData(50,1)]
    [InlineData(99,2)] [InlineData(101,2)]
    public void NumericBandsAreBounded(double value, int expected)
    {
        var shape = new Shape(); shape.Data["V"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rule = new ShapeDataGraphic { Field = "V" };
        Assert.True(rule.TryFraction(shape, out var fraction)); Assert.Equal(expected, rule.Band(fraction));
        Assert.Equal(2 - expected, (rule with { LowerIsBetter = true }).Band(fraction));
    }
    [Theory] [InlineData("NaN")] [InlineData("Infinity")] [InlineData("text")] [InlineData("")]
    public void NonnumericDataKeepsBaseFillAndSuppressesNumericOverlays(string text)
    {
        var shape = new Shape(); shape.Data["V"] = text;
        foreach (var kind in new[] { DataGraphicKind.ColorByValue, DataGraphicKind.DataBar, DataGraphicKind.IconSet })
            shape.DataGraphics.Add(new() { Field = "V", Kind = kind });
        Assert.Equal(shape.Style.Fill, DataGraphicProjection.Fill(shape)); Assert.Empty(DataGraphicProjection.Create(shape));
    }
    [Fact] public void AllGraphicFamiliesExportWithoutModifyingBaseStyleOrModel()
    {
        var session = Session(); Refresh(session); session.Select("a");
        foreach (var kind in Enum.GetValues<DataGraphicKind>())
            session.SetSelectedDataGraphic(new() { Kind = kind, Field = "Progress" });
        var before = DocumentCodec.Save(session.Document); var shape = session.Page.Find("a")!;
        using var renderer = new SceneRenderer();
        var svg = XDocument.Parse(renderer.ExportSvg(session.Document, session.Page));
        Assert.Contains(svg.Descendants(), node => (string?)node.Attribute("data-graphics-for") == "a");
        Assert.Contains(svg.Descendants(), node => (string?)node.Attribute("fill") == "#D13438");
        Assert.Contains(svg.Descendants(), node => (string?)node.Attribute("aria-label") == "Progress: 10");
        Assert.NotEmpty(renderer.ExportPng(session.Document, session.Page, .5));
        Assert.NotEmpty(renderer.ExportPdf(session.Document));
        Assert.Equal(before, DocumentCodec.Save(session.Document));
        session.ClearSelectedDataGraphics(); Assert.Empty(shape.DataGraphics); Assert.Equal("#FFFFFF", shape.Style.Fill);
        session.Undo(); Assert.Equal(4, session.Page.Find("a")!.DataGraphics.Count);
    }
    [Fact] public void GraphicBoundsFollowReflectionRotationShearAndIncludeOverhang()
    {
        var shape = new Shape { X = 100, Y = 200, Width = 120, Height = 70, Rotation = 27, ShearX = .7, FlipX = true };
        shape.Data["V"] = "50";
        shape.DataGraphics.Add(new() { Kind = DataGraphicKind.DataBar, Field = "V", Bounds = new(1.2, .3, .5, .3) });
        var matrix = shape.WorldMatrix * MatrixD.Scale(1 / shape.Width, 1 / shape.Height);
        var point = matrix.Map(new PointD(1.5 * shape.Width, .4 * shape.Height));
        Assert.True(DataGraphicProjection.WorldBounds(shape).Contains(point));
    }
    [Fact] public void OffscreenOwnerStillDrawsItsVisibleDataGraphicWithSpatialIndex()
    {
        var page = new DiagramPage();
        for (var i = 0; i < 129; i++) page.Shapes.Add(new() { X = 1000 + i * 4, Y = 1000, Text = "" });
        var shape = new Shape { Id = "owner", X = 400, Y = 400, Width = 100, Height = 50, Text = "" };
        shape.Data["V"] = "50"; shape.DataGraphics.Add(new() { Kind = DataGraphicKind.IconSet, Field = "V", Bounds = new(2, 0, .5, 1) });
        page.Shapes.Add(shape);
        using var renderer = new SceneRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(800, 600));
        surface.Canvas.Clear(SKColors.White);
        renderer.DrawPage(surface.Canvas, page, 1, visible: new RectD(600, 400, 50, 50), drawBackground: false);
        using var image = surface.Snapshot(); using var pixels = SKBitmap.FromImage(image);
        Assert.Equal(SKColor.Parse("#F2B134"), pixels.GetPixel(615, 425));
    }
    [Fact] public void GraphicCacheIgnoresTranslationButDetectsMutableDataAndRuleChanges()
    {
        var shape = new Shape { Text = "" }; shape.Data["V"] = "20";
        shape.DataGraphics.Add(new() { Kind = DataGraphicKind.DataBar, Field = "V" });
        using var renderer = new SceneRenderer(); using var surface = SKSurface.Create(new SKImageInfo(500, 400));
        renderer.DrawShape(surface.Canvas, shape); var count = renderer.DataGraphicCacheMisses;
        shape.X += 10; renderer.DrawShape(surface.Canvas, shape); Assert.Equal(count, renderer.DataGraphicCacheMisses);
        shape.Data["V"] = "99"; renderer.DrawShape(surface.Canvas, shape); Assert.Equal(count + 1, renderer.DataGraphicCacheMisses);
        shape.DataGraphics[0] = shape.DataGraphics[0] with { HighColor = "#0000FF" };
        renderer.DrawShape(surface.Canvas, shape); Assert.Equal(count + 2, renderer.DataGraphicCacheMisses);
    }
    [Fact] public void InvalidGraphicIsRolledBackAndLinkedBooleanReplacementIsRejected()
    {
        var session = Session(); Refresh(session); session.Select("a");
        var before = DocumentCodec.Save(session.Document);
        Assert.Throws<InvalidDataException>(() => session.SetSelectedDataGraphic(new() { Field = "V", Minimum = 10, Maximum = 0 }));
        Assert.Equal(before, DocumentCodec.Save(session.Document));
        session.AddShape(new() { Id = "b" });
        Assert.Throws<InvalidOperationException>(() => session.GetGeometryOperands(new[] { "a", "b" }));
    }
    [Fact] public void VisioExportReportsMaterializedDataBoundaryAndPreservesValueAndFill()
    {
        var session = Session(); Refresh(session); session.Select("a");
        session.SetSelectedDataGraphic(new() { Field = "Progress" });
        var written = VisioWriter.Write(session.Document, new() { IncludeDrawingSpaceMetadata = false, PreserveOriginalWhenUnmodified = false });
        var read = VisioReader.Read(written.Bytes);
        Assert.Equal("10", read.Document.Pages[0].Shapes[0].Data["Progress"]);
        Assert.Equal("#D13438", read.Document.Pages[0].Shapes[0].Style.Fill);
    }
}
