using System.Xml;

namespace DrawingSpace.Documents;

/// <summary>Caps nesting before a selected XML subtree is materialized. Subtree readers continue through this wrapper.</summary>
internal sealed class XlsxXmlReader(XmlReader inner) : XmlReader
{
    public override bool Read()
    {
        var result = inner.Read();
        if (result && inner.Depth > 64) throw new XmlException("Workbook XML exceeds the 64-level nesting limit.");
        return result;
    }

    public override int AttributeCount => inner.AttributeCount;
    public override string BaseURI => inner.BaseURI;
    public override int Depth => inner.Depth;
    public override bool EOF => inner.EOF;
    public override bool HasValue => inner.HasValue;
    public override bool IsEmptyElement => inner.IsEmptyElement;
    public override string LocalName => inner.LocalName;
    public override XmlNameTable NameTable => inner.NameTable;
    public override string NamespaceURI => inner.NamespaceURI;
    public override XmlNodeType NodeType => inner.NodeType;
    public override string Prefix => inner.Prefix;
    public override ReadState ReadState => inner.ReadState;
    public override string Value => inner.Value;
    public override string this[int i] => inner[i];
    public override string? this[string name] => inner[name];
    public override string? this[string name, string? namespaceURI] => inner[name, namespaceURI];
    public override string GetAttribute(int i) => inner.GetAttribute(i);
    public override string? GetAttribute(string name) => inner.GetAttribute(name);
    public override string? GetAttribute(string name, string? namespaceURI) => inner.GetAttribute(name, namespaceURI);
    public override string? LookupNamespace(string prefix) => inner.LookupNamespace(prefix);
    public override void MoveToAttribute(int i) => inner.MoveToAttribute(i);
    public override bool MoveToAttribute(string name) => inner.MoveToAttribute(name);
    public override bool MoveToAttribute(string name, string? ns) => inner.MoveToAttribute(name, ns);
    public override bool MoveToElement() => inner.MoveToElement();
    public override bool MoveToFirstAttribute() => inner.MoveToFirstAttribute();
    public override bool MoveToNextAttribute() => inner.MoveToNextAttribute();
    public override bool ReadAttributeValue() => inner.ReadAttributeValue();
    public override void ResolveEntity() => inner.ResolveEntity();
    public override void Close() => inner.Close();
}
