using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;

namespace PrismPane_Widgets.Views;

public partial class SlideshowWidget : Window
{
    private const string SlideshowFolderKey = "SlideshowFolder";
    private const string SlideshowIntervalKey = "SlideshowInterval";
    private const string SlideshowRandomKey = "SlideshowRandom";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff", ".tif"
    };

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _slideTimer;
    private readonly Random _random = new();
    private readonly WidgetMinimizeBehavior _minimizeBehavior;

    private List<string> _imagePaths = [];
    private int _currentIndex = -1;
    private bool _randomOrder;

    public string WidgetId => _widgetId;

    public SlideshowWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        _minimizeBehavior = new WidgetMinimizeBehavior(this, RootBorder, InnerLayout, HeaderPanel, ContentPanel, BtnMinimize, _widgetSettings, _appSettings, Height);

        _slideTimer = new DispatcherTimer();
        _slideTimer.Tick += (_, _) => AdvanceSlide();

        ApplyWidgetSettingsFromModel();

        Loaded += (_, _) =>
        {
            if (_imagePaths.Count == 0 && string.IsNullOrWhiteSpace(GetFolder()))
                PromptForFolder();
        };

        Closed += (_, _) => _slideTimer.Stop();
    }

    private string GetFolder() =>
        _widgetSettings.Custom.TryGetValue(SlideshowFolderKey, out var folder) ? folder : string.Empty;

    public void PromptForFolder()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder containing images for the slideshow",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _widgetSettings.Custom[SlideshowFolderKey] = dlg.SelectedPath;
            _appSettings.Save();
            LoadImages();
        }
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
        TxtWidgetTitle.Text = !string.IsNullOrWhiteSpace(ws.Title) ? ws.Title : "Slideshow";

        if (ws.Width is > 0)
            Width = ws.Width.Value;
        if (ws.Height is > 0)
            Height = ws.Height.Value;

        _minimizeBehavior.ApplyFromSettings();

        var intervalSeconds = ws.Custom.TryGetValue(SlideshowIntervalKey, out var savedInterval)
            && double.TryParse(savedInterval, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 5.0;

        _randomOrder = ws.Custom.TryGetValue(SlideshowRandomKey, out var savedRandom)
            && string.Equals(savedRandom, "true", StringComparison.OrdinalIgnoreCase);

        _slideTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));

        LoadImages();
        _appSettings.Save();
    }

    private void LoadImages()
    {
        _slideTimer.Stop();
        _imagePaths.Clear();
        _currentIndex = -1;

        var folder = GetFolder();
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            _imagePaths = Directory.EnumerateFiles(folder)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (_imagePaths.Count > 0)
        {
            TxtNoImages.Visibility = Visibility.Collapsed;
            _currentIndex = _randomOrder ? _random.Next(_imagePaths.Count) : 0;
            ShowCurrentImage();
            if (_imagePaths.Count > 1)
                _slideTimer.Start();
        }
        else
        {
            ImgSlideshow.Source = null;
            TxtNoImages.Visibility = Visibility.Visible;
            TxtImageInfo.Text = string.Empty;
        }
    }

    private void AdvanceSlide()
    {
        if (_imagePaths.Count == 0) return;

        if (_randomOrder)
            _currentIndex = _random.Next(_imagePaths.Count);
        else
            _currentIndex = (_currentIndex + 1) % _imagePaths.Count;

        ShowCurrentImage();
    }

    private void ShowCurrentImage()
    {
        if (_currentIndex < 0 || _currentIndex >= _imagePaths.Count) return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(_imagePaths[_currentIndex], UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            ImgSlideshow.Source = bitmap;
            TxtImageInfo.Text = $"{_currentIndex + 1} / {_imagePaths.Count}";
        }
        catch
        {
            AdvanceSlide();
        }
    }

    private void BtnPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_imagePaths.Count == 0) return;
        _slideTimer.Stop();

        if (_randomOrder)
            _currentIndex = _random.Next(_imagePaths.Count);
        else
            _currentIndex = (_currentIndex - 1 + _imagePaths.Count) % _imagePaths.Count;

        ShowCurrentImage();
        if (_imagePaths.Count > 1)
            _slideTimer.Start();
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_imagePaths.Count == 0) return;
        _slideTimer.Stop();
        AdvanceSlide();
        if (_imagePaths.Count > 1)
            _slideTimer.Start();
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
