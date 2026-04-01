namespace PrismPane_Widgets.Models;

/// <summary>
/// Complete color scheme for the app or an individual widget.
/// All values stored as hex strings (e.g. "#FF1E1E2E").
/// </summary>
public class ThemeColors
{
    public string WindowBackground { get; set; } = "#FF1E1E2E";
    public string ControlBackground { get; set; } = "#FF2A2A3E";
    public string Foreground { get; set; } = "#FFFFFFFF";
    public string MutedForeground { get; set; } = "#FFAAAAAA";
    public string Border { get; set; } = "#FF555577";
    public string Accent { get; set; } = "#FF3A86FF";
    public string SecondaryButton { get; set; } = "#FF555577";
    public string DropdownBackground { get; set; } = "#FF2A2A3E";
    public string DropdownItemHover { get; set; } = "#FF3A3A50";

    public ThemeColors Clone() => (ThemeColors)MemberwiseClone();

    public static ThemeColors Dark => new();

    public static ThemeColors Light => new()
    {
        WindowBackground = "#FFF3F3F3",
        ControlBackground = "#FFFFFFFF",
        Foreground = "#FF1E1E1E",
        MutedForeground = "#FF666666",
        Border = "#FFCCCCCC",
        Accent = "#FF0067C0",
        SecondaryButton = "#FFCCCCCC",
        DropdownBackground = "#FFFFFFFF",
        DropdownItemHover = "#FFE5E5E5"
    };
}
