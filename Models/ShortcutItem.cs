using System.Windows.Media;

namespace PrismPane_Widgets.Models;

/// <summary>
/// A single shortcut entry inside a Shortcut Panel widget.
/// </summary>
public class ShortcutItem
{
    public string Name { get; set; } = "New Shortcut";
    public string TargetPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// Optional path to a custom icon file. When empty the shell icon for
    /// <see cref="TargetPath"/> is used.
    /// </summary>
    public string CustomIconPath { get; set; } = string.Empty;

    // ── Runtime-only (not serialized) ───────────────────────
    [System.Text.Json.Serialization.JsonIgnore]
    public ImageSource? IconImage { get; set; }
}
