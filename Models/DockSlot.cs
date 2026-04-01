namespace PrismPane_Widgets.Models;

public enum DockEdge
{
    None,
    Left,
    Right,
    Top,
    Bottom
}

public class DockSlot
{
    public string WidgetId { get; set; } = string.Empty;
    public DockEdge Edge { get; set; }
    public int ThicknessPx { get; set; }
}
