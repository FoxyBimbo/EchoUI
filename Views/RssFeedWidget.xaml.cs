using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;

namespace PrismPane_Widgets.Views;

public partial class RssFeedWidget : Window
{
    private const string RssFeedUrlKey = "RssFeedUrl";
    private const string RssMaxItemsKey = "RssMaxItems";
    private const string RssRefreshIntervalKey = "RssRefreshInterval";
    private const string RssRefreshUnitKey = "RssRefreshUnit";

    private static readonly HttpClient RssHttpClient = CreateRssHttpClient();

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _refreshTimer;
    private readonly WidgetMinimizeBehavior _minimizeBehavior;

    private string _feedUrl = string.Empty;
    private int _maxItems = 15;

    public string WidgetId => _widgetId;

    public RssFeedWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        _minimizeBehavior = new WidgetMinimizeBehavior(this, RootBorder, InnerLayout, HeaderPanel, ContentPanel, BtnMinimize, _widgetSettings, _appSettings, Height);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(3) };
        _refreshTimer.Tick += (_, _) => _ = RefreshFeedAsync();

        ApplyWidgetSettingsFromModel();

        Loaded += async (_, _) =>
        {
            await RefreshFeedAsync();
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
            TxtFeedTitle.Text = ws.Title;

        if (ws.Width is > 0)
            Width = ws.Width.Value;
        if (ws.Height is > 0)
            Height = ws.Height.Value;

        _minimizeBehavior.ApplyFromSettings();

        _feedUrl = ws.Custom.TryGetValue(RssFeedUrlKey, out var savedUrl) && !string.IsNullOrWhiteSpace(savedUrl)
            ? savedUrl
            : "https://feeds.bbci.co.uk/news/rss.xml";

        _maxItems = ws.Custom.TryGetValue(RssMaxItemsKey, out var savedMax) && int.TryParse(savedMax, out var parsed)
            ? Math.Clamp(parsed, 1, 100)
            : 15;

        var intervalValue = ws.Custom.TryGetValue(RssRefreshIntervalKey, out var savedInterval)
            && double.TryParse(savedInterval, System.Globalization.CultureInfo.InvariantCulture, out var parsedInterval)
            ? Math.Max(1, parsedInterval)
            : 3.0;
        var unit = ws.Custom.TryGetValue(RssRefreshUnitKey, out var savedUnit) ? savedUnit : "Hours";
        _refreshTimer.Stop();
        _refreshTimer.Interval = unit switch
        {
            "Seconds" => TimeSpan.FromSeconds(intervalValue),
            "Minutes" => TimeSpan.FromMinutes(intervalValue),
            "Days" => TimeSpan.FromDays(intervalValue),
            _ => TimeSpan.FromHours(intervalValue)
        };
        _refreshTimer.Start();

        _appSettings.Save();
        _ = RefreshFeedAsync();
    }

    private async Task RefreshFeedAsync()
    {
        if (string.IsNullOrWhiteSpace(_feedUrl))
        {
            TxtStatus.Text = "No feed URL configured.";
            return;
        }

        TxtStatus.Text = "Loading…";

        try
        {
            var xml = await RssHttpClient.GetStringAsync(_feedUrl);
            var doc = XDocument.Parse(xml);

            var items = new List<RssFeedItem>();
            string feedTitle = "RSS Feed";

            // RSS 2.0
            var channel = doc.Descendants("channel").FirstOrDefault();
            if (channel is not null)
            {
                feedTitle = channel.Element("title")?.Value ?? feedTitle;

                foreach (var item in channel.Descendants("item").Take(_maxItems))
                {
                    var title = item.Element("title")?.Value ?? "(no title)";
                    var link = item.Element("link")?.Value ?? string.Empty;
                    var pubDate = item.Element("pubDate")?.Value ?? string.Empty;

                    items.Add(new RssFeedItem
                    {
                        Title = title,
                        Link = link,
                        Published = FormatDate(pubDate)
                    });
                }
            }

            // Atom fallback
            if (items.Count == 0)
            {
                XNamespace atom = "http://www.w3.org/2005/Atom";
                var feed = doc.Element(atom + "feed");
                if (feed is not null)
                {
                    feedTitle = feed.Element(atom + "title")?.Value ?? feedTitle;

                    foreach (var entry in feed.Elements(atom + "entry").Take(_maxItems))
                    {
                        var title = entry.Element(atom + "title")?.Value ?? "(no title)";
                        var link = entry.Element(atom + "link")?.Attribute("href")?.Value ?? string.Empty;
                        var updated = entry.Element(atom + "updated")?.Value ?? string.Empty;

                        items.Add(new RssFeedItem
                        {
                            Title = title,
                            Link = link,
                            Published = FormatDate(updated)
                        });
                    }
                }
            }

            TxtFeedTitle.Text = feedTitle;
            LstFeedItems.ItemsSource = items;
            TxtStatus.Text = $"Updated {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error: {ex.Message}";
        }
    }

    private static string FormatDate(string raw)
    {
        if (DateTime.TryParse(raw, out var dt))
            return dt.ToString("g");
        return raw;
    }

    private void LstFeedItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstFeedItems.SelectedItem is RssFeedItem item && !string.IsNullOrWhiteSpace(item.Link))
        {
            try
            {
                Process.Start(new ProcessStartInfo(item.Link) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshFeedAsync();

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

    private static HttpClient CreateRssHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PrismPaneWidgets/1.0");
        return client;
    }

    private sealed class RssFeedItem
    {
        public string Title { get; init; } = string.Empty;
        public string Link { get; init; } = string.Empty;
        public string Published { get; init; } = string.Empty;
    }
}
