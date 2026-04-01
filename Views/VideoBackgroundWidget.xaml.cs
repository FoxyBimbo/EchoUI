using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;

namespace PrismPane_Widgets.Views;

public partial class VideoBackgroundWidget : Window
{
    private const string SourcePathKey = "VideoBackgroundSourcePath";

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;

    private readonly DispatcherTimer _gifTimer = new();
    private List<BitmapSource>? _gifFrames;
    private List<int>? _gifDelaysMs;
    private int _gifFrameIndex;

    public string WidgetId => _widgetId;

    public VideoBackgroundWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;

        Loaded += (_, _) => ApplyWidgetSettingsFromModel();
        Closed += (_, _) =>
        {
            _gifTimer.Stop();
            StopPlayback();
        };

        _gifTimer.Tick += (_, _) => AdvanceGifFrame();
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

        if (!ws.Custom.TryGetValue(SourcePathKey, out var sourcePath) || string.IsNullOrWhiteSpace(sourcePath))
        {
            StopPlayback();
            ShowPlaceholder("Select a video or GIF in settings.");
            return;
        }

        if (!File.Exists(sourcePath))
        {
            StopPlayback();
            ShowPlaceholder("The selected video or GIF file could not be found.");
            return;
        }

        if (string.Equals(Path.GetExtension(sourcePath), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            StartGifPlayback(sourcePath);
            return;
        }

        StartVideoPlayback(sourcePath);
    }

    private WidgetSettings SyncWidgetSettings()
    {
        _appSettings.Widgets[_widgetId] = _widgetSettings;
        return _widgetSettings;
    }

    private void StartVideoPlayback(string sourcePath)
    {
        _gifTimer.Stop();
        _gifFrames = null;
        _gifDelaysMs = null;
        _gifFrameIndex = 0;

        GifPlayer.Visibility = Visibility.Collapsed;
        VideoPlayer.Visibility = Visibility.Visible;
        TxtPlaceholder.Visibility = Visibility.Collapsed;

        VideoPlayer.Source = new Uri(sourcePath, UriKind.Absolute);
        VideoPlayer.Position = TimeSpan.Zero;
        VideoPlayer.Volume = 0;
        VideoPlayer.IsMuted = true;
        VideoPlayer.Play();
    }

    private void StartGifPlayback(string sourcePath)
    {
        VideoPlayer.Stop();
        VideoPlayer.Source = null;
        VideoPlayer.Visibility = Visibility.Collapsed;
        GifPlayer.Visibility = Visibility.Visible;
        TxtPlaceholder.Visibility = Visibility.Collapsed;

        var decoder = new GifBitmapDecoder(new Uri(sourcePath, UriKind.Absolute), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            StopPlayback();
            return;
        }

        _gifFrames = decoder.Frames.Cast<BitmapSource>().ToList();
        foreach (var frame in _gifFrames)
            frame.Freeze();

        _gifDelaysMs = _gifFrames.Select(GetGifFrameDelayMs).ToList();
        _gifFrameIndex = 0;
        GifPlayer.Source = _gifFrames[0];

        _gifTimer.Interval = TimeSpan.FromMilliseconds(_gifDelaysMs[0]);
        _gifTimer.Start();
    }

    private void AdvanceGifFrame()
    {
        if (_gifFrames is null || _gifFrames.Count == 0 || _gifDelaysMs is null || _gifDelaysMs.Count == 0)
            return;

        _gifFrameIndex = (_gifFrameIndex + 1) % _gifFrames.Count;
        GifPlayer.Source = _gifFrames[_gifFrameIndex];
        _gifTimer.Interval = TimeSpan.FromMilliseconds(_gifDelaysMs[_gifFrameIndex]);
    }

    private static int GetGifFrameDelayMs(BitmapSource frame)
    {
        try
        {
            if (frame.Metadata is not BitmapMetadata metadata)
                return 100;

            var rawDelay = metadata.GetQuery("/grctlext/Delay");
            if (rawDelay is not ushort delay)
                return 100;

            var ms = delay * 10;
            return ms <= 0 ? 100 : ms;
        }
        catch
        {
            return 100;
        }
    }

    private void StopPlayback()
    {
        _gifTimer.Stop();
        _gifFrames = null;
        _gifDelaysMs = null;
        _gifFrameIndex = 0;

        VideoPlayer.Stop();
        VideoPlayer.Source = null;
        GifPlayer.Source = null;
        VideoPlayer.Visibility = Visibility.Visible;
        GifPlayer.Visibility = Visibility.Collapsed;
    }

    private void ShowPlaceholder(string message)
    {
        TxtPlaceholder.Text = message;
        TxtPlaceholder.Visibility = Visibility.Visible;
    }

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Position = TimeSpan.Zero;
        VideoPlayer.Play();
    }

    private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        StopPlayback();
        ShowPlaceholder("The selected media could not be played.");
    }

    private void RootBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
        }
        catch
        {
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
