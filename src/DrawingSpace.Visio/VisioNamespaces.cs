using System.Xml.Linq;

namespace DrawingSpace.Visio;

internal static class VisioNamespaces
{
    internal static readonly XNamespace Main = "http://schemas.microsoft.com/office/visio/2012/main";
    internal static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    internal static readonly XNamespace RelationshipReference = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    internal static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    internal static readonly XNamespace DrawingSpace = "https://wieslawsoltes.github.io/DrawingSpace/schema/2026";
    internal const string VisioRelationship = "http://schemas.microsoft.com/visio/2010/relationships/";
    internal const string OfficeRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
}
