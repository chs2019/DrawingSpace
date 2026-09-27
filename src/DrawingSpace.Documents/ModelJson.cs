using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DrawingSpace.Documents;

/// <summary>Source-generated serialization for document entities; safe for trimming and WebAssembly.</summary>
public static class ModelJson
{
    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, Metadata<T>());
    public static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize(json, Metadata<T>()) ?? throw new InvalidDataException("The entity is null.");
    private static JsonTypeInfo<T> Metadata<T>() => DocumentJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new NotSupportedException("The type is not part of the DrawingSpace document contract: " + typeof(T).FullName);
}
