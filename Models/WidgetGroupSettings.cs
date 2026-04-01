namespace PrismPane_Widgets.Models;

/// <summary>
/// Persisted state for a widget group (tab container).
/// </summary>
public class WidgetGroupSettings
{
    /// <summary>Unique identifier for this group.</summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>Widget IDs that belong to this group, in tab order.</summary>
    public List<string> WidgetIds { get; set; } = [];

    /// <summary>Index of the active tab when the group was last saved.</summary>
    public int ActiveTab { get; set; }

    /// <summary>Last known screen position and size.</summary>
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}
