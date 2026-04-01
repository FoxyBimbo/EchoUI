using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;
using MediaColor = System.Windows.Media.Color;

namespace PrismPane_Widgets.Views;

public partial class NetworkTrafficWidget : Window
{
    private const string NetDownloadColorKey = "NetDownloadColor";
    private const string NetUploadColorKey = "NetUploadColor";

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _netTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private long _previousBytesReceived;
    private long _previousBytesSent;
    private bool _hasBaseline;
    private double _downloadBytesPerSec;
    private double _uploadBytesPerSec;
    private double _peakSpeed;

    private MediaColor _downloadColor;
    private MediaColor _uploadColor;
    private readonly WidgetMinimizeBehavior _minimizeBehavior;

    public string WidgetId => _widgetId;

    public NetworkTrafficWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        _minimizeBehavior = new WidgetMinimizeBehavior(this, RootBorder, InnerLayout, HeaderPanel, ContentPanel, BtnMinimize, _widgetSettings, _appSettings, Height);

        ApplyWidgetSettingsFromModel();

        _netTimer.Tick += (_, _) => UpdateTraffic();
        DownloadBarTrack.SizeChanged += (_, _) => UpdateBars();
        UploadBarTrack.SizeChanged += (_, _) => UpdateBars();

        Loaded += (_, _) =>
        {
            PrimeBaseline();
            _netTimer.Start();
        };
        Closed += (_, _) => _netTimer.Stop();
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
        TxtWidgetTitle.Text = !string.IsNullOrWhiteSpace(ws.Title) ? ws.Title : "Network";

        if (ws.Width is > 0)
            Width = ws.Width.Value;
        if (ws.Height is > 0)
            Height = ws.Height.Value;

        _minimizeBehavior.ApplyFromSettings();

        _downloadColor = ReadColor(ws, NetDownloadColorKey, "#FF34D399");
        _uploadColor = ReadColor(ws, NetUploadColorKey, "#FF60A5FA");

        DownloadIndicator.Fill = new SolidColorBrush(_downloadColor);
        UploadIndicator.Fill = new SolidColorBrush(_uploadColor);

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

    private void PrimeBaseline()
    {
        var (received, sent) = GetTotalBytes();
        _previousBytesReceived = received;
        _previousBytesSent = sent;
        _hasBaseline = true;
    }

    private void UpdateTraffic()
    {
        var (received, sent) = GetTotalBytes();

        if (!_hasBaseline)
        {
            _previousBytesReceived = received;
            _previousBytesSent = sent;
            _hasBaseline = true;
            return;
        }

        _downloadBytesPerSec = Math.Max(0, received - _previousBytesReceived);
        _uploadBytesPerSec = Math.Max(0, sent - _previousBytesSent);
        _previousBytesReceived = received;
        _previousBytesSent = sent;

        var maxCurrent = Math.Max(_downloadBytesPerSec, _uploadBytesPerSec);
        if (maxCurrent > _peakSpeed)
            _peakSpeed = maxCurrent;

        if (_peakSpeed > 0 && maxCurrent < _peakSpeed * 0.01)
            _peakSpeed = _peakSpeed * 0.95;

        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        TxtDownloadSpeed.Text = $"↓ {FormatSpeed(_downloadBytesPerSec)}";
        TxtDownloadSpeed.Foreground = new SolidColorBrush(_downloadColor);
        TxtUploadSpeed.Text = $"↑ {FormatSpeed(_uploadBytesPerSec)}";
        TxtUploadSpeed.Foreground = new SolidColorBrush(_uploadColor);

        DownloadBarFill.Background = new SolidColorBrush(_downloadColor);
        UploadBarFill.Background = new SolidColorBrush(_uploadColor);

        UpdateBars();
    }

    private void UpdateBars()
    {
        UpdateSingleBar(DownloadBarTrack, DownloadBarFill, _downloadBytesPerSec);
        UpdateSingleBar(UploadBarTrack, UploadBarFill, _uploadBytesPerSec);
    }

    private void UpdateSingleBar(System.Windows.Controls.Border track, System.Windows.Controls.Border fill, double speed)
    {
        if (track.ActualWidth <= 2)
            return;

        var width = Math.Max(0, track.ActualWidth - 2);
        var ratio = _peakSpeed > 0 ? Math.Clamp(speed / _peakSpeed, 0, 1) : 0;
        fill.Width = width * ratio;
    }

    private static string FormatSpeed(double bytesPerSec) => bytesPerSec switch
    {
        >= 1_073_741_824 => $"{bytesPerSec / 1_073_741_824:F1} GB/s",
        >= 1_048_576 => $"{bytesPerSec / 1_048_576:F1} MB/s",
        >= 1024 => $"{bytesPerSec / 1024:F1} KB/s",
        _ => $"{bytesPerSec:F0} B/s"
    };

    private static (long Received, long Sent) GetTotalBytes()
    {
        long totalReceived = 0;
        long totalSent = 0;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            var stats = nic.GetIPStatistics();
            totalReceived += stats.BytesReceived;
            totalSent += stats.BytesSent;
        }

        return (totalReceived, totalSent);
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
