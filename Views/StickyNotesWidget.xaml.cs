using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;

namespace PrismPane_Widgets.Views;

public partial class StickyNotesWidget : Window
{
    private const string NoteContentKey = "NoteContent";
    private const string NoteFontFamilyKey = "NoteFontFamily";
    private const string NoteFontSizeKey = "NoteFontSize";
    private const string NoteFontColorKey = "NoteFontColor";

    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _saveTimer;
    private readonly WidgetMinimizeBehavior _minimizeBehavior;
    private bool _loading = true;

    public string WidgetId => _widgetId;

    public StickyNotesWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        _minimizeBehavior = new WidgetMinimizeBehavior(this, RootBorder, InnerLayout, HeaderPanel, ContentPanel, BtnMinimize, _widgetSettings, _appSettings, Height);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            PersistNoteContent();
        };

        ApplyWidgetSettingsFromModel();
        _loading = false;

        Closed += (_, _) =>
        {
            _saveTimer.Stop();
            PersistNoteContent();
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
        TxtWidgetTitle.Text = !string.IsNullOrWhiteSpace(ws.Title) ? ws.Title : "Sticky Notes";

        if (ws.Width is > 0)
            Width = ws.Width.Value;
        if (ws.Height is > 0)
            Height = ws.Height.Value;

        _minimizeBehavior.ApplyFromSettings();

        var fontFamily = ws.Custom.TryGetValue(NoteFontFamilyKey, out var savedFont) && !string.IsNullOrWhiteSpace(savedFont)
            ? savedFont
            : "Segoe UI";
        TxtNoteContent.FontFamily = new System.Windows.Media.FontFamily(fontFamily);

        var fontSize = ws.Custom.TryGetValue(NoteFontSizeKey, out var savedSize) && double.TryParse(savedSize, System.Globalization.CultureInfo.InvariantCulture, out var parsedSize)
            ? Math.Clamp(parsedSize, 8, 45)
            : 14.0;
        TxtNoteContent.FontSize = fontSize;

        if (ws.Custom.TryGetValue(NoteFontColorKey, out var savedColor) && !string.IsNullOrWhiteSpace(savedColor))
        {
            try { TxtNoteContent.Foreground = new SolidColorBrush(ThemeHelper.ParseColor(savedColor)); }
            catch { }
        }

        _loading = true;
        TxtNoteContent.Text = ws.Custom.TryGetValue(NoteContentKey, out var content) ? content : string.Empty;
        _loading = false;

        _appSettings.Save();
    }

    private void PersistNoteContent()
    {
        _widgetSettings.Custom[NoteContentKey] = TxtNoteContent.Text;
        _appSettings.Save();
    }

    private void TxtNoteContent_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading) return;
        _saveTimer.Stop();
        _saveTimer.Start();
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
