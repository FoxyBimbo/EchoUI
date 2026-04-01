using System.IO;
using PrismPane_Widgets.Models;

namespace PrismPane_Widgets.Services;

public class ExtensionManager
{
    private static readonly HashSet<string> RemovedWidgetNames =
    [
        "Full Screen Shell",
        "FullscreenShell",
        "FullScreenShell"
    ];

    private readonly HashSet<string> _enabledExtensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _respectEnabledList;
    private static readonly string ExtensionsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismPane_Widgets", "Extensions");

    private static readonly string WidgetsDir = Path.Combine(ExtensionsDir, "Widgets");

    public string WidgetsFolderPath => WidgetsDir;

    public List<ExtensionInfo> Extensions { get; } = [];

    public ExtensionManager(AppSettings? settings = null)
    {
        if (settings is not null)
        {
            _respectEnabledList = settings.HasConfiguredExtensions;
            foreach (var name in settings.EnabledExtensions)
                _enabledExtensions.Add(name);
        }

        Directory.CreateDirectory(WidgetsDir);
        EnsureSampleExtensions();
        Scan();
    }

    public void Scan()
    {
        Extensions.Clear();
        ScanDirectory(WidgetsDir);
        ApplyEnabledFlags();
    }

    private void ScanDirectory(string dir)
    {
        foreach (var file in Directory.GetFiles(dir, "*.js").Concat(Directory.GetFiles(dir, "*.lua")))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (RemovedWidgetNames.Contains(name))
                continue;

            var ext = Path.GetExtension(file).ToLowerInvariant();
            Extensions.Add(new ExtensionInfo
            {
                Name = name,
                FilePath = file,
                ScriptType = ext == ".lua" ? ScriptType.Lua : ScriptType.JavaScript,
                Kind = ExtensionKind.Widget,
                IsEnabled = true,
                Description = "Widget script"
            });
        }
    }

    public void UpdateEnabledExtensions(IEnumerable<string> enabledNames)
    {
        _enabledExtensions.Clear();
        foreach (var name in enabledNames)
            _enabledExtensions.Add(name);

        ApplyEnabledFlags();
    }

    private void ApplyEnabledFlags()
    {
        if (!_respectEnabledList)
            return;

        foreach (var ext in Extensions)
            ext.IsEnabled = _enabledExtensions.Contains(ext.Name);
    }

    public void ImportWidget(string sourcePath)
    {
        var destPath = Path.Combine(WidgetsDir, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destPath, overwrite: true);
        Scan();
    }

    public List<ExtensionInfo> GetWidgets() =>
        Extensions.Where(e => e.Kind == ExtensionKind.Widget && e.IsEnabled).ToList();

    public string ReadScript(ExtensionInfo ext) => File.ReadAllText(ext.FilePath);

    private void EnsureSampleExtensions()
    {
        var legacyDesktopFolder = Path.Combine(WidgetsDir, "DesktopFolder.js");
        var sampleFolder = Path.Combine(WidgetsDir, "Folder.js");
        if (File.Exists(legacyDesktopFolder) && !File.Exists(sampleFolder))
            File.Move(legacyDesktopFolder, sampleFolder);

        if (!File.Exists(sampleFolder))
        {
            File.WriteAllText(sampleFolder, """
                // Folder widget – built-in, handled natively.
                // This marker file tells PrismPane Widgets to show the Folder widget.
                echo.notify("Folder", "Folder widget is active.");
                """);
        }

        var sampleShortcutPanel = Path.Combine(WidgetsDir, "ShortcutPanel.js");
        if (!File.Exists(sampleShortcutPanel))
        {
            File.WriteAllText(sampleShortcutPanel, """
                // ShortcutPanel widget – built-in, handled natively.
                // This marker file tells PrismPane Widgets to show the Shortcut Panel widget.
                echo.notify("ShortcutPanel", "Shortcut panel widget is active.");
                """);
        }

        var sampleTitleBar = Path.Combine(WidgetsDir, "TitleBar.js");
        if (!File.Exists(sampleTitleBar))
        {
            File.WriteAllText(sampleTitleBar, """
                // TitleBar widget – built-in, handled natively.
                // This marker file tells PrismPane Widgets to show the TitleBar widget.
                echo.notify("TitleBar", "Title bar widget is active.");
                """);
        }

    }
}
