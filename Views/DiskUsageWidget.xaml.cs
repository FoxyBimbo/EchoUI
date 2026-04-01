using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;
using MediaColor = System.Windows.Media.Color;

namespace PrismPane_Widgets.Views;

public partial class DiskUsageWidget : Window
{
    private const string DiskDriveKey = "DiskDrive";
    private const string DiskLowColorKey = "DiskLowColor";
    private const string DiskMediumColorKey = "DiskMediumColor";
    private const string DiskHighColorKey = "DiskHighColor";

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _diskTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private double _currentUsage;
    private string _driveLetter = "C";

    private MediaColor _lowColor;
    private MediaColor _mediumColor;
    private MediaColor _highColor;
    private readonly WidgetMinimizeBehavior _minimizeBehavior;

    public string WidgetId => _widgetId;

    public DiskUsageWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        _minimizeBehavior = new WidgetMinimizeBehavior(this, RootBorder, InnerLayout, HeaderPanel, ContentPanel, BtnMinimize, _widgetSettings, _appSettings, Height);

        ApplyWidgetSettingsFromModel();

        _diskTimer.Tick += (_, _) => UpdateUsage();
        UsageBarTrack.SizeChanged += (_, _) => UpdateBarFill();

        Loaded += (_, _) =>
        {
            UpdateUsage();
            _diskTimer.Start();
        };
        Closed += (_, _) => _diskTimer.Stop();
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
        TxtTitle.Text = !string.IsNullOrWhiteSpace(ws.Title) ? ws.Title : "Disk";

        if (ws.Width is > 0)
            Width = ws.Width.Value;
        if (ws.Height is > 0)
            Height = ws.Height.Value;

        _minimizeBehavior.ApplyFromSettings();

        _driveLetter = ws.Custom.TryGetValue(DiskDriveKey, out var drive) && !string.IsNullOrWhiteSpace(drive)
            ? drive
            : "C";

        _lowColor = ReadColor(ws, DiskLowColorKey, "#FF34D399");
        _mediumColor = ReadColor(ws, DiskMediumColorKey, "#FFFBBF24");
        _highColor = ReadColor(ws, DiskHighColorKey, "#FFF87171");

        LowIndicator.Fill = new SolidColorBrush(_lowColor);
        MediumIndicator.Fill = new SolidColorBrush(_mediumColor);
        HighIndicator.Fill = new SolidColorBrush(_highColor);

        TxtTitle.Text = $"Disk ({_driveLetter}:)";

        UpdateUsage();
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
        if (TryReadDiskUsage(out var usage, out var usedGb, out var totalGb))
        {
            _currentUsage = usage;
            TxtBarLabel.Text = $"{usedGb:F1} / {totalGb:F1} GB used";
        }

        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        var color = GetUsageColor(_currentUsage);
        var usageBrush = new SolidColorBrush(color);

        TxtUsage.Text = $"{Math.Round(_currentUsage):0}%";
        TxtUsage.Foreground = usageBrush;

        UsageBarFill.Background = usageBrush;

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
        < 60 => _lowColor,
        < 85 => _mediumColor,
        _ => _highColor
    };

    private bool TryReadDiskUsage(out double usage, out double usedGb, out double totalGb)
    {
        usage = 0;
        usedGb = 0;
        totalGb = 0;

        try
        {
            var driveInfo = new DriveInfo(_driveLetter);
            if (!driveInfo.IsReady)
                return false;

            totalGb = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
            var freeGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            usedGb = totalGb - freeGb;
            usage = totalGb > 0 ? Math.Clamp((usedGb / totalGb) * 100.0, 0, 100) : 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

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
