namespace DrawingSpace.Documents;

public enum ContainerLayout { Free, HorizontalLanes, VerticalLanes }
public sealed class ContainerOptions
{
    public double Padding { get; set; } = 20;
    public double HeaderHeight { get; set; } = 32;
    public bool AutoResize { get; set; } = true;
    public bool LockedMembership { get; set; }
    public ContainerLayout Layout { get; set; }
    public ContainerOptions Clone() => (ContainerOptions)MemberwiseClone();
}
