using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EchoUI.Models;
using EchoUI.Services;
using ColorDialog = System.Windows.Forms.ColorDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Button = System.Windows.Controls.Button;

namespace EchoUI.Views;

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

    private static readonly HttpClient GeocodeHttpClient = CreateGeocodeHttpClient();

    private readonly AppSettings _settings;
    private readonly ExtensionManager? _extManager;
    private readonly string? _widgetIdOverride;
    private readonly WidgetSettings? _widgetSettingsOverride;
    private readonly Action? _widgetSettingsApplied;
    private readonly Action<string>? _spawnWidget;
    private readonly Action? _settingsApplied;
    private bool _loading = true;

    public SettingsWindow(AppSettings settings, ExtensionManager? extManager, string? widgetIdOverride = null,
        WidgetSettings? widgetSettingsOverride = null, Action? widgetSettingsApplied = null,
        Action<string>? spawnWidget = null, Action? settingsApplied = null)
    {
        _settings = settings;
        _extManager = extManager;
        _widgetIdOverride = widgetIdOverride;
        _widgetSettingsOverride = widgetSettingsOverride;
        _widgetSettingsApplied = widgetSettingsApplied;
        _spawnWidget = spawnWidget;
        _settingsApplied = settingsApplied;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        _loading = true;
        TxtAccentColor.Text = _settings.AccentColor;

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
            PanelThemeSettings.Visibility = Visibility.Collapsed;
            PanelGeneralSettings.Visibility = Visibility.Collapsed;
            PanelWidgetSettings.Visibility = Visibility.Visible;
            PanelWidgetSelector.Visibility = Visibility.Collapsed;
            PanelAddWidgets.Visibility = Visibility.Collapsed;
            return;
        }

        PanelWidgetSettings.Visibility = Visibility.Collapsed;
        PanelThemeSettings.Visibility = Visibility.Visible;
        PanelGeneralSettings.Visibility = Visibility.Visible;
        PanelAddWidgets.Visibility = _extManager is null ? Visibility.Collapsed : Visibility.Visible;
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
            new ExtensionInfo { Name = "Folder", Kind = ExtensionKind.Widget, IsEnabled = true },
            new ExtensionInfo { Name = "ShortcutPanel", Kind = ExtensionKind.Widget, IsEnabled = true },
            new ExtensionInfo { Name = "TitleBar", Kind = ExtensionKind.Widget, IsEnabled = true },
            new ExtensionInfo { Name = "Clock", Kind = ExtensionKind.Widget, IsEnabled = true },
            new ExtensionInfo { Name = "CpuMonitor", Kind = ExtensionKind.Widget, IsEnabled = true },
            new ExtensionInfo { Name = "RamMonitor", Kind = ExtensionKind.Widget, IsEnabled = true },
            new ExtensionInfo { Name = "Weather", Kind = ExtensionKind.Widget, IsEnabled = true }
        };

        var scripted = _extManager.Extensions
            .Where(e => e.Kind == ExtensionKind.Widget && e.IsEnabled)
            .ToList();

        LstWidgetTypes.ItemsSource = builtins
            .Concat(scripted)
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Name)
            .ToList();
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

    private static string NormalizeSpawnWidgetKind(string kind) => kind switch
    {
        "CPU Monitor" => "CpuMonitor",
        "RAM Monitor" => "RamMonitor",
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

        SliderOpacity.Value = ws.Opacity;
        TxtOpacityValue.Text = $"{(int)(ws.Opacity * 100)}%";
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
        PanelWidgetColors.Visibility = ws.CustomColors is not null ? Visibility.Visible : Visibility.Collapsed;
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

        ws.Opacity = SliderOpacity.Value;
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

        if (_widgetSettingsOverride is not null && _widgetIdOverride is not null)
            _settings.Widgets[_widgetIdOverride] = ws;
    }

    private void BtnAddWidget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string widgetType)
            _spawnWidget?.Invoke(NormalizeSpawnWidgetKind(widgetType));
    }

    private WidgetSettings ResolveWidgetSettings(string widgetId) =>
        _widgetSettingsOverride ?? _settings.GetWidgetSettings(widgetId);

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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoUI/1.0 (+https://openstreetmap.org)");
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
