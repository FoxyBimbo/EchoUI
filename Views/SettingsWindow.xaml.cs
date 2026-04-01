using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;
using ColorDialog = System.Windows.Forms.ColorDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Button = System.Windows.Controls.Button;

namespace PrismPane_Widgets.Views;

public partial class SettingsWindow : Window
{
    private const string ClockModeKey = "ClockMode";
    private const string ClockHourColorKey = "ClockHourColor";
    private const string ClockMinuteColorKey = "ClockMinuteColor";
    private const string ClockSecondColorKey = "ClockSecondColor";
    private const string CpuViewModeKey = "CpuViewMode";
    private const string CpuLowColorKey = "CpuLowColor";
    private const string CpuMediumColorKey = "CpuMediumColor";
    private const string CpuHighColorKey = "CpuHighColor";
    private const string RamViewModeKey = "RamViewMode";
    private const string RamLowColorKey = "RamLowColor";
    private const string RamMediumColorKey = "RamMediumColor";
    private const string RamHighColorKey = "RamHighColor";
    private const string WeatherLocationKey = "WeatherLocation";
    private const string WeatherLatitudeKey = "WeatherLatitude";
    private const string WeatherLongitudeKey = "WeatherLongitude";
    private const string WeatherForecastDaysKey = "WeatherForecastDays";
    private const string WeatherTemperatureUnitKey = "WeatherTemperatureUnit";
    private const string WeatherFreezeColorKey = "WeatherFreezeColor";
    private const string WeatherCoolColorKey = "WeatherCoolColor";
    private const string WeatherWarmColorKey = "WeatherWarmColor";
    private const string WeatherHotColorKey = "WeatherHotColor";
    private const string WeatherExtremeColorKey = "WeatherExtremeColor";
    private const string VideoBackgroundSourcePathKey = "VideoBackgroundSourcePath";
    private const string NetDownloadColorKey = "NetDownloadColor";
    private const string NetUploadColorKey = "NetUploadColor";
    private const string DiskDriveKey = "DiskDrive";
    private const string DiskLowColorKey = "DiskLowColor";
    private const string DiskMediumColorKey = "DiskMediumColor";
    private const string DiskHighColorKey = "DiskHighColor";
    private const string GpuLowColorKey = "GpuLowColor";
    private const string GpuMediumColorKey = "GpuMediumColor";
    private const string GpuHighColorKey = "GpuHighColor";
    private const string NoteFontFamilyKey = "NoteFontFamily";
    private const string NoteFontSizeKey = "NoteFontSize";
    private const string NoteFontColorKey = "NoteFontColor";
    private const string SlideshowFolderKey = "SlideshowFolder";
    private const string SlideshowIntervalKey = "SlideshowInterval";
    private const string SlideshowRandomKey = "SlideshowRandom";
    private const string RssFeedUrlKey = "RssFeedUrl";
    private const string RssMaxItemsKey = "RssMaxItems";
    private const string RssRefreshIntervalKey = "RssRefreshInterval";
    private const string RssRefreshUnitKey = "RssRefreshUnit";

    private static readonly HttpClient GeocodeHttpClient = CreateGeocodeHttpClient();

    private readonly AppSettings _settings;
    private readonly ExtensionManager? _extManager;
    private readonly string? _widgetIdOverride;
    private readonly WidgetSettings? _widgetSettingsOverride;
    private readonly Action? _widgetSettingsApplied;
    private readonly Action<string>? _spawnWidget;
    private readonly Func<IReadOnlyList<ActiveWidgetListItem>>? _getActiveWidgets;
    private readonly Action<string>? _closeWidget;
    private readonly Action<string>? _openWidgetSettings;
    private readonly Action<string>? _resetWidgetLocation;
    private readonly Action? _settingsApplied;
    private bool _loading = true;

    public SettingsWindow(AppSettings settings, ExtensionManager? extManager, string? widgetIdOverride = null,
        WidgetSettings? widgetSettingsOverride = null, Action? widgetSettingsApplied = null,
        Action<string>? spawnWidget = null, Func<IReadOnlyList<ActiveWidgetListItem>>? getActiveWidgets = null,
        Action<string>? closeWidget = null, Action<string>? openWidgetSettings = null,
        Action<string>? resetWidgetLocation = null, Action? settingsApplied = null)
    {
        _settings = settings;
        _extManager = extManager;
        _widgetIdOverride = widgetIdOverride;
        _widgetSettingsOverride = widgetSettingsOverride;
        _widgetSettingsApplied = widgetSettingsApplied;
        _spawnWidget = spawnWidget;
        _getActiveWidgets = getActiveWidgets;
        _closeWidget = closeWidget;
        _openWidgetSettings = openWidgetSettings;
        _resetWidgetLocation = resetWidgetLocation;
        _settingsApplied = settingsApplied;
        InitializeComponent();
        Activated += (_, _) => RefreshActiveWidgets();
        LoadSettings();
    }

    private void LoadSettings()
    {
        _loading = true;
        TxtAccentColor.Text = _settings.AccentColor;
        SetSwatch(SwatchAccentColor, _settings.AccentColor);

        // Theme mode
        CmbThemeMode.SelectedIndex = _settings.ThemeMode switch
        {
            "Dark" => 1,
            "Light" => 2,
            "Custom" => 3,
            _ => 0 // Auto
        };
        LoadCustomThemeFields();

        ConfigureSectionVisibility();
        RefreshActiveWidgets();
        LoadWidgetTypes();
        if (_widgetIdOverride is not null)
        {
            LoadWidgetSettings(_widgetIdOverride);
        }
        else
        {
            LoadWidgetSettings("Folder");
        }
        _loading = false;
    }

    private void ConfigureSectionVisibility()
    {
        if (_widgetIdOverride is not null)
        {
            NavSidebar.Visibility = Visibility.Collapsed;
            SectionAppearance.Visibility = Visibility.Collapsed;
            SectionWidgets.Visibility = Visibility.Visible;
            PanelThemeSettings.Visibility = Visibility.Collapsed;
            PanelGeneralSettings.Visibility = Visibility.Collapsed;
            PanelWidgetSettings.Visibility = Visibility.Visible;
            PanelWidgetSelector.Visibility = Visibility.Collapsed;
            PanelAddWidgets.Visibility = Visibility.Collapsed;
            PanelActiveWidgets.Visibility = Visibility.Collapsed;
            return;
        }

        SectionAppearance.Visibility = Visibility.Visible;
        SectionWidgets.Visibility = Visibility.Collapsed;
        PanelWidgetSettings.Visibility = Visibility.Collapsed;
        PanelThemeSettings.Visibility = Visibility.Visible;
        PanelGeneralSettings.Visibility = Visibility.Visible;
        PanelAddWidgets.Visibility = _extManager is null ? Visibility.Collapsed : Visibility.Visible;
        PanelActiveWidgets.Visibility = _getActiveWidgets is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshActiveWidgets()
    {
        if (LstActiveWidgets is null)
            return;

        if (_widgetIdOverride is not null || _getActiveWidgets is null)
        {
            LstActiveWidgets.ItemsSource = null;
            TxtNoActiveWidgets.Visibility = Visibility.Collapsed;
            return;
        }

        var activeWidgets = _getActiveWidgets()
            .OrderBy(widget => widget.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        LstActiveWidgets.ItemsSource = activeWidgets;
        TxtNoActiveWidgets.Visibility = activeWidgets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (NavList.SelectedItem is not System.Windows.Controls.ListBoxItem item || item.Tag is not string tag) return;

        SectionAppearance.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        SectionWidgets.Visibility = tag == "Widgets" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadWidgetTypes()
    {
        if (_extManager is null)
        {
            LstWidgetTypes.ItemsSource = null;
            return;
        }

        var builtins = new[]
        {
            new ExtensionInfo { Name = "Folder", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Productivity" },
            new ExtensionInfo { Name = "ShortcutPanel", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Productivity" },
            new ExtensionInfo { Name = "TitleBar", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Productivity" },
            new ExtensionInfo { Name = "Clock", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Productivity" },
            new ExtensionInfo { Name = "CpuMonitor", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Monitoring" },
            new ExtensionInfo { Name = "RamMonitor", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Monitoring" },
            new ExtensionInfo { Name = "Weather", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Productivity" },
            new ExtensionInfo { Name = "Video Widget", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Media" },
            new ExtensionInfo { Name = "Media Control", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Media" },
            new ExtensionInfo { Name = "NetworkTraffic", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Monitoring" },
            new ExtensionInfo { Name = "DiskUsage", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Monitoring" },
            new ExtensionInfo { Name = "GpuMonitor", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Monitoring" },
            new ExtensionInfo { Name = "Sticky Notes", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Productivity" },
            new ExtensionInfo { Name = "Slideshow", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Media" },
            new ExtensionInfo { Name = "RSS Feed", Kind = ExtensionKind.Widget, IsEnabled = true, Category = "Productivity" }
        };

        var scripted = _extManager.Extensions
            .Where(e => e.Kind == ExtensionKind.Widget && e.IsEnabled)
            .ToList();

        var all = builtins
            .Concat(scripted)
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Name)
            .ToList();

        var categoryOrder = new[] { "Monitoring", "Media", "Productivity", "Other" };
        var grouped = all
            .GroupBy(e => e.Category)
            .OrderBy(g => Array.IndexOf(categoryOrder, g.Key) is var i && i < 0 ? int.MaxValue : i)
            .ToList();

        LstWidgetTypes.ItemsSource = grouped;
    }

    // ── Theme mode ──────────────────────────────────────────
    private void CmbThemeMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        PanelCustomTheme.Visibility = CmbThemeMode.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string SelectedThemeMode => CmbThemeMode.SelectedIndex switch
    {
        1 => "Dark",
        2 => "Light",
        3 => "Custom",
        _ => "Auto"
    };

    private void LoadCustomThemeFields()
    {
        var c = _settings.CustomTheme ?? ThemeColors.Dark;
        TxtCustomWindowBg.Text = c.WindowBackground;
        TxtCustomControlBg.Text = c.ControlBackground;
        TxtCustomForeground.Text = c.Foreground;
        TxtCustomMuted.Text = c.MutedForeground;
        TxtCustomBorder.Text = c.Border;
        TxtCustomAccent.Text = c.Accent;
        TxtCustomSecondary.Text = c.SecondaryButton;
        TxtCustomDropdownBg.Text = c.DropdownBackground;
        TxtCustomDropdownHover.Text = c.DropdownItemHover;
        UpdateSwatches();
        PanelCustomTheme.Visibility = _settings.ThemeMode == "Custom" ? Visibility.Visible : Visibility.Collapsed;
    }

    private ThemeColors ReadCustomThemeFields() => new()
    {
        WindowBackground = TxtCustomWindowBg.Text.Trim(),
        ControlBackground = TxtCustomControlBg.Text.Trim(),
        Foreground = TxtCustomForeground.Text.Trim(),
        MutedForeground = TxtCustomMuted.Text.Trim(),
        Border = TxtCustomBorder.Text.Trim(),
        Accent = TxtCustomAccent.Text.Trim(),
        SecondaryButton = TxtCustomSecondary.Text.Trim(),
        DropdownBackground = TxtCustomDropdownBg.Text.Trim(),
        DropdownItemHover = TxtCustomDropdownHover.Text.Trim()
    };

    private void UpdateSwatches()
    {
        SetSwatch(SwatchWindowBg, TxtCustomWindowBg.Text);
        SetSwatch(SwatchControlBg, TxtCustomControlBg.Text);
        SetSwatch(SwatchForeground, TxtCustomForeground.Text);
        SetSwatch(SwatchMuted, TxtCustomMuted.Text);
        SetSwatch(SwatchBorder, TxtCustomBorder.Text);
        SetSwatch(SwatchAccent, TxtCustomAccent.Text);
        SetSwatch(SwatchSecondary, TxtCustomSecondary.Text);
        SetSwatch(SwatchDropdownBg, TxtCustomDropdownBg.Text);
        SetSwatch(SwatchDropdownHover, TxtCustomDropdownHover.Text);

        SetSwatch(SwatchAccentColor, TxtAccentColor.Text);

        SetSwatch(SwatchWcWindowBg, TxtWcWindowBg.Text);
        SetSwatch(SwatchWcControlBg, TxtWcControlBg.Text);
        SetSwatch(SwatchWcForeground, TxtWcForeground.Text);
        SetSwatch(SwatchWcAccent, TxtWcAccent.Text);
        SetSwatch(SwatchWcBorder, TxtWcBorder.Text);
        SetSwatch(SwatchWcMuted, TxtWcMuted.Text);

        SetSwatch(SwatchClockHourColor, TxtClockHourColor.Text);
        SetSwatch(SwatchClockMinuteColor, TxtClockMinuteColor.Text);
        SetSwatch(SwatchClockSecondColor, TxtClockSecondColor.Text);

        SetSwatch(SwatchCpuLowColor, TxtCpuLowColor.Text);
        SetSwatch(SwatchCpuMediumColor, TxtCpuMediumColor.Text);
        SetSwatch(SwatchCpuHighColor, TxtCpuHighColor.Text);

        SetSwatch(SwatchRamLowColor, TxtRamLowColor.Text);
        SetSwatch(SwatchRamMediumColor, TxtRamMediumColor.Text);
        SetSwatch(SwatchRamHighColor, TxtRamHighColor.Text);

        SetSwatch(SwatchWeatherFreezeColor, TxtWeatherFreezeColor.Text);
        SetSwatch(SwatchWeatherCoolColor, TxtWeatherCoolColor.Text);
        SetSwatch(SwatchWeatherWarmColor, TxtWeatherWarmColor.Text);
        SetSwatch(SwatchWeatherHotColor, TxtWeatherHotColor.Text);
        SetSwatch(SwatchWeatherExtremeColor, TxtWeatherExtremeColor.Text);

        SetSwatch(SwatchNetDownloadColor, TxtNetDownloadColor.Text);
        SetSwatch(SwatchNetUploadColor, TxtNetUploadColor.Text);

        SetSwatch(SwatchDiskLowColor, TxtDiskLowColor.Text);
        SetSwatch(SwatchDiskMediumColor, TxtDiskMediumColor.Text);
        SetSwatch(SwatchDiskHighColor, TxtDiskHighColor.Text);

        SetSwatch(SwatchGpuLowColor, TxtGpuLowColor.Text);
        SetSwatch(SwatchGpuMediumColor, TxtGpuMediumColor.Text);
        SetSwatch(SwatchGpuHighColor, TxtGpuHighColor.Text);

        SetSwatch(SwatchStickyNoteFontColor, TxtStickyNoteFontColor.Text);
    }

    private static void SetSwatch(Border swatch, string hex)
    {
        try { swatch.Background = new SolidColorBrush(ThemeHelper.ParseColor(hex)); }
        catch { swatch.Background = System.Windows.Media.Brushes.Transparent; }
    }

    private void BtnPickColors_Click(object sender, RoutedEventArgs e)
    {
        var fields = new (System.Windows.Controls.TextBox Txt, string Label)[]
        {
            (TxtCustomWindowBg, "Window Background"),
            (TxtCustomControlBg, "Control Background"),
            (TxtCustomForeground, "Foreground"),
            (TxtCustomMuted, "Muted Text"),
            (TxtCustomBorder, "Border"),
            (TxtCustomAccent, "Accent"),
            (TxtCustomSecondary, "Secondary Button"),
            (TxtCustomDropdownBg, "Dropdown Background"),
            (TxtCustomDropdownHover, "Dropdown Hover"),
        };

        foreach (var (txt, label) in fields)
        {
            var dlg = new ColorDialog { FullOpen = true };
            try
            {
                var c = ThemeHelper.ParseColor(txt.Text.Trim());
                dlg.Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
            }
            catch { }

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var sc = dlg.Color;
                txt.Text = $"#{sc.A:X2}{sc.R:X2}{sc.G:X2}{sc.B:X2}";
            }
            else
            {
                break; // user cancelled — stop prompting
            }
        }
        UpdateSwatches();
    }

    private void BtnPickColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is System.Windows.Controls.TextBox txt)
        {
            if (PickColorForTextBox(txt))
                UpdateSwatches();
        }
    }

    private static bool PickColorForTextBox(System.Windows.Controls.TextBox txt)
    {
        var dlg = new ColorDialog { FullOpen = true };
        try
        {
            var c = ThemeHelper.ParseColor(txt.Text.Trim());
            dlg.Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
        }
        catch { }

        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return false;

        var sc = dlg.Color;
        txt.Text = $"#{sc.A:X2}{sc.R:X2}{sc.G:X2}{sc.B:X2}";
        return true;
    }

    // ── Widget import ───────────────────────────────────────
    private void BtnImportWidget_Click(object sender, RoutedEventArgs e)
    {
        if (_extManager is null)
            return;

        var dlg = new OpenFileDialog
        {
            Filter = "Script files (*.js;*.lua)|*.js;*.lua",
            Title = "Import Widget"
        };
        if (dlg.ShowDialog() == true)
        {
            _extManager.ImportWidget(dlg.FileName);
            LoadWidgetTypes();
        }
    }

    // ── Show folders ────────────────────────────────────────
    private void BtnShowWidgetFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_extManager.WidgetsFolderPath}\"") { UseShellExecute = false }); }
        catch { }
    }

    private static string NormalizeWidgetKind(string kind) =>
        kind == "DesktopFolder" ? "Folder" : kind;

    private static string BuildDefaultWidgetName(string widgetId)
    {
        var separatorIndex = widgetId.LastIndexOf('_');
        var kind = separatorIndex > 0 ? widgetId[..separatorIndex] : widgetId;
        return NormalizeWidgetKind(kind) switch
        {
            "ShortcutPanel" => "Shortcut Panel",
            "TitleBar" => "Title Bar",
            "CpuMonitor" => "CPU Monitor",
            "RamMonitor" => "RAM Monitor",
            "VideoBackground" => "Video Widget",
            "MediaControl" => "Media Control",
            "NetworkTraffic" => "Network Traffic",
            "DiskUsage" => "Disk Usage",
            "GpuMonitor" => "GPU Monitor",
            "StickyNotes" => "Sticky Notes",
            "RssFeed" => "RSS Feed",
            _ => NormalizeWidgetKind(kind)
        };
    }

    private static string NormalizeSpawnWidgetKind(string kind) => kind switch
    {
        "CPU Monitor" => "CpuMonitor",
        "RAM Monitor" => "RamMonitor",
        "Video Widget" => "VideoBackground",
        "Media Control" => "MediaControl",
        "Network Traffic" => "NetworkTraffic",
        "Disk Usage" => "DiskUsage",
        "GPU Monitor" => "GpuMonitor",
        "Sticky Notes" => "StickyNotes",
        "RSS Feed" => "RssFeed",
        _ => NormalizeWidgetKind(kind)
    };

    // ── Widget settings ─────────────────────────────────────
    private string SelectedWidgetId =>
        CmbWidgetSelect.SelectedItem is ComboBoxItem item && item.Tag is string id ? id : "Folder";

    private void CmbWidgetSelect_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (_widgetIdOverride is not null) return;
        LoadWidgetSettings(SelectedWidgetId);
    }

    private void LoadWidgetSettings(string widgetId)
    {
        _loading = true;
        var ws = ResolveWidgetSettings(widgetId);

        // Name field — default to the friendly kind name when Title is empty
        var defaultName = BuildDefaultWidgetName(widgetId);
        TxtWidgetName.Text = !string.IsNullOrWhiteSpace(ws.Title) ? ws.Title : defaultName;

        SliderOpacity.Value = Math.Max(ws.Opacity, SliderOpacity.Minimum);
        TxtOpacityValue.Text = $"{(int)(SliderOpacity.Value * 100)}%";
        ChkShowBorder.IsChecked = ws.ShowBorder;
        ChkTopmost.IsChecked = ws.Topmost;
        ChkStartMinimized.IsChecked = ws.IsMinimized;

        // Per-widget custom colors
        ChkWidgetCustomColors.IsChecked = ws.CustomColors is not null;
        LoadWidgetColorFields(ws);

        // Widget-type-specific panels
        var kind = widgetId.Contains('_') ? widgetId[..widgetId.LastIndexOf('_')] : widgetId;
        kind = NormalizeWidgetKind(kind);
        PanelDesktopFolderSettings.Visibility = kind == "Folder" ? Visibility.Visible : Visibility.Collapsed;
        PanelClockSettings.Visibility = kind == "Clock" ? Visibility.Visible : Visibility.Collapsed;
        PanelCpuSettings.Visibility = kind == "CpuMonitor" ? Visibility.Visible : Visibility.Collapsed;
        PanelRamSettings.Visibility = kind == "RamMonitor" ? Visibility.Visible : Visibility.Collapsed;
        PanelWeatherSettings.Visibility = kind == "Weather" ? Visibility.Visible : Visibility.Collapsed;
        PanelVideoBackgroundSettings.Visibility = kind == "VideoBackground" ? Visibility.Visible : Visibility.Collapsed;
        PanelStickyNotesSettings.Visibility = kind == "StickyNotes" ? Visibility.Visible : Visibility.Collapsed;
        PanelSlideshowSettings.Visibility = kind == "Slideshow" ? Visibility.Visible : Visibility.Collapsed;
        PanelRssFeedSettings.Visibility = kind == "RssFeed" ? Visibility.Visible : Visibility.Collapsed;
        PanelShortcutPanelSettings.Visibility = kind == "ShortcutPanel" ? Visibility.Visible : Visibility.Collapsed;

        if (kind == "Folder")
        {
            ws.Custom.TryGetValue("DefaultSort", out var sort);
            CmbDefaultSort.SelectedIndex = sort switch
            {
                "DateModified" => 1,
                "Size" => 2,
                "Type" => 3,
                _ => 0
            };

            var folder = ws.ActiveFolder;
            if (string.IsNullOrEmpty(folder))
                ws.Custom.TryGetValue("DefaultFolder", out folder);
            TxtDefaultFolder.Text = string.IsNullOrEmpty(folder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : folder;
        }

        if (kind == "Clock")
            LoadClockFields(ws);

        if (kind == "CpuMonitor")
            LoadCpuFields(ws);

        if (kind == "RamMonitor")
            LoadRamFields(ws);

        if (kind == "Weather")
            LoadWeatherFields(ws);

        if (kind == "VideoBackground")
            LoadVideoBackgroundFields(ws);

        if (kind == "StickyNotes")
            LoadStickyNotesFields(ws);

        if (kind == "Slideshow")
            LoadSlideshowFields(ws);

        if (kind == "RssFeed")
            LoadRssFeedFields(ws);

        if (kind == "ShortcutPanel")
            LoadShortcutPanelFields(ws);

        _loading = false;
    }

    private void LoadClockFields(WidgetSettings ws)
    {
        var mode = ws.Custom.TryGetValue(ClockModeKey, out var savedMode) && savedMode == "Analog"
            ? "Analog"
            : "Digital";
        CmbClockMode.SelectedIndex = mode == "Analog" ? 1 : 0;

        TxtClockHourColor.Text = ReadClockColorSetting(ws, ClockHourColorKey, "#FFE5E7EB");
        TxtClockMinuteColor.Text = ReadClockColorSetting(ws, ClockMinuteColorKey, "#FF93C5FD");
        TxtClockSecondColor.Text = ReadClockColorSetting(ws, ClockSecondColorKey, "#FFFCA5A5");

        SetSwatch(SwatchClockHourColor, TxtClockHourColor.Text);
        SetSwatch(SwatchClockMinuteColor, TxtClockMinuteColor.Text);
        SetSwatch(SwatchClockSecondColor, TxtClockSecondColor.Text);
    }

    private static string ReadClockColorSetting(WidgetSettings ws, string key, string fallback)
    {
        if (ws.Custom.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        return fallback;
    }

    private void LoadCpuFields(WidgetSettings ws)
    {
        var mode = ws.Custom.TryGetValue(CpuViewModeKey, out var savedMode) && savedMode == "Speedometer"
            ? "Speedometer"
            : "Bar";
        CmbCpuViewMode.SelectedIndex = mode == "Speedometer" ? 1 : 0;

        TxtCpuLowColor.Text = ReadClockColorSetting(ws, CpuLowColorKey, "#FF34D399");
        TxtCpuMediumColor.Text = ReadClockColorSetting(ws, CpuMediumColorKey, "#FFFBBF24");
        TxtCpuHighColor.Text = ReadClockColorSetting(ws, CpuHighColorKey, "#FFF87171");

        SetSwatch(SwatchCpuLowColor, TxtCpuLowColor.Text);
        SetSwatch(SwatchCpuMediumColor, TxtCpuMediumColor.Text);
        SetSwatch(SwatchCpuHighColor, TxtCpuHighColor.Text);
    }

    private void LoadRamFields(WidgetSettings ws)
    {
        var mode = ws.Custom.TryGetValue(RamViewModeKey, out var savedMode) && savedMode == "Speedometer"
            ? "Speedometer"
            : "Bar";
        CmbRamViewMode.SelectedIndex = mode == "Speedometer" ? 1 : 0;

        TxtRamLowColor.Text = ReadClockColorSetting(ws, RamLowColorKey, "#FF34D399");
        TxtRamMediumColor.Text = ReadClockColorSetting(ws, RamMediumColorKey, "#FFFBBF24");
        TxtRamHighColor.Text = ReadClockColorSetting(ws, RamHighColorKey, "#FFF87171");

        SetSwatch(SwatchRamLowColor, TxtRamLowColor.Text);
        SetSwatch(SwatchRamMediumColor, TxtRamMediumColor.Text);
        SetSwatch(SwatchRamHighColor, TxtRamHighColor.Text);
    }

    private void LoadWeatherFields(WidgetSettings ws)
    {
        var location = ws.Custom.TryGetValue(WeatherLocationKey, out var savedLocation) && !string.IsNullOrWhiteSpace(savedLocation)
            ? savedLocation
            : "New York City, New York, USA";
        TxtWeatherLocation.Text = location;

        var forecastDays = ws.Custom.TryGetValue(WeatherForecastDaysKey, out var savedDays) ? savedDays : "1";
        CmbWeatherForecastMode.SelectedIndex = forecastDays switch
        {
            "7" => 2,
            "3" => 1,
            _ => 0
        };

        var unit = ws.Custom.TryGetValue(WeatherTemperatureUnitKey, out var savedUnit)
            ? savedUnit
            : "celsius";
        CmbWeatherTempUnit.SelectedIndex = string.Equals(unit, "fahrenheit", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        TxtWeatherFreezeColor.Text = ReadClockColorSetting(ws, WeatherFreezeColorKey, "#FF60A5FA");
        TxtWeatherCoolColor.Text = ReadClockColorSetting(ws, WeatherCoolColorKey, "#FF22D3EE");
        TxtWeatherWarmColor.Text = ReadClockColorSetting(ws, WeatherWarmColorKey, "#FFFACC15");
        TxtWeatherHotColor.Text = ReadClockColorSetting(ws, WeatherHotColorKey, "#FFFB923C");
        TxtWeatherExtremeColor.Text = ReadClockColorSetting(ws, WeatherExtremeColorKey, "#FFEF4444");

        SetSwatch(SwatchWeatherFreezeColor, TxtWeatherFreezeColor.Text);
        SetSwatch(SwatchWeatherCoolColor, TxtWeatherCoolColor.Text);
        SetSwatch(SwatchWeatherWarmColor, TxtWeatherWarmColor.Text);
        SetSwatch(SwatchWeatherHotColor, TxtWeatherHotColor.Text);
        SetSwatch(SwatchWeatherExtremeColor, TxtWeatherExtremeColor.Text);
    }

    private void LoadWidgetColorFields(WidgetSettings ws)
    {
        var c = ws.CustomColors ?? ThemeHelper.ResolveColors(_settings);
        TxtWcWindowBg.Text = c.WindowBackground;
        TxtWcControlBg.Text = c.ControlBackground;
        TxtWcForeground.Text = c.Foreground;
        TxtWcAccent.Text = c.Accent;
        TxtWcBorder.Text = c.Border;
        TxtWcMuted.Text = c.MutedForeground;

        SetSwatch(SwatchWcWindowBg, TxtWcWindowBg.Text);
        SetSwatch(SwatchWcControlBg, TxtWcControlBg.Text);
        SetSwatch(SwatchWcForeground, TxtWcForeground.Text);
        SetSwatch(SwatchWcAccent, TxtWcAccent.Text);
        SetSwatch(SwatchWcBorder, TxtWcBorder.Text);
        SetSwatch(SwatchWcMuted, TxtWcMuted.Text);

        PanelWidgetColors.Visibility = ws.CustomColors is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadVideoBackgroundFields(WidgetSettings ws)
    {
        TxtVideoBackgroundPath.Text = ws.Custom.TryGetValue(VideoBackgroundSourcePathKey, out var savedPath)
            ? savedPath
            : string.Empty;
    }

    private void LoadStickyNotesFields(WidgetSettings ws)
    {
        if (CmbStickyNoteFont.Items.Count == 0)
        {
            foreach (var family in System.Windows.Media.Fonts.SystemFontFamilies.OrderBy(f => f.Source))
                CmbStickyNoteFont.Items.Add(family.Source);
        }

        var font = ws.Custom.TryGetValue(NoteFontFamilyKey, out var savedFont) && !string.IsNullOrWhiteSpace(savedFont)
            ? savedFont
            : "Segoe UI";

        CmbStickyNoteFont.SelectedItem = font;
        if (CmbStickyNoteFont.SelectedItem is null)
            CmbStickyNoteFont.SelectedIndex = 0;

        var fontSize = ws.Custom.TryGetValue(NoteFontSizeKey, out var savedSize)
            && double.TryParse(savedSize, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedSize)
            ? Math.Clamp(parsedSize, 8, 45)
            : 14;
        SliderStickyNoteFontSize.Value = fontSize;
        TxtStickyNoteFontSizeValue.Text = ((int)Math.Round(fontSize)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        TxtStickyNoteFontColor.Text = ws.Custom.TryGetValue(NoteFontColorKey, out var savedColor)
            ? savedColor
            : string.Empty;

        SetSwatch(SwatchStickyNoteFontColor, TxtStickyNoteFontColor.Text);
    }

    private void LoadSlideshowFields(WidgetSettings ws)
    {
        TxtSlideshowFolder.Text = ws.Custom.TryGetValue(SlideshowFolderKey, out var savedFolder)
            ? savedFolder
            : string.Empty;

        TxtSlideshowInterval.Text = ws.Custom.TryGetValue(SlideshowIntervalKey, out var savedInterval)
            ? savedInterval
            : "5";

        ChkSlideshowRandom.IsChecked = ws.Custom.TryGetValue(SlideshowRandomKey, out var savedRandom)
            && string.Equals(savedRandom, "true", StringComparison.OrdinalIgnoreCase);
    }

    private void LoadRssFeedFields(WidgetSettings ws)
    {
        TxtRssFeedUrl.Text = ws.Custom.TryGetValue(RssFeedUrlKey, out var savedUrl) && !string.IsNullOrWhiteSpace(savedUrl)
            ? savedUrl
            : "https://feeds.bbci.co.uk/news/rss.xml";

        TxtRssMaxItems.Text = ws.Custom.TryGetValue(RssMaxItemsKey, out var savedMax)
            ? savedMax
            : "15";

        TxtRssRefreshInterval.Text = ws.Custom.TryGetValue(RssRefreshIntervalKey, out var savedInterval)
            ? savedInterval
            : "3";

        var unit = ws.Custom.TryGetValue(RssRefreshUnitKey, out var savedUnit) ? savedUnit : "Hours";
        CmbRssRefreshUnit.SelectedIndex = unit switch
        {
            "Seconds" => 0,
            "Minutes" => 1,
            "Days" => 3,
            _ => 2 // Hours
        };
    }

    private void LoadShortcutPanelFields(WidgetSettings ws)
    {
        LstShortcuts.ItemsSource = ws.Shortcuts;
        TxtNoShortcuts.Visibility = ws.Shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private ThemeColors? ReadWidgetColorFields()
    {
        if (ChkWidgetCustomColors.IsChecked != true) return null;

        var global = ThemeHelper.ResolveColors(_settings);
        return new ThemeColors
        {
            WindowBackground = TxtWcWindowBg.Text.Trim(),
            ControlBackground = TxtWcControlBg.Text.Trim(),
            Foreground = TxtWcForeground.Text.Trim(),
            Accent = TxtWcAccent.Text.Trim(),
            Border = TxtWcBorder.Text.Trim(),
            MutedForeground = TxtWcMuted.Text.Trim(),
            SecondaryButton = global.SecondaryButton,
            DropdownBackground = TxtWcControlBg.Text.Trim(),
            DropdownItemHover = global.DropdownItemHover
        };
    }

    private void ChkWidgetCustomColors_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        PanelWidgetColors.Visibility = ChkWidgetCustomColors.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || TxtOpacityValue is null) return;
        TxtOpacityValue.Text = $"{(int)(SliderOpacity.Value * 100)}%";
    }

    private void SliderStickyNoteFontSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtStickyNoteFontSizeValue is null) return;
        TxtStickyNoteFontSizeValue.Text = ((int)Math.Round(SliderStickyNoteFontSize.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Slider_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        if (e.OriginalSource is DependencyObject source && FindAncestor<Thumb>(source) is not null)
            return;

        if (slider.Template.FindName("PART_Track", slider) is not Track track)
            return;

        var point = e.GetPosition(track);
        double ratio;

        if (slider.Orientation == System.Windows.Controls.Orientation.Horizontal)
        {
            ratio = track.ActualWidth <= 0 ? 0 : point.X / track.ActualWidth;
        }
        else
        {
            ratio = track.ActualHeight <= 0 ? 0 : 1 - (point.Y / track.ActualHeight);
        }

        ratio = Math.Clamp(ratio, 0, 1);
        slider.Value = slider.Minimum + ((slider.Maximum - slider.Minimum) * ratio);
        e.Handled = true;
    }

    private void ChkTopmost_Changed(object sender, RoutedEventArgs e)
    {
        // Just tracks state — saved on Save click
    }

    private void SaveWidgetSettings()
    {
        var widgetId = _widgetIdOverride ?? SelectedWidgetId;
        var ws = ResolveWidgetSettings(widgetId);
        var kind = widgetId.Contains('_') ? widgetId[..widgetId.LastIndexOf('_')] : widgetId;
        kind = NormalizeWidgetKind(kind);

        // Name / Title
        var name = TxtWidgetName.Text.Trim();
        var defaultName = BuildDefaultWidgetName(widgetId);
        ws.Title = string.Equals(name, defaultName, StringComparison.Ordinal) ? null : name;

        ws.Opacity = SliderOpacity.Value;
        ws.ShowBorder = ChkShowBorder.IsChecked == true;
        ws.Topmost = ChkTopmost.IsChecked == true;
        ws.IsMinimized = ChkStartMinimized.IsChecked == true;
        ws.CustomColors = ReadWidgetColorFields();

        if (kind == "Folder")
        {
            ws.Custom["DefaultSort"] = CmbDefaultSort.SelectedIndex switch
            {
                1 => "DateModified",
                2 => "Size",
                3 => "Type",
                _ => "Name"
            };

            var folder = TxtDefaultFolder.Text.Trim();
            if (!string.IsNullOrEmpty(folder))
                ws.ActiveFolder = folder;
            ws.Custom.Remove("DefaultFolder");
        }

        if (kind == "Clock")
        {
            ws.Custom[ClockModeKey] = CmbClockMode.SelectedIndex == 1 ? "Analog" : "Digital";
            ws.Custom[ClockHourColorKey] = TxtClockHourColor.Text.Trim();
            ws.Custom[ClockMinuteColorKey] = TxtClockMinuteColor.Text.Trim();
            ws.Custom[ClockSecondColorKey] = TxtClockSecondColor.Text.Trim();
        }

        if (kind == "CpuMonitor")
        {
            ws.Custom[CpuViewModeKey] = CmbCpuViewMode.SelectedIndex == 1 ? "Speedometer" : "Bar";
            ws.Custom[CpuLowColorKey] = TxtCpuLowColor.Text.Trim();
            ws.Custom[CpuMediumColorKey] = TxtCpuMediumColor.Text.Trim();
            ws.Custom[CpuHighColorKey] = TxtCpuHighColor.Text.Trim();
        }

        if (kind == "RamMonitor")
        {
            ws.Custom[RamViewModeKey] = CmbRamViewMode.SelectedIndex == 1 ? "Speedometer" : "Bar";
            ws.Custom[RamLowColorKey] = TxtRamLowColor.Text.Trim();
            ws.Custom[RamMediumColorKey] = TxtRamMediumColor.Text.Trim();
            ws.Custom[RamHighColorKey] = TxtRamHighColor.Text.Trim();
        }

        if (kind == "Weather")
        {
            var location = TxtWeatherLocation.Text.Trim();
            if (string.IsNullOrWhiteSpace(location))
                location = "New York City, New York, USA";

            var previousLocation = ws.Custom.TryGetValue(WeatherLocationKey, out var oldLocation)
                ? oldLocation
                : string.Empty;

            ws.Custom[WeatherLocationKey] = location;
            ws.Custom[WeatherForecastDaysKey] = CmbWeatherForecastMode.SelectedIndex switch
            {
                2 => "7",
                1 => "3",
                _ => "1"
            };
            ws.Custom[WeatherTemperatureUnitKey] = CmbWeatherTempUnit.SelectedIndex == 1 ? "fahrenheit" : "celsius";
            ws.Custom[WeatherFreezeColorKey] = TxtWeatherFreezeColor.Text.Trim();
            ws.Custom[WeatherCoolColorKey] = TxtWeatherCoolColor.Text.Trim();
            ws.Custom[WeatherWarmColorKey] = TxtWeatherWarmColor.Text.Trim();
            ws.Custom[WeatherHotColorKey] = TxtWeatherHotColor.Text.Trim();
            ws.Custom[WeatherExtremeColorKey] = TxtWeatherExtremeColor.Text.Trim();

            var hasCoordinates = ws.Custom.ContainsKey(WeatherLatitudeKey) && ws.Custom.ContainsKey(WeatherLongitudeKey);
            var locationChanged = !string.Equals(previousLocation?.Trim(), location, StringComparison.OrdinalIgnoreCase);
            if (locationChanged || !hasCoordinates)
            {
                if (TryGeocodeLocation(location, out var latitude, out var longitude))
                {
                    ws.Custom[WeatherLatitudeKey] = latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ws.Custom[WeatherLongitudeKey] = longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "Unable to resolve the location using OpenStreetMap. The widget will continue using the previous coordinates.",
                        "Weather Location",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        if (kind == "VideoBackground")
        {
            ws.Custom[VideoBackgroundSourcePathKey] = TxtVideoBackgroundPath.Text.Trim();
        }

        if (kind == "StickyNotes")
        {
            ws.Custom[NoteFontFamilyKey] = CmbStickyNoteFont.SelectedItem is string font ? font : "Segoe UI";
            ws.Custom[NoteFontSizeKey] = ((int)Math.Round(SliderStickyNoteFontSize.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            ws.Custom[NoteFontColorKey] = TxtStickyNoteFontColor.Text.Trim();
        }

        if (kind == "Slideshow")
        {
            ws.Custom[SlideshowFolderKey] = TxtSlideshowFolder.Text.Trim();
            ws.Custom[SlideshowIntervalKey] = TxtSlideshowInterval.Text.Trim();
            ws.Custom[SlideshowRandomKey] = ChkSlideshowRandom.IsChecked == true ? "true" : "false";
        }

        if (kind == "RssFeed")
        {
            ws.Custom[RssFeedUrlKey] = TxtRssFeedUrl.Text.Trim();
            ws.Custom[RssMaxItemsKey] = TxtRssMaxItems.Text.Trim();
            ws.Custom[RssRefreshIntervalKey] = TxtRssRefreshInterval.Text.Trim();
            ws.Custom[RssRefreshUnitKey] = CmbRssRefreshUnit.SelectedItem is System.Windows.Controls.ComboBoxItem cbi && cbi.Tag is string tag
                ? tag
                : "Hours";
        }

        if (_widgetSettingsOverride is not null && _widgetIdOverride is not null)
            _settings.Widgets[_widgetIdOverride] = ws;
    }

    private void BtnAddWidget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string widgetType)
        {
            _spawnWidget?.Invoke(NormalizeSpawnWidgetKind(widgetType));
            RefreshActiveWidgets();
        }
    }

    private void BtnCloseActiveWidget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string widgetId)
        {
            _closeWidget?.Invoke(widgetId);
            RefreshActiveWidgets();
        }
    }

    private void LstActiveWidgets_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_openWidgetSettings is null)
            return;

        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
            return;

        if (LstActiveWidgets.SelectedItem is ActiveWidgetListItem widget)
            _openWidgetSettings(widget.Id);
    }

    private void LstActiveWidgets_PreviewMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var listBoxItem = FindAncestor<ListBoxItem>(source);
        if (listBoxItem?.Content is not ActiveWidgetListItem widget)
            return;

        var menu = new System.Windows.Controls.ContextMenu();

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => _openWidgetSettings?.Invoke(widget.Id);
        menu.Items.Add(settingsItem);

        var resetItem = new System.Windows.Controls.MenuItem { Header = "Reset Location" };
        resetItem.Click += (_, _) => _resetWidgetLocation?.Invoke(widget.Id);
        menu.Items.Add(resetItem);

        menu.PlacementTarget = listBoxItem;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
                return match;

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private WidgetSettings ResolveWidgetSettings(string widgetId) =>
        _widgetSettingsOverride ?? _settings.GetWidgetSettings(widgetId);

    private void BtnBrowseVideoBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Media files (*.mp4;*.avi;*.mov;*.wmv;*.mkv;*.webm;*.gif)|*.mp4;*.avi;*.mov;*.wmv;*.mkv;*.webm;*.gif|All files (*.*)|*.*",
            Title = "Select a video or GIF for Video Widget"
        };

        if (dialog.ShowDialog() == true)
            TxtVideoBackgroundPath.Text = dialog.FileName;
    }

    private void BtnEditShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ShortcutItem shortcut)
            return;

        var widgetId = _widgetIdOverride ?? SelectedWidgetId;
        var ws = ResolveWidgetSettings(widgetId);

        var dlg = new ShortcutEditDialog(shortcut);
        if (IsLoaded && PresentationSource.FromVisual(this) is not null)
            dlg.Owner = this;

        if (dlg.ShowDialog() == true)
        {
            LoadShortcutPanelFields(ws);
            _settings.Save();
            _widgetSettingsApplied?.Invoke();
        }
    }

    private void BtnBrowseSlideshowFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder containing images",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtSlideshowFolder.Text = dlg.SelectedPath;
    }

    // ── Save / Close ────────────────────────────────────────
    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        DialogResult = true;
        Close();
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        ApplySettings();
    }

    private void ApplySettings()
    {
        var isWidgetSettings = _widgetIdOverride is not null;

        if (!isWidgetSettings)
        {
            _settings.AccentColor = TxtAccentColor.Text.Trim();
            _settings.ThemeMode = SelectedThemeMode;

            if (SelectedThemeMode == "Custom")
                _settings.CustomTheme = ReadCustomThemeFields();

            if (_extManager is not null)
            {
                _settings.HasConfiguredExtensions = true;
                _settings.EnabledExtensions = _extManager.Extensions
                    .Where(e => e.IsEnabled)
                    .Select(e => e.Name)
                    .ToList();
                _extManager.UpdateEnabledExtensions(_settings.EnabledExtensions);
                LoadWidgetTypes();
            }
        }

        if (isWidgetSettings)
        {
            SaveWidgetSettings();
        }

        if (!isWidgetSettings)
        {
            var colors = ThemeHelper.ResolveColors(_settings);
            ThemeHelper.ApplyToApp(colors);
        }

        _settings.Save();
        _widgetSettingsApplied?.Invoke();

        // Update the group tab label if this widget is currently grouped
        if (isWidgetSettings)
        {
            var widgetId = _widgetIdOverride ?? SelectedWidgetId;
            var ws = ResolveWidgetSettings(widgetId);
            MainWindow.RefreshWidgetGroupTab(widgetId, ws);
        }

        if (!isWidgetSettings)
            _settingsApplied?.Invoke();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private static HttpClient CreateGeocodeHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PrismPaneWidgets/1.0 (+https://openstreetmap.org)");
        return client;
    }

    private static bool TryGeocodeLocation(string location, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;

        try
        {
            var encoded = Uri.EscapeDataString(location);
            var url = $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q={encoded}";
            var response = GeocodeHttpClient.GetStringAsync(url).GetAwaiter().GetResult();

            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return false;

            var first = doc.RootElement[0];
            if (!first.TryGetProperty("lat", out var latProp) || !first.TryGetProperty("lon", out var lonProp))
                return false;

            if (!double.TryParse(latProp.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out latitude))
                return false;

            if (!double.TryParse(lonProp.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out longitude))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }
}
