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

public partial class CpuMonitorWidget : Window
{
    private const string CpuViewModeKey = "CpuViewMode";
    private const string CpuLowColorKey = "CpuLowColor";
    private const string CpuMediumColorKey = "CpuMediumColor";
    private const string CpuHighColorKey = "CpuHighColor";

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _cpuTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private bool _hasCpuBaseline;
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private double _currentUsage;

    private string _viewMode = "Bar";
    private MediaColor _lowColor;
    private MediaColor _mediumColor;
    private MediaColor _highColor;
    private readonly WidgetMinimizeBehavior _minimizeBehavior;

    public string WidgetId => _widgetId;

    public CpuMonitorWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        _minimizeBehavior = new WidgetMinimizeBehavior(this, RootBorder, InnerLayout, HeaderPanel, ContentPanel, BtnMinimize, _widgetSettings, _appSettings, Height);

        ApplyWidgetSettingsFromModel();

        _cpuTimer.Tick += (_, _) => UpdateUsage();
        UsageBarTrack.SizeChanged += (_, _) => UpdateBarFill();

        Loaded += (_, _) =>
        {
            PrimeCpuReading();
            UpdateUsage();
            _cpuTimer.Start();
        };
        Closed += (_, _) => _cpuTimer.Stop();
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
        TxtWidgetTitle.Text = !string.IsNullOrWhiteSpace(ws.Title) ? ws.Title : "CPU";

        if (ws.Width is > 0)
            Width = ws.Width.Value;
        if (ws.Height is > 0)
            Height = ws.Height.Value;

        _minimizeBehavior.ApplyFromSettings();

        _viewMode = ws.Custom.TryGetValue(CpuViewModeKey, out var viewMode) && viewMode == "Speedometer"
            ? "Speedometer"
            : "Bar";

        _lowColor = ReadColor(ws, CpuLowColorKey, "#FF34D399");
        _mediumColor = ReadColor(ws, CpuMediumColorKey, "#FFFBBF24");
        _highColor = ReadColor(ws, CpuHighColorKey, "#FFF87171");

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

    private void PrimeCpuReading()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return;

        _previousIdle = ToUInt64(idle);
        _previousKernel = ToUInt64(kernel);
        _previousUser = ToUInt64(user);
        _hasCpuBaseline = true;
    }

    private void UpdateUsage()
    {
        var usage = ReadCpuUsagePercent();
        if (usage >= 0)
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

    private double ReadCpuUsagePercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return -1;

        var idle = ToUInt64(idleTime);
        var kernel = ToUInt64(kernelTime);
        var user = ToUInt64(userTime);

        if (!_hasCpuBaseline)
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            _hasCpuBaseline = true;
            return 0;
        }

        var idleDelta = idle - _previousIdle;
        var kernelDelta = kernel - _previousKernel;
        var userDelta = user - _previousUser;

        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        var total = kernelDelta + userDelta;
        if (total == 0)
            return 0;

        var usage = (1.0 - (double)idleDelta / total) * 100.0;
        return Math.Clamp(usage, 0, 100);
    }

    private static ulong ToUInt64(FILETIME fileTime) => ((ulong)fileTime.dwHighDateTime << 32) + fileTime.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

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
}
