using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using FormsScreen = System.Windows.Forms.Screen;

namespace PrismPane_Widgets.Services;

internal sealed class WidgetMinimizeBehavior
{
    private readonly Window _window;
    private readonly Border _rootBorder;
    private readonly Grid _innerLayout;
    private readonly FrameworkElement _headerPanel;
    private readonly FrameworkElement _contentPanel;
    private readonly Button _minimizeButton;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _minimizeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Thickness _defaultHeaderMargin;
    private readonly double _fallbackExpandedHeight;

    private bool _isMinimized;
    private bool _isExpandedUpward;
    private bool _hasLoaded;
    private double _expandedHeight;
    private double _preHoverExpandTop;
    private bool _defaultTopmost;

    public WidgetMinimizeBehavior(
        Window window,
        Border rootBorder,
        Grid innerLayout,
        FrameworkElement headerPanel,
        FrameworkElement contentPanel,
        Button minimizeButton,
        WidgetSettings widgetSettings,
        AppSettings appSettings,
        double fallbackExpandedHeight)
    {
        _window = window;
        _rootBorder = rootBorder;
        _innerLayout = innerLayout;
        _headerPanel = headerPanel;
        _contentPanel = contentPanel;
        _minimizeButton = minimizeButton;
        _widgetSettings = widgetSettings;
        _appSettings = appSettings;
        _defaultHeaderMargin = headerPanel.Margin;
        _fallbackExpandedHeight = fallbackExpandedHeight;

        _minimizeButton.Click += BtnMinimize_Click;
        _window.MouseEnter += Window_MouseEnter;
        _window.MouseLeave += Window_MouseLeave;
        _window.Loaded += Window_Loaded;
        _window.Closed += (_, _) => _minimizeTimer.Stop();
        _minimizeTimer.Tick += MinimizeTimer_Tick;
    }

    public void ApplyFromSettings()
    {
        var previousMinimized = _isMinimized;
        _defaultTopmost = _widgetSettings.Topmost;
        _isMinimized = _widgetSettings.IsMinimized;
        _expandedHeight = _widgetSettings.ExpandedHeight ?? 0;

        UpdateMinimizeButtonVisual();

        if (!_hasLoaded)
            return;

        if (_isMinimized && !previousMinimized)
            CollapseToMinimize();
        else if (!_isMinimized && previousMinimized)
            ExpandFromMinimize();
        else if (_isMinimized)
            CollapseToMinimize();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _hasLoaded = true;
        if (_isMinimized)
            CollapseToMinimize();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (_isMinimized)
        {
            _isMinimized = false;
            ExpandFromMinimize();
        }
        else
        {
            _expandedHeight = Math.Max(_window.Height, ResolveExpandedHeight());
            _isMinimized = true;
            CollapseToMinimize();
        }

        UpdateMinimizeButtonVisual();
        PersistState();
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _window.Topmost = true;
        if (!_isMinimized)
            return;

        _minimizeTimer.Stop();

        var expandedHeight = ResolveExpandedHeight();
        var screen = FormsScreen.FromPoint(new System.Drawing.Point((int)Math.Round(_window.Left), (int)Math.Round(_window.Top))).WorkingArea;

        if (_window.Top + expandedHeight > screen.Bottom)
        {
            _preHoverExpandTop = _window.Top;
            _isExpandedUpward = true;

            Grid.SetRow(_headerPanel, 1);
            Grid.SetRow(_contentPanel, 0);
            _innerLayout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            _innerLayout.RowDefinitions[1].Height = GridLength.Auto;
            _headerPanel.Margin = new Thickness(_defaultHeaderMargin.Left, 6, _defaultHeaderMargin.Right, 0);

            _contentPanel.Visibility = Visibility.Visible;
            _window.Height = expandedHeight;
            _window.Top = _window.Top + GetMinimizedHeight() - expandedHeight;
        }
        else
        {
            ExpandFromMinimize();
        }
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _window.Topmost = _defaultTopmost;
        if (!_isMinimized)
            return;

        _minimizeTimer.Stop();
        _minimizeTimer.Start();
    }

    private void MinimizeTimer_Tick(object? sender, EventArgs e)
    {
        _minimizeTimer.Stop();
        if (_isMinimized && !_window.IsMouseOver)
            CollapseToMinimize();
    }

    private void CollapseToMinimize()
    {
        if (_isExpandedUpward)
        {
            _window.Top = _preHoverExpandTop;
            ResetUpwardExpansion();
        }

        _contentPanel.Visibility = Visibility.Collapsed;
        _window.UpdateLayout();
        _window.Height = GetMinimizedHeight();
    }

    private void ExpandFromMinimize()
    {
        _contentPanel.Visibility = Visibility.Visible;
        _window.Height = ResolveExpandedHeight();
    }

    private double ResolveExpandedHeight()
    {
        if (_expandedHeight > 0)
            return _expandedHeight;
        if (_widgetSettings.Height is > 0)
            return _widgetSettings.Height.Value;
        if (_window.Height > 0)
            return _window.Height;
        return _fallbackExpandedHeight;
    }

    private void ResetUpwardExpansion()
    {
        Grid.SetRow(_headerPanel, 0);
        Grid.SetRow(_contentPanel, 1);
        _innerLayout.RowDefinitions[0].Height = GridLength.Auto;
        _innerLayout.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        _headerPanel.Margin = _defaultHeaderMargin;
        _isExpandedUpward = false;
    }

    private double GetMinimizedHeight()
    {
        _headerPanel.UpdateLayout();
        var headerHeight = _headerPanel.ActualHeight;
        var rootMargin = _rootBorder.Margin.Top + _rootBorder.Margin.Bottom;
        var innerMargin = _innerLayout.Margin.Top + _innerLayout.Margin.Bottom;
        var border = _rootBorder.BorderThickness.Top + _rootBorder.BorderThickness.Bottom;
        return Math.Max(0, headerHeight + rootMargin + innerMargin + border);
    }

    private void UpdateMinimizeButtonVisual()
    {
        _minimizeButton.Content = _isMinimized ? "▢" : "—";
        _minimizeButton.ToolTip = _isMinimized ? "Restore" : "Minimize";
    }

    private void PersistState()
    {
        _widgetSettings.IsMinimized = _isMinimized;
        _widgetSettings.ExpandedHeight = _expandedHeight > 0 ? _expandedHeight : null;
        _appSettings.Save();
    }
}
