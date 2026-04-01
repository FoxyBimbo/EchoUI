namespace PrismPane_Widgets.Models;

/// <summary>
/// Per-widget settings with generic properties shared by all widgets
/// and a dictionary for widget-specific custom values.
/// </summary>
public class WidgetSettings
{
    /// <summary>Widget kind — used to create the correct window type.</summary>
    public string Kind { get; set; } = "Folder";

    /// <summary>User-customisable display name shown in headers and group tabs.</summary>
    public string? Title { get; set; }

    /// <summary>Window opacity (0.0 – 1.0).</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Whether the widget border is visible.</summary>
    public bool ShowBorder { get; set; } = true;

    /// <summary>Whether the widget stays on top of other applications.</summary>
    public bool Topmost { get; set; } = true;

    /// <summary>Whether the widget should be reopened on app start.</summary>
    public bool IsOpen { get; set; }

    /// <summary>Last known screen position and size.</summary>
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    /// <summary>Last known docked edge and thickness.</summary>
    public DockEdge DockEdge { get; set; } = DockEdge.None;
    public double? DockThickness { get; set; }

    /// <summary>Widget-specific key/value settings.</summary>
    public Dictionary<string, string> Custom { get; set; } = [];

    /// <summary>Shortcut entries for ShortcutPanel widgets.</summary>
    public List<ShortcutItem> Shortcuts { get; set; } = [];

    /// <summary>View mode for ShortcutPanel widgets: "List" or "Grid".</summary>
    public string ViewMode { get; set; } = "List";

    /// <summary>
    /// Optional per-widget custom color override. When non-null the widget
    /// uses these colors instead of the global theme.
    /// </summary>
    public ThemeColors? CustomColors { get; set; }

    /// <summary>Last active folder for folder widgets.</summary>
    public string? ActiveFolder { get; set; }

    /// <summary>Whether a folder widget is minimized.</summary>
    public bool IsMinimized { get; set; }

    /// <summary>Expanded height for minimized widgets.</summary>
    public double? ExpandedHeight { get; set; }
}
