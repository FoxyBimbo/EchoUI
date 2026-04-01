using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;
using MediaColor = System.Windows.Media.Color;

namespace PrismPane_Widgets.Views;

public partial class WeatherWidget : Window
{
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

    private static readonly HttpClient WeatherHttpClient = CreateHttpClient();

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMinutes(30) };

    private bool _isRefreshing;
    private string _locationName = "New York City, New York, USA";
    private int _forecastDays = 1;
    private double _latitude = 40.7128;
    private double _longitude = -74.0060;
    private string _temperatureUnit = "celsius";
    private MediaColor _freezeColor;
    private MediaColor _coolColor;
    private MediaColor _warmColor;
    private MediaColor _hotColor;
    private MediaColor _extremeColor;
    private readonly WidgetMinimizeBehavior _minimizeBehavior;

    public string WidgetId => _widgetId;

    public WeatherWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        _minimizeBehavior = new WidgetMinimizeBehavior(this, RootBorder, InnerLayout, HeaderPanel, ContentPanel, BtnMinimize, _widgetSettings, _appSettings, Height);

        ApplyWidgetSettingsFromModel();

        _refreshTimer.Tick += async (_, _) => await RefreshWeatherAsync();
        Loaded += async (_, _) =>
        {
            await RefreshWeatherAsync();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private WidgetSettings SyncWidgetSettings()
    {
        _appSettings.Widgets[_widgetId] = _widgetSettings;
        return _widgetSettings;
    }

    public void ApplyWidgetSettingsFromModel()
    {
        var ws = SyncWidgetSettings();
        ThemeHelper.ApplyToElement(this, ws.CustomColors);
        Topmost = ws.Topmost;
        Opacity = ws.Opacity;
        RootBorder.BorderThickness = ws.ShowBorder ? new Thickness(1.5) : new Thickness(0);
        if (!string.IsNullOrWhiteSpace(ws.Title))
            TxtLocationTitle.Text = ws.Title;

        if (ws.Width is > 0)
            Width = ws.Width.Value;
        if (ws.Height is > 0)
            Height = ws.Height.Value;

        _minimizeBehavior.ApplyFromSettings();

        _locationName = ws.Custom.TryGetValue(WeatherLocationKey, out var location) && !string.IsNullOrWhiteSpace(location)
            ? location
            : "New York City, New York, USA";

        _forecastDays = ws.Custom.TryGetValue(WeatherForecastDaysKey, out var days) && int.TryParse(days, out var parsedDays)
            ? Math.Clamp(parsedDays, 1, 7)
            : 1;

        _latitude = ws.Custom.TryGetValue(WeatherLatitudeKey, out var lat) && double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLat)
            ? parsedLat
            : 40.7128;

        _longitude = ws.Custom.TryGetValue(WeatherLongitudeKey, out var lon) && double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLon)
            ? parsedLon
            : -74.0060;

        _temperatureUnit = ws.Custom.TryGetValue(WeatherTemperatureUnitKey, out var unit) && string.Equals(unit, "fahrenheit", StringComparison.OrdinalIgnoreCase)
            ? "fahrenheit"
            : "celsius";

        _freezeColor = ReadThemeColor(ws, WeatherFreezeColorKey, "#FF60A5FA");
        _coolColor = ReadThemeColor(ws, WeatherCoolColorKey, "#FF22D3EE");
        _warmColor = ReadThemeColor(ws, WeatherWarmColorKey, "#FFFACC15");
        _hotColor = ReadThemeColor(ws, WeatherHotColorKey, "#FFFB923C");
        _extremeColor = ReadThemeColor(ws, WeatherExtremeColorKey, "#FFEF4444");

        TxtLocationTitle.Text = _locationName;
        Title = _locationName;

        _ = RefreshWeatherAsync();
        _appSettings.Save();
    }

    private async Task RefreshWeatherAsync()
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        try
        {
            var url =
                $"https://api.open-meteo.com/v1/forecast?latitude={_latitude.ToString(CultureInfo.InvariantCulture)}&longitude={_longitude.ToString(CultureInfo.InvariantCulture)}&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&temperature_unit={_temperatureUnit}&timezone=auto&forecast_days={_forecastDays}";

            var json = await WeatherHttpClient.GetStringAsync(url);
            var weather = JsonSerializer.Deserialize<OpenMeteoResponse>(json);
            if (weather?.Current is null || weather.Daily is null)
                return;

            var tempColor = new System.Windows.Media.SolidColorBrush(GetTemperatureColor(weather.Current.Temperature2m));
            var symbol = _temperatureUnit == "fahrenheit" ? "°F" : "°C";

            TxtCurrentTemp.Text = $"{Math.Round(weather.Current.Temperature2m):0}{symbol}";
            TxtCurrentTemp.Foreground = tempColor;
            TxtCurrentCondition.Text = WeatherCodeToDescription(weather.Current.WeatherCode);
            TxtCurrentCondition.Foreground = tempColor;

            ForecastPanel.Children.Clear();
            var daysToShow = Math.Min(_forecastDays, weather.Daily.Time?.Length ?? 0);
            for (var i = 0; i < daysToShow; i++)
            {
                var date = weather.Daily.Time![i];
                var max = weather.Daily.Temperature2mMax?[i];
                var min = weather.Daily.Temperature2mMin?[i];
                var code = weather.Daily.WeatherCode?[i] ?? weather.Current.WeatherCode;

                var row = new TextBlock
                {
                    Text = $"{FormatDate(date)}: {Math.Round(max ?? 0):0}{symbol} / {Math.Round(min ?? 0):0}{symbol} • {WeatherCodeToDescription(code)}",
                    Margin = new Thickness(0, 0, 0, 4),
                    Foreground = (System.Windows.Media.Brush)FindResource("ForegroundBrush")
                };
                ForecastPanel.Children.Add(row);
            }

            TxtLastUpdated.Text = $"Updated {DateTime.Now:t}";
        }
        catch
        {
            TxtCurrentCondition.Text = "Unable to load weather";
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static string FormatDate(string isoDate)
    {
        if (!DateTime.TryParse(isoDate, out var dt))
            return isoDate;

        return dt.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PrismPaneWidgets/1.0");
        return client;
    }

    private static MediaColor ReadThemeColor(WidgetSettings ws, string key, string fallback)
    {
        if (ws.Custom.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            try
            {
                return ThemeHelper.ParseColor(value);
            }
            catch
            {
            }
        }

        return ThemeHelper.ParseColor(fallback);
    }

    private MediaColor GetTemperatureColor(double temperature)
    {
        if (_temperatureUnit == "fahrenheit")
        {
            if (temperature < 32) return _freezeColor;
            if (temperature < 59) return _coolColor;
            if (temperature < 77) return _warmColor;
            if (temperature < 90) return _hotColor;
            return _extremeColor;
        }

        if (temperature < 0) return _freezeColor;
        if (temperature < 15) return _coolColor;
        if (temperature < 25) return _warmColor;
        if (temperature < 32) return _hotColor;
        return _extremeColor;
    }

    private static string WeatherCodeToDescription(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 or 3 => "Partly cloudy",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 => "Snow",
        77 => "Snow grains",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Unknown"
    };

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (MainWindow.DragWidgetGroup(this)) return;
            DragMove();
            MainWindow.SnapManager.OnDragCompleted(this);
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_appSettings, null, _widgetId, _widgetSettings, ApplyWidgetSettingsFromModel)
        {
            Owner = this
        };
        win.ShowDialog();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.DockManager.Undock(_widgetId);
        Close();
    }

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("current")]
        public CurrentWeather? Current { get; set; }

        [JsonPropertyName("daily")]
        public DailyWeather? Daily { get; set; }
    }

    private sealed class CurrentWeather
    {
        [JsonPropertyName("temperature_2m")]
        public double Temperature2m { get; set; }

        [JsonPropertyName("weather_code")]
        public int WeatherCode { get; set; }
    }

    private sealed class DailyWeather
    {
        [JsonPropertyName("time")]
        public string[]? Time { get; set; }

        [JsonPropertyName("weather_code")]
        public int[]? WeatherCode { get; set; }

        [JsonPropertyName("temperature_2m_max")]
        public double[]? Temperature2mMax { get; set; }

        [JsonPropertyName("temperature_2m_min")]
        public double[]? Temperature2mMin { get; set; }
    }
}
