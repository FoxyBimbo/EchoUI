namespace PrismPane_Widgets.Models;

public enum ScriptType
{
    JavaScript,
    Lua
}

public enum ExtensionKind
{
    Plugin,
    Widget
}

public class ExtensionInfo
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public ScriptType ScriptType { get; set; }
    public ExtensionKind Kind { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
}
