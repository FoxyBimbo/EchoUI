using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Button = System.Windows.Controls.Button;

namespace PrismPane_Widgets.Views;

public partial class ShortcutPanelWidget : Window
{
    private const string MinimizedKey = "IsMinimized";
    private const string LegacyCollapsedKey = "IsCollapsed";
    private const string LegacyExpandedHeightKey = "ExpandedHeight";
    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private bool _isMinimized;
    private double _expandedHeight;
    private bool _titleDragging;
    private System.Windows.Point _titleDragStart;
    private bool _isDocked;
    private DockEdge _currentEdge = DockEdge.None;
    private const int AutoDockThreshold = 2;
    private bool _defaultTopmost;
    private readonly DispatcherTimer _minimizeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _isResizing;
    private System.Windows.Point _resizeStart;
    private int _resizeOriginalThickness;
    private bool _isExpandedUpward;
    private double _preHoverExpandTop;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    public string WidgetId => _widgetId;
    public void RestoreDock(DockEdge edge, double thickness)
    {
        if (edge == DockEdge.None)
            return;

        DockTo(edge);
        if (thickness > 0)
            MainWindow.DockManager.Resize(WidgetId, MainWindow.DockManager.DipToPixel(thickness));
    }

    public ShortcutPanelWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        _minimizeTimer.Tick += MinimizeTimer_Tick;

        ApplyWidgetSettingsFromModel();
        Loaded += (_, _) => ApplySavedMinimizeState();

        Closed += (_, _) => ReleaseResources();
    }

    private WidgetSettings SyncWidgetSettings()
    {
        var ws = _appSettings.GetWidgetSettings(_widgetId);
        _widgetSettings.Kind = ws.Kind;
        _widgetSettings.Opacity = ws.Opacity;
        _widgetSettings.Topmost = ws.Topmost;
        _widgetSettings.Custom = ws.Custom;
        _widgetSettings.Shortcuts = ws.Shortcuts;
        _widgetSettings.ViewMode = ws.ViewMode;
        _widgetSettings.CustomColors = ws.CustomColors;
        return ws;
    }

    public void ApplyWidgetSettingsFromModel()
    {
        var ws = SyncWidgetSettings();
        var previousMinimized = _isMinimized;
        ThemeHelper.ApplyToElement(this, ws.CustomColors);
        Topmost = ws.Topmost;
        _defaultTopmost = ws.Topmost;
        Opacity = ws.Opacity;
        RootBorder.BorderThickness = ws.ShowBorder ? new Thickness(1.5) : new Thickness(0);

        var displayTitle = !string.IsNullOrWhiteSpace(ws.Title) ? ws.Title : null;
        if (displayTitle is null)
        {
            ws.Custom.TryGetValue("Title", out var legacyTitle);
            displayTitle = string.IsNullOrEmpty(legacyTitle) ? "Shortcuts" : legacyTitle;
        }
        TxtTitle.Text = displayTitle;

        _isMinimized = ws.IsMinimized;
        if (!_isMinimized
            && ws.Custom.TryGetValue(MinimizedKey, out var minimizedValue)
            && bool.TryParse(minimizedValue, out var minimized))
            _isMinimized = minimized;
        if (!_isMinimized
            && ws.Custom.TryGetValue(LegacyCollapsedKey, out var collapsedValue)
            && bool.TryParse(collapsedValue, out var collapsed))
            _isMinimized = collapsed;
        ws.Custom.Remove(LegacyCollapsedKey);

        _expandedHeight = ws.ExpandedHeight ?? 0;
        if (ws.Custom.TryGetValue(LegacyExpandedHeightKey, out var expandedValue)
            && double.TryParse(expandedValue, out var expandedHeight))
            _expandedHeight = expandedHeight;

        BtnMinimize.Content = _isMinimized ? "▢" : "—";
        BtnMinimize.ToolTip = _isMinimized ? "Restore" : "Minimize";

        if (IsLoaded)
        {
            if (_isMinimized && !previousMinimized)
                CollapseToMinimize();
            else if (!_isMinimized && previousMinimized)
                ExpandFromMinimize();
        }

        LoadShortcuts();
    }

    private void ApplySavedMinimizeState()
    {
        if (_isMinimized)
            CollapseToMinimize();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var ws = SyncWidgetSettings();
        if (ws.Left.HasValue && ws.Top.HasValue)
        {
            Left = ws.Left.Value;
            Top = ws.Top.Value;
            EnsureOnScreen();
            return;
        }
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        Left = (screen.Width - Width) / 2;
        Top = (screen.Height - Height) / 2;
    }

    private void EnsureOnScreen()
    {
        if (double.IsNaN(Width) || double.IsNaN(Height))
            return;

        var rect = new System.Drawing.Rectangle(
            (int)Math.Round(Left),
            (int)Math.Round(Top),
            (int)Math.Round(Width),
            (int)Math.Round(Height));

        bool onScreen = System.Windows.Forms.Screen.AllScreens
            .Any(s => rect.IntersectsWith(s.WorkingArea));

        if (!onScreen)
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            Left = primary.Left;
            Top = primary.Top;
        }
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Topmost = true;
        if (!_isMinimized)
            return;

        _minimizeTimer.Stop();

        var expandedHeight = _expandedHeight > 0 ? _expandedHeight : 320;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)Left, (int)Top)).WorkingArea;

        if (Top + expandedHeight > screen.Bottom)
        {
            _preHoverExpandTop = Top;
            _isExpandedUpward = true;

            Grid.SetRow(HeaderPanel, 1);
            Grid.SetRow(ContentPanel, 0);
            InnerLayout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            InnerLayout.RowDefinitions[1].Height = GridLength.Auto;
            HeaderPanel.Margin = new Thickness(0, 6, 0, 0);

            ContentPanel.Visibility = Visibility.Visible;
            Height = expandedHeight;
            Top = Top + GetMinimizedHeight() - expandedHeight;
        }
        else
        {
            ExpandFromMinimize();
        }
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Topmost = _defaultTopmost;
        if (!_isMinimized)
            return;

        _minimizeTimer.Stop();
        _minimizeTimer.Start();
    }

    // ── Load / Refresh ──────────────────────────────────────
    private void LoadShortcuts()
    {
        foreach (var s in _widgetSettings.Shortcuts)
            s.IconImage = ResolveIcon(s);

        ShortcutList.ItemsSource = null;
        ShortcutList.ItemsSource = _widgetSettings.Shortcuts;
    }

    private static ImageSource? ResolveIcon(ShortcutItem s)
    {
        if (!string.IsNullOrEmpty(s.CustomIconPath) && File.Exists(s.CustomIconPath))
            return IconHelper.GetIconForPath(s.CustomIconPath);

        if (!string.IsNullOrEmpty(s.TargetPath))
        {
            if (File.Exists(s.TargetPath) || Directory.Exists(s.TargetPath))
                return IconHelper.GetIconForPath(s.TargetPath);
        }

        return null;
    }

    // ── Title: drag to move, click to collapse, double-click to edit ──
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            TitleEditPanel.Visibility = Visibility.Visible;
            TxtTitleEdit.Text = TxtTitle.Text;
            TxtTitleEdit.Focus();
            TxtTitleEdit.SelectAll();
            e.Handled = true;
            return;
        }

        _titleDragging = false;
        _titleDragStart = e.GetPosition(this);
        e.Handled = true;
    }

    private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(this);
        var diff = pos - _titleDragStart;

        if (!_titleDragging &&
            (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
             Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
        {
            _titleDragging = true;
            if (MainWindow.DragWidgetGroup(this))
                return;
            if (_isDocked)
                Undock(true);
            DragMove();
            if (!MainWindow.SnapManager.OnDragCompleted(this))
            {
                ResetUpwardExpansion();
                TryAutoDockFromPosition();
            }
        }
    }

    private void TitleBar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_titleDragging)
        {
            _titleDragging = false;
            TryAutoDockFromPosition();
            e.Handled = true;
        }
    }

    private void CollapseToMinimize()
    {
        if (_isExpandedUpward)
        {
            Top = _preHoverExpandTop;
            ResetUpwardExpansion();
        }
        ContentPanel.Visibility = Visibility.Collapsed;
        UpdateLayout();
        Height = GetMinimizedHeight();
    }

    private void ExpandFromMinimize()
    {
        ContentPanel.Visibility = Visibility.Visible;
        Height = _expandedHeight > 0 ? _expandedHeight : 320;
    }

    private double GetMinimizedHeight()
    {
        HeaderPanel.UpdateLayout();
        var headerHeight = HeaderPanel.ActualHeight;
        var rootMargin = RootBorder.Margin.Top + RootBorder.Margin.Bottom;
        var innerMargin = InnerLayout.Margin.Top + InnerLayout.Margin.Bottom;
        var border = RootBorder.BorderThickness.Top + RootBorder.BorderThickness.Bottom;
        return Math.Max(0, headerHeight + rootMargin + innerMargin + border);
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
            _expandedHeight = Height;
            _isMinimized = true;
            CollapseToMinimize();
        }

        BtnMinimize.Content = _isMinimized ? "▢" : "—";
        BtnMinimize.ToolTip = _isMinimized ? "Restore" : "Minimize";
        PersistShortcutState();
    }

    private void MinimizeTimer_Tick(object? sender, EventArgs e)
    {
        _minimizeTimer.Stop();
        if (_isMinimized && !IsMouseOver)
            CollapseToMinimize();
    }

    // ── Title editing ───────────────────────────────────────
    private void BtnTitleSave_Click(object sender, RoutedEventArgs e)
    {
        var newTitle = TxtTitleEdit.Text.Trim();
        if (!string.IsNullOrEmpty(newTitle))
        {
            TxtTitle.Text = newTitle;
            _widgetSettings.Custom["Title"] = newTitle;
            PersistShortcutState();
        }
        TitleEditPanel.Visibility = Visibility.Collapsed;
    }

    // ── Add shortcut ────────────────────────────────────────
    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ShortcutEditDialog { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _widgetSettings.Shortcuts.Add(dlg.Result);
            LoadShortcuts();
            PersistShortcutState();
        }
    }

    // ── Dock / Undock ───────────────────────────────────────
    private void BtnDock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string edgeName &&
            Enum.TryParse<DockEdge>(edgeName, out var edge))
        {
            if (_isDocked && edge == _currentEdge)
                Undock();
            else
                DockTo(edge);
        }
    }

    private void BtnUndock_Click(object sender, RoutedEventArgs e)
    {
        Undock();
    }

    private void DockTo(DockEdge edge)
    {
        _currentEdge = edge;
        _isDocked = true;

        RootBorder.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        RootBorder.CornerRadius = new CornerRadius(0);
        RootBorder.Margin = new Thickness(0);
        RootBorder.Effect = null;

        double thickness = edge is DockEdge.Left or DockEdge.Right ? 260 : 180;

        MainWindow.DockManager.Dock(WidgetId, this, edge, thickness);
        HighlightActiveEdge(edge);
        ShowResizeGrip(edge);
    }

    private void Undock(bool preservePosition = false)
    {
        var currentLeft = Left;
        var currentTop = Top;
        _isDocked = false;
        _currentEdge = DockEdge.None;
        MainWindow.DockManager.Undock(WidgetId);

        RootBorder.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundSemiBrush");
        RootBorder.CornerRadius = new CornerRadius(14);
        RootBorder.Margin = new Thickness(8);
        RootBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black
        };

        Width = 260;
        Height = 320;
        if (preservePosition)
        {
            Left = currentLeft;
            Top = currentTop;
        }
        else
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            Left = (screen.Width - Width) / 2;
            Top = (screen.Height - Height) / 2;
        }
        HighlightActiveEdge(DockEdge.None);
        DockResizeGrip.Visibility = Visibility.Collapsed;
    }

    private void ShowResizeGrip(DockEdge edge)
    {
        DockResizeGrip.Visibility = Visibility.Visible;

        switch (edge)
        {
            case DockEdge.Left:
                DockResizeGrip.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                DockResizeGrip.VerticalAlignment = VerticalAlignment.Stretch;
                DockResizeGrip.Width = 6;
                DockResizeGrip.Height = double.NaN;
                DockResizeGrip.Cursor = System.Windows.Input.Cursors.SizeWE;
                break;
            case DockEdge.Right:
                DockResizeGrip.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                DockResizeGrip.VerticalAlignment = VerticalAlignment.Stretch;
                DockResizeGrip.Width = 6;
                DockResizeGrip.Height = double.NaN;
                DockResizeGrip.Cursor = System.Windows.Input.Cursors.SizeWE;
                break;
            case DockEdge.Top:
                DockResizeGrip.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                DockResizeGrip.VerticalAlignment = VerticalAlignment.Bottom;
                DockResizeGrip.Width = double.NaN;
                DockResizeGrip.Height = 6;
                DockResizeGrip.Cursor = System.Windows.Input.Cursors.SizeNS;
                break;
            case DockEdge.Bottom:
                DockResizeGrip.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                DockResizeGrip.VerticalAlignment = VerticalAlignment.Top;
                DockResizeGrip.Width = double.NaN;
                DockResizeGrip.Height = 6;
                DockResizeGrip.Cursor = System.Windows.Input.Cursors.SizeNS;
                break;
        }
    }

    private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isDocked || e.LeftButton != MouseButtonState.Pressed) return;
        _isResizing = true;
        if (!GetCursorPos(out var pt))
            return;
        _resizeStart = new System.Windows.Point(pt.x, pt.y);
        _resizeOriginalThickness = MainWindow.DockManager.DipToPixel(
            _currentEdge is DockEdge.Left or DockEdge.Right ? ActualWidth : ActualHeight);
        ((Border)sender).CaptureMouse();
    }

    private void ResizeGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isResizing) return;

        if (!GetCursorPos(out var pt))
            return;
        int dx = pt.x - (int)_resizeStart.X;
        int dy = pt.y - (int)_resizeStart.Y;

        int newThickness = _currentEdge switch
        {
            DockEdge.Left => _resizeOriginalThickness + dx,
            DockEdge.Right => _resizeOriginalThickness - dx,
            DockEdge.Top => _resizeOriginalThickness + dy,
            DockEdge.Bottom => _resizeOriginalThickness - dy,
            _ => _resizeOriginalThickness
        };

        if (newThickness < 150) newThickness = 150;
        MainWindow.DockManager.Resize(WidgetId, newThickness);
    }

    private void ResizeGrip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isResizing = false;
        ((Border)sender).ReleaseMouseCapture();
    }

    private void HighlightActiveEdge(DockEdge edge)
    {
    }

    private DockEdge GetAutoDockEdgeFromCursor()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var cursor = System.Windows.Forms.Cursor.Position;

        int leftDistance = Math.Abs(cursor.X - screen.Left);
        int rightDistance = Math.Abs(screen.Right - cursor.X);
        int topDistance = Math.Abs(cursor.Y - screen.Top);
        int bottomDistance = Math.Abs(screen.Bottom - cursor.Y);

        int min = Math.Min(Math.Min(leftDistance, rightDistance), Math.Min(topDistance, bottomDistance));
        if (min > AutoDockThreshold)
            return DockEdge.None;

        if (min == leftDistance) return DockEdge.Left;
        if (min == rightDistance) return DockEdge.Right;
        if (min == topDistance) return DockEdge.Top;
        return DockEdge.Bottom;
    }

    private void TryAutoDockFromPosition()
    {
        if (_isDocked)
            return;

        var edge = GetAutoDockEdgeFromCursor();
        if (edge != DockEdge.None)
            DockTo(edge);
    }

    // ── Launch shortcut (single click) ──────────────────────
    private void Shortcut_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1) return;
        if (sender is FrameworkElement fe && fe.Tag is ShortcutItem s)
        {
            e.Handled = true;
            LaunchShortcut(s);
        }
    }

    private static void LaunchShortcut(ShortcutItem s)
    {
        if (!string.IsNullOrWhiteSpace(s.Url))
        {
            try { Process.Start(new ProcessStartInfo(s.Url) { UseShellExecute = true }); }
            catch { }
            return;
        }

        if (string.IsNullOrWhiteSpace(s.TargetPath)) return;
        try
        {
            var psi = new ProcessStartInfo(s.TargetPath) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(s.Arguments))
                psi.Arguments = s.Arguments;
            Process.Start(psi);
        }
        catch { }
    }

    // ── Right-click or ⋮ menu ───────────────────────────────
    private void Shortcut_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ShortcutItem s)
        {
            e.Handled = true;
            ShowShortcutContextMenu(s, fe);
        }
    }

    private void BtnShortcutMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ShortcutItem s)
            ShowShortcutContextMenu(s, fe);
    }

    private void ShowShortcutContextMenu(ShortcutItem shortcut, FrameworkElement anchor)
    {
        var menu = new ContextMenu();

        var edit = new MenuItem { Header = "Edit…" };
        edit.Click += (_, _) =>
        {
            var dlg = new ShortcutEditDialog(shortcut) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                LoadShortcuts();
                PersistShortcutState();
            }
        };
        menu.Items.Add(edit);

        var del = new MenuItem { Header = "Delete" };
        del.Click += (_, _) =>
        {
            _widgetSettings.Shortcuts.Remove(shortcut);
            LoadShortcuts();
            PersistShortcutState();
        };
        menu.Items.Add(del);

        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    // ── Close / cleanup ─────────────────────────────────────
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.DockManager.Undock(_widgetId);
        Close();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_appSettings, null, _widgetId, _widgetSettings, ApplyWidgetSettingsFromModel)
        {
            Owner = this
        };
        win.ShowDialog();
    }

    private void ReleaseResources()
    {
        foreach (var s in _widgetSettings.Shortcuts)
            s.IconImage = null;
        ShortcutList.ItemsSource = null;
        RootBorder.Effect = null;
        RootBorder.Child = null;
        Content = null;
    }

    private void PersistShortcutState()
    {
        var ws = _appSettings.GetWidgetSettings(_widgetId);
        ws.Custom["Title"] = TxtTitle.Text;
        ws.Custom.Remove(LegacyCollapsedKey);
        ws.Custom.Remove(MinimizedKey);
        ws.Custom.Remove(LegacyExpandedHeightKey);
        ws.IsMinimized = _isMinimized;
        _widgetSettings.IsMinimized = _isMinimized;
        if (_expandedHeight > 0)
        {
            ws.ExpandedHeight = _expandedHeight;
            _widgetSettings.ExpandedHeight = _expandedHeight;
        }
        else
        {
            ws.ExpandedHeight = null;
            _widgetSettings.ExpandedHeight = null;
        }
        _appSettings.Save();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (MainWindow.DragWidgetGroup(this))
            return;

        if (_isDocked)
            Undock(true);

        DragMove();
        if (!MainWindow.SnapManager.OnDragCompleted(this))
        {
            ResetUpwardExpansion();
            TryAutoDockFromPosition();
        }
    }

    private void ResetUpwardExpansion()
    {
        if (!_isExpandedUpward)
            return;

        Grid.SetRow(HeaderPanel, 0);
        Grid.SetRow(ContentPanel, 1);
        InnerLayout.RowDefinitions[0].Height = GridLength.Auto;
        InnerLayout.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        HeaderPanel.Margin = new Thickness(0, 0, 0, 6);
        _isExpandedUpward = false;
    }
}
