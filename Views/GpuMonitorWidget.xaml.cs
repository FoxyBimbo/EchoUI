using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;
using MediaColor = System.Windows.Media.Color;

namespace PrismPane_Widgets.Views;

public partial class GpuMonitorWidget : Window
{
    private const string GpuLowColorKey = "GpuLowColor";
    private const string GpuMediumColorKey = "GpuMediumColor";
    private const string GpuHighColorKey = "GpuHighColor";

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _gpuTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    private double _currentUsage;
    private List<PerformanceCounter>? _gpuCounters;

    private MediaColor _lowColor;
    private MediaColor _mediumColor;
    private MediaColor _highColor;
    private readonly WidgetMinimizeBehavior _minimizeBehavior;

    public string WidgetId => _widgetId;

    public GpuMonitorWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        _minimizeBehavior = new WidgetMinimizeBehavior(this, RootBorder, InnerLayout, HeaderPanel, ContentPanel, BtnMinimize, _widgetSettings, _appSettings, Height);

        ApplyWidgetSettingsFromModel();

        _gpuTimer.Tick += (_, _) => UpdateUsage();
        UsageBarTrack.SizeChanged += (_, _) => UpdateBarFill();

        Loaded += (_, _) =>
        {
            InitializeCounters();
            UpdateUsage();
            _gpuTimer.Start();
        };
        Closed += (_, _) =>
        {
            _gpuTimer.Stop();
            DisposeCounters();
        };
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
        TxtWidgetTitle.Text = !string.IsNullOrWhiteSpace(ws.Title) ? ws.Title : "GPU";

        if (ws.Width is > 0)
            Width = ws.Width.Value;
        if (ws.Height is > 0)
            Height = ws.Height.Value;

        _minimizeBehavior.ApplyFromSettings();

        _lowColor = ReadColor(ws, GpuLowColorKey, "#FF34D399");
        _mediumColor = ReadColor(ws, GpuMediumColorKey, "#FFFBBF24");
        _highColor = ReadColor(ws, GpuHighColorKey, "#FFF87171");

        LowIndicator.Fill = new SolidColorBrush(_lowColor);
        MediumIndicator.Fill = new SolidColorBrush(_mediumColor);
        HighIndicator.Fill = new SolidColorBrush(_highColor);

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

    private void InitializeCounters()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames();

            _gpuCounters = [];
            foreach (var instance in instanceNames)
            {
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    continue;

                var counters = category.GetCounters(instance);
                foreach (var counter in counters)
                {
                    if (counter.CounterName.Equals("Utilization Percentage", StringComparison.OrdinalIgnoreCase))
                    {
                        counter.NextValue();
                        _gpuCounters.Add(counter);
                    }
                    else
                    {
                        counter.Dispose();
                    }
                }
            }
        }
        catch
        {
            _gpuCounters = null;
        }
    }

    private void DisposeCounters()
    {
        if (_gpuCounters is null)
            return;

        foreach (var counter in _gpuCounters)
        {
            try { counter.Dispose(); }
            catch { }
        }

        _gpuCounters = null;
    }

    private void UpdateUsage()
    {
        var usage = ReadGpuUsage();
        if (usage >= 0)
            _currentUsage = usage;

        UpdateVisuals();
    }

    private double ReadGpuUsage()
    {
        if (_gpuCounters is null || _gpuCounters.Count == 0)
        {
            InitializeCounters();
            if (_gpuCounters is null || _gpuCounters.Count == 0)
                return 0;
        }

        try
        {
            double total = 0;
            foreach (var counter in _gpuCounters)
                total += counter.NextValue();

            return Math.Clamp(total, 0, 100);
        }
        catch
        {
            DisposeCounters();
            return -1;
        }
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
    }

    private void UpdateBarFill()
    {
        if (UsageBarTrack.ActualWidth <= 2)
            return;

        var width = Math.Max(0, UsageBarTrack.ActualWidth - 2);
        UsageBarFill.Width = width * (_currentUsage / 100.0);
    }

    private MediaColor GetUsageColor(double usage) => usage switch
    {
        < 40 => _lowColor,
        < 75 => _mediumColor,
        _ => _highColor
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
}
