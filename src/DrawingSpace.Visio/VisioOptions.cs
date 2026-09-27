using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

public enum VisioPackageKind { Drawing, Stencil, Template }
public enum VisioDiagnosticSeverity { Information, Warning, Error }
public sealed record VisioDiagnostic(VisioDiagnosticSeverity Severity, string Code, string Part, string Message, uint? ShapeId = null);
public sealed record VisioReadResult(DiagramDocument Document, IReadOnlyList<VisioDiagnostic> Diagnostics);
public sealed record VisioWriteResult(byte[] Bytes, IReadOnlyList<VisioDiagnostic> Diagnostics, bool OriginalBytesPreserved);

public sealed class VisioReadOptions
{
    public int MaximumInputBytes { get; init; } = DocumentCodec.MaximumPackageBytes;
    public long MaximumExpandedBytes { get; init; } = 128 * 1024 * 1024;
    public int MaximumPartBytes { get; init; } = 32 * 1024 * 1024;
    public int MaximumParts { get; init; } = 4096;
    public int MaximumShapes { get; init; } = 50000;
    public int MaximumGroupDepth { get; init; } = 32;
    public bool PreserveOriginalPackage { get; init; } = true;
    public CancellationToken CancellationToken { get; init; }
}

public sealed class VisioWriteOptions
{
    public VisioPackageKind Kind { get; init; }
    public bool PreserveUnknownParts { get; init; } = true;
    public bool PreserveOriginalWhenUnmodified { get; init; } = true;
    public bool IncludeDrawingSpaceMetadata { get; init; } = true;
    public CancellationToken CancellationToken { get; init; }
}
