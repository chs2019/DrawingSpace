namespace DrawingSpace.Documents;

/// <summary>Navigation data only. Importers and renderers never automatically open a hyperlink.</summary>
public sealed record ShapeHyperlink(string Description, string Address, string SubAddress = "");
