using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrismPane_Widgets.Models;

public class AppSettings
{
    public bool AutoHideTaskbar { get; set; }
    public bool ShowSeconds { get; set; }
    public bool Use24HourClock { get; set; }
    public string TaskbarPosition { get; set; } = "Bottom";
    public double TaskbarOpacity { get; set; } = 0.95;
    public string AccentColor { get; set; } = "#FF3A86FF";
    public List<string> PinnedAppPaths { get; set; } = [];
    public List<string> EnabledExtensions { get; set; } = [];
    public bool HasConfiguredExtensions { get; set; }

    /// <summary>Theme mode: "Dark", "Light", "Auto", or "Custom".</summary>
    public string ThemeMode { get; set; } = "Auto";

    /// <summary>Custom color scheme used when ThemeMode is "Custom".</summary>
    public ThemeColors? CustomTheme { get; set; }

    /// <summary>Per-widget settings keyed by widget id.</summary>
    public Dictionary<string, WidgetSettings> Widgets { get; set; } = [];

    /// <summary>Persisted widget group (tab container) definitions.</summary>
    public List<WidgetGroupSettings> WidgetGroups { get; set; } = [];

    /// <summary>
    /// Returns settings for a widget, creating defaults if missing.
    /// </summary>
    public WidgetSettings GetWidgetSettings(string widgetId)
    {
        if (Widgets.TryGetValue(widgetId, out var ws))
            return ws;

        ws = GetDefaultWidgetSettings(widgetId);
        Widgets[widgetId] = ws;
        return ws;
    }

    /// <summary>
    /// Creates a new widget instance with a unique ID and returns (id, settings).
    /// </summary>
    public (string Id, WidgetSettings Settings) CreateWidgetInstance(string kind)
    {
        int counter = 1;
        string id;
        do { id = $"{kind}_{counter++}"; }
        while (Widgets.ContainsKey(id));

        var ws = GetDefaultWidgetSettings(kind);
        ws.Kind = kind;
        Widgets[id] = ws;
        return (id, ws);
    }

    /// <summary>
    /// Removes a widget instance from saved settings.
    /// </summary>
    public void RemoveWidgetInstance(string widgetId) => Widgets.Remove(widgetId);

    private static WidgetSettings GetDefaultWidgetSettings(string widgetId)
    {
        var kind = widgetId.Contains('_') ? widgetId[..widgetId.LastIndexOf('_')] : widgetId;
        return kind switch
        {
            "Folder" or "DesktopFolder" => new WidgetSettings { Kind = "Folder", Topmost = false, Opacity = 0.85 },
            "ShortcutPanel" => new WidgetSettings
            {
                Kind = "ShortcutPanel",
                Topmost = false,
                Opacity = 0.85,
                Custom = new() { ["Title"] = "Shortcuts" }
            },
            "TitleBar" => new WidgetSettings
            {
                Kind = "TitleBar",
                Topmost = false,
                Opacity = 0.85,
                Height = 36,
                DockEdge = DockEdge.Top,
                DockThickness = 36
            },
            "Clock" => new WidgetSettings
            {
                Kind = "Clock",
                Topmost = false,
                Opacity = 0.85,
                Width = 220,
                Height = 220,
                Custom = new()
                {
                    ["ClockMode"] = "Digital",
                    ["ClockHourColor"] = "#FFE5E7EB",
                    ["ClockMinuteColor"] = "#FF93C5FD",
                    ["ClockSecondColor"] = "#FFFCA5A5"
                }
            },
            "CpuMonitor" => new WidgetSettings
            {
                Kind = "CpuMonitor",
                Topmost = false,
                Opacity = 0.85,
                Width = 260,
                Height = 220,
                Custom = new()
                {
                    ["CpuViewMode"] = "Bar",
                    ["CpuLowColor"] = "#FF34D399",
                    ["CpuMediumColor"] = "#FFFBBF24",
                    ["CpuHighColor"] = "#FFF87171"
                }
            },
            "RamMonitor" => new WidgetSettings
            {
                Kind = "RamMonitor",
                Topmost = false,
                Opacity = 0.85,
                Width = 260,
                Height = 220,
                Custom = new()
                {
                    ["RamViewMode"] = "Bar",
                    ["RamLowColor"] = "#FF34D399",
                    ["RamMediumColor"] = "#FFFBBF24",
                    ["RamHighColor"] = "#FFF87171"
                }
            },
            "Weather" => new WidgetSettings
            {
                Kind = "Weather",
                Topmost = false,
                Opacity = 0.85,
                Width = 280,
                Height = 260,
                Custom = new()
                {
                    ["WeatherLocation"] = "New York City, New York, USA",
                    ["WeatherLatitude"] = "40.7128",
                    ["WeatherLongitude"] = "-74.0060",
                    ["WeatherForecastDays"] = "1",
                    ["WeatherTemperatureUnit"] = "celsius",
                    ["WeatherFreezeColor"] = "#FF60A5FA",
                    ["WeatherCoolColor"] = "#FF22D3EE",
                    ["WeatherWarmColor"] = "#FFFACC15",
                    ["WeatherHotColor"] = "#FFFB923C",
                    ["WeatherExtremeColor"] = "#FFEF4444"
                }
            },
            "VideoBackground" => new WidgetSettings
            {
                Kind = "VideoBackground",
                Topmost = false,
                Opacity = 0.85,
                Width = 360,
                Height = 240,
                Custom = new()
                {
                    ["VideoBackgroundSourcePath"] = string.Empty
                }
            },
            "MediaControl" => new WidgetSettings
            {
                Kind = "MediaControl",
                Topmost = false,
                Opacity = 0.85,
                Width = 320,
                Height = 44
            },
            "NetworkTraffic" => new WidgetSettings
            {
                Kind = "NetworkTraffic",
                Topmost = false,
                Opacity = 0.85,
                Width = 260,
                Height = 220,
                Custom = new()
                {
                    ["NetDownloadColor"] = "#FF34D399",
                    ["NetUploadColor"] = "#FF60A5FA"
                }
            },
            "DiskUsage" => new WidgetSettings
            {
                Kind = "DiskUsage",
                Topmost = false,
                Opacity = 0.85,
                Width = 260,
                Height = 220,
                Custom = new()
                {
                    ["DiskDrive"] = "C",
                    ["DiskLowColor"] = "#FF34D399",
                    ["DiskMediumColor"] = "#FFFBBF24",
                    ["DiskHighColor"] = "#FFF87171"
                }
            },
            "GpuMonitor" => new WidgetSettings
            {
                Kind = "GpuMonitor",
                Topmost = false,
                Opacity = 0.85,
                Width = 260,
                Height = 220,
                Custom = new()
                {
                    ["GpuLowColor"] = "#FF34D399",
                    ["GpuMediumColor"] = "#FFFBBF24",
                    ["GpuHighColor"] = "#FFF87171"
                }
            },
            "StickyNotes" => new WidgetSettings
            {
                Kind = "StickyNotes",
                Topmost = false,
                Opacity = 0.85,
                Width = 260,
                Height = 280,
                Custom = new()
                {
                    ["NoteContent"] = string.Empty,
                    ["NoteFontFamily"] = "Segoe UI",
                    ["NoteFontSize"] = "14",
                    ["NoteFontColor"] = string.Empty
                }
            },
            "Slideshow" => new WidgetSettings
            {
                Kind = "Slideshow",
                Topmost = false,
                Opacity = 0.85,
                Width = 360,
                Height = 280,
                Custom = new()
                {
                    ["SlideshowFolder"] = string.Empty,
                    ["SlideshowInterval"] = "5",
                    ["SlideshowRandom"] = "false"
                }
            },
            "RssFeed" => new WidgetSettings
            {
                Kind = "RssFeed",
                Topmost = false,
                Opacity = 0.85,
                Width = 300,
                Height = 320,
                Custom = new()
                {
                    ["RssFeedUrl"] = "https://feeds.bbci.co.uk/news/rss.xml",
                    ["RssMaxItems"] = "15",
                    ["RssRefreshInterval"] = "3",
                    ["RssRefreshUnit"] = "Hours"
                }
            },
            _ => new WidgetSettings { Kind = kind }
        };
    }

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismPane Widgets");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}
