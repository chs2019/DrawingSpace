using DrawingSpace.Core;

namespace DrawingSpace.Editing;

/// <summary>An exact source row key and the drawing-space center of its new shape.</summary>
public readonly record struct DataRowPlacement(string RowKey, PointD Center);
