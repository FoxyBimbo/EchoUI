using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;

namespace PrismPane_Widgets.Views;

public partial class ClockWidget : Window
{
    private const string ClockModeKey = "ClockMode";
    private const string HourColorKey = "ClockHourColor";
    private const string MinuteColorKey = "ClockMinuteColor";
    private const string SecondColorKey = "ClockSecondColor";

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly WidgetMinimizeBehavior _minimizeBehavior;

    public string WidgetId => _widgetId;

    public ClockWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        _minimizeBehavior = new WidgetMinimizeBehavior(this, RootBorder, InnerLayout, HeaderPanel, ContentPanel, BtnMinimize, _widgetSettings, _appSettings, Height);

        ApplyWidgetSettingsFromModel();

        _clockTimer.Tick += (_, _) => UpdateClock();
        Loaded += (_, _) =>
        {
            UpdateClock();
            _clockTimer.Start();
        };
        Closed += (_, _) => _clockTimer.Stop();
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
        TxtWidgetTitle.Text = !string.IsNullOrWhiteSpace(ws.Title) ? ws.Title : "Clock";

        if (ws.Width is > 0)
            Width = ws.Width.Value;
        if (ws.Height is > 0)
            Height = ws.Height.Value;

        _minimizeBehavior.ApplyFromSettings();

        var mode = ws.Custom.TryGetValue(ClockModeKey, out var savedMode) && savedMode == "Analog"
            ? "Analog"
            : "Digital";

        var hourColor = ReadColor(ws, HourColorKey, "#FFE5E7EB");
        var minuteColor = ReadColor(ws, MinuteColorKey, "#FF93C5FD");
        var secondColor = ReadColor(ws, SecondColorKey, "#FFFCA5A5");

        RunHour.Foreground = new SolidColorBrush(hourColor);
        RunMinute.Foreground = new SolidColorBrush(minuteColor);
        RunSecond.Foreground = new SolidColorBrush(secondColor);

        HourHand.Stroke = new SolidColorBrush(hourColor);
        MinuteHand.Stroke = new SolidColorBrush(minuteColor);
        SecondHand.Stroke = new SolidColorBrush(secondColor);

        DigitalPanel.Visibility = mode == "Digital" ? Visibility.Visible : Visibility.Collapsed;
        AnalogPanel.Visibility = mode == "Analog" ? Visibility.Visible : Visibility.Collapsed;

        _appSettings.Save();
    }

    private static System.Windows.Media.Color ReadColor(WidgetSettings ws, string key, string fallback)
    {
        if (ws.Custom.TryGetValue(key, out var value))
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

    private void UpdateClock()
    {
        var now = DateTime.Now;

        RunHour.Text = now.Hour.ToString("00");
        RunMinute.Text = now.Minute.ToString("00");
        RunSecond.Text = now.Second.ToString("00");
        TxtDigitalDate.Text = now.ToString("ddd, MMM dd");

        HourRotate.Angle = ((now.Hour % 12) + now.Minute / 60.0) * 30.0;
        MinuteRotate.Angle = (now.Minute + now.Second / 60.0) * 6.0;
        SecondRotate.Angle = (now.Second + now.Millisecond / 1000.0) * 6.0;
    }

    private void RootBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
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
}
