namespace DrawingSpace.Core;

public enum ShapeKind
{
    Rectangle, RoundedRectangle, Ellipse, Decision, Triangle, Document, Data, Cylinder,
    Hexagon, Cloud, Person, Server, Note, PredefinedProcess, Preparation, ManualInput,
    ManualOperation, Delay, Display, OffPage, Text, Container, Cross, Star, Arrow,
    Pentagon, Callout, Ring, Annotation
}

public enum PortSide { Auto, North, East, South, West }
public enum ConnectorKind { Orthogonal, Straight }
public enum ArrowHead { None, Open, Triangle, Diamond }
public enum EditorTool { Pointer, Connector, Text, Rectangle, Ellipse, Pan }
public enum Alignment { Left, Center, Right, Top, Middle, Bottom }
public enum ChangeKind { Document, Preview, Selection, Viewport, Tool }
