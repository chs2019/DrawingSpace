using System.Text.Json.Serialization;

namespace DrawingSpace.Documents;

[JsonSourceGenerationOptions(WriteIndented = true, IgnoreReadOnlyProperties = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiagramDocument))]
[JsonSerializable(typeof(DiagramPage))]
[JsonSerializable(typeof(DiagramMaster))]
[JsonSerializable(typeof(DiagramGroup))]
[JsonSerializable(typeof(DiagramLayer))]
[JsonSerializable(typeof(Shape))]
[JsonSerializable(typeof(Connector))]
[JsonSerializable(typeof(ShapeCell))]
[JsonSerializable(typeof(CommentThread))]
[JsonSerializable(typeof(CommentMessage))]
public partial class DocumentJsonContext : JsonSerializerContext { }
