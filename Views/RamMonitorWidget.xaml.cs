using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;
using MediaColor = System.Windows.Media.Color;
using WPoint = System.Windows.Point;

namespace PrismPane_Widgets.Views;

public partial class RamMonitorWidget : Window
{
    private const string RamViewModeKey = "RamViewMode";
    private const string RamLowColorKey = "RamLowColor";
    private const string RamMediumColorKey = "RamMediumColor";
    private const string RamHighColorKey = "RamHighColor";

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _ramTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private double _currentUsage;

    private string _viewMode = "Bar";
    private MediaColor _lowColor;
    private MediaColor _mediumColor;
    private MediaColor _highColor;

    public string WidgetId => _widgetId;

    public RamMonitorWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;

        ApplyWidgetSettingsFromModel();

        _ramTimer.Tick += (_, _) => UpdateUsage();
        UsageBarTrack.SizeChanged += (_, _) => UpdateBarFill();

        Loaded += (_, _) =>
        {
            UpdateUsage();
            _ramTimer.Start();
        };
        Closed += (_, _) => _ramTimer.Stop();
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

        if (ws.Width is > 0)
            Width = ws.Width.Value;
        if (ws.Height is > 0)
            Height = ws.Height.Value;

        _viewMode = ws.Custom.TryGetValue(RamViewModeKey, out var viewMode) && viewMode == "Speedometer"
            ? "Speedometer"
            : "Bar";

        _lowColor = ReadColor(ws, RamLowColorKey, "#FF34D399");
        _mediumColor = ReadColor(ws, RamMediumColorKey, "#FFFBBF24");
        _highColor = ReadColor(ws, RamHighColorKey, "#FFF87171");

        LowIndicator.Fill = new SolidColorBrush(_lowColor);
        MediumIndicator.Fill = new SolidColorBrush(_mediumColor);
        HighIndicator.Fill = new SolidColorBrush(_highColor);

        BarPanel.Visibility = _viewMode == "Bar" ? Visibility.Visible : Visibility.Collapsed;
        SpeedometerPanel.Visibility = _viewMode == "Speedometer" ? Visibility.Visible : Visibility.Collapsed;

        UpdateVisuals();
        _appSettings.Save();
    }

    private static MediaColor ReadColor(WidgetSettings ws, string key, string fallback)
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

    private void UpdateUsage()
    {
        if (TryReadRamUsagePercent(out var usage))
            _currentUsage = usage;

        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        var color = GetUsageColor(_currentUsage);
        var usageBrush = new SolidColorBrush(color);

        TxtUsage.Text = $"{Math.Round(_currentUsage):0}%";
        TxtUsage.Foreground = usageBrush;

        UsageBarFill.Background = usageBrush;
        TxtBarLabel.Text = _currentUsage switch
        {
            < 40 => "Low usage",
            < 75 => "Medium usage",
            _ => "High usage"
        };

        UpdateBarFill();

        NeedleRotate.Angle = -90 + (_currentUsage / 100.0) * 180;
        GaugeArc.Stroke = usageBrush;
        GaugeArc.Data = BuildGaugeArc(_currentUsage);
    }

    private void UpdateBarFill()
    {
        if (UsageBarTrack.ActualWidth <= 2)
            return;

        var width = Math.Max(0, UsageBarTrack.ActualWidth - 2);
        UsageBarFill.Width = width * (_currentUsage / 100.0);
    }

    private Geometry BuildGaugeArc(double usage)
    {
        const double centerX = 90;
        const double centerY = 90;
        const double radius = 70;

        var percentage = Math.Clamp(usage, 0, 100) / 100.0;
        if (percentage <= 0)
        {
            var empty = new PathGeometry();
            empty.Figures.Add(new PathFigure { StartPoint = new WPoint(20, 90), IsClosed = false, IsFilled = false });
            return empty;
        }

        var startAngle = 180.0;
        var endAngle = startAngle - (percentage * 180.0);

        var startRadians = startAngle * Math.PI / 180.0;
        var startX = centerX + radius * Math.Cos(startRadians);
        var startY = centerY - radius * Math.Sin(startRadians);

        var points = new PointCollection();
        for (var angle = startAngle - 3.0; angle > endAngle; angle -= 3.0)
        {
            var radians = angle * Math.PI / 180.0;
            points.Add(new WPoint(
                centerX + radius * Math.Cos(radians),
                centerY - radius * Math.Sin(radians)));
        }

        var endRadians = endAngle * Math.PI / 180.0;
        points.Add(new WPoint(
            centerX + radius * Math.Cos(endRadians),
            centerY - radius * Math.Sin(endRadians)));

        var figure = new PathFigure { StartPoint = new WPoint(startX, startY), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(points, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private MediaColor GetUsageColor(double usage) => usage switch
    {
        < 40 => _lowColor,
        < 75 => _mediumColor,
        _ => _highColor
    };

    private static bool TryReadRamUsagePercent(out double usage)
    {
        var status = new MEMORYSTATUSEX();
        if (!GlobalMemoryStatusEx(status))
        {
            usage = 0;
            return false;
        }

        usage = Math.Clamp(status.dwMemoryLoad, 0, 100);
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
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
