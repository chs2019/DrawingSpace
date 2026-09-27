namespace DrawingSpace.Documents;

/// <summary>Original bytes enable no-edit byte-identical round trips and preservation of unknown OPC parts.</summary>
public sealed class VisioPackageOrigin
{
    public string Format { get; set; } = "vsdx";
    public byte[] Package { get; set; } = [];
    public string ModelFingerprint { get; set; } = "";
    public List<string> Diagnostics { get; set; } = [];
}
