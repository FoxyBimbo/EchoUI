using System.Windows.Media;

namespace PrismPane_Widgets.Models;

public class PinnedApp
{
    public string Name { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }
}
