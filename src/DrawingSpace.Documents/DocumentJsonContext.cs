using System.Text.Json.Serialization;

namespace DrawingSpace.Documents;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiagramDocument))]
internal partial class DocumentJsonContext : JsonSerializerContext { }
