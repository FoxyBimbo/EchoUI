using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace PrismPane_Widgets.Views;

public partial class DesktopFolderWidget : Window
{
    private const bool EnableStateDiagnostics = true;
    private const string LegacyFolderKey = "DefaultFolder";
    private const string LegacyActiveFolderKey = "ActiveFolder";
    private const string SortKey = "DefaultSort";
    private const string SortDescendingKey = "SortDescending";
    private const string MinimizedKey = "IsMinimized";
    private const string LegacyExpandedHeightKey = "ExpandedHeight";
    private readonly string _widgetId;
    private string _folderPath;
    private DockEdge _currentEdge = DockEdge.None;
    private bool _isDocked;
    private SortMode _sortMode = SortMode.Name;
    private bool _sortDescending;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private const int AutoDockThreshold = 2;
    private readonly DispatcherTimer _minimizeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _isMinimized;
    private bool _hasLoadedMinimizeMode;
    private bool _isInitializing = true;
    private double _expandedHeight;
    private bool _defaultTopmost;

    // ── Drag state ───────────────────────────────────────────
    private System.Windows.Point _dragStartPoint;
    private string? _dragItemPath;
    private bool _didDrag;
    private bool _dragIsLeftButton;

    // ── Resize state ────────────────────────────────────────
    private bool _isResizing;
    private System.Windows.Point _resizeStart;
    private int _resizeOriginalThickness;
    private bool _isExpandedUpward;
    private double _preHoverExpandTop;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    public string WidgetId => _widgetId;
    public string ActiveFolderPath => _folderPath;
    public bool IsWidgetMinimized => _isMinimized;
    public bool IsMinimizeStateInitialized { get; private set; }
    public double GetPersistedHeight() => _isMinimized && _expandedHeight > 0 ? _expandedHeight : Height;
    public void PersistState() => PersistFolderState();
    public void RestoreDock(DockEdge edge, double thickness)
    {
        if (edge == DockEdge.None)
            return;

        DockTo(edge);
        if (thickness > 0)
            MainWindow.DockManager.Resize(WidgetId, MainWindow.DockManager.DipToPixel(thickness));
    }

    public DesktopFolderWidget() : this("Folder", new WidgetSettings { Topmost = false }, new AppSettings()) { }

    public DesktopFolderWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;
        InitializeComponent();
        _minimizeTimer.Tick += MinimizeTimer_Tick;

        ApplyWidgetSettingsFromModel();
        _isInitializing = false;

        Loaded += (_, _) => ApplySavedMinimizeState();

        Closing += (_, _) => PersistFolderState();
        Closed += (_, _) => ReleaseResources();
    }

    private WidgetSettings SyncWidgetSettings()
    {
        _appSettings.Widgets[_widgetId] = _widgetSettings;
        return _widgetSettings;
    }

    public void ApplyWidgetSettingsFromModel()
    {
        var ws = SyncWidgetSettings();
        var previousMinimized = _isMinimized;
        ThemeHelper.ApplyToElement(this, ws.CustomColors);
        Topmost = ws.Topmost;
        _defaultTopmost = ws.Topmost;
        Opacity = ws.Opacity;

        var activeFolder = ws.ActiveFolder;
        if (string.IsNullOrEmpty(activeFolder))
        {
            if (!ws.Custom.TryGetValue(LegacyActiveFolderKey, out activeFolder) || string.IsNullOrEmpty(activeFolder))
                ws.Custom.TryGetValue(LegacyFolderKey, out activeFolder);
        }

        _folderPath = !string.IsNullOrWhiteSpace(activeFolder)
            ? activeFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        TxtPath.Text = _folderPath;
        ws.ActiveFolder = _folderPath;

        if (ws.Custom.TryGetValue(SortKey, out var sort))
        {
            _sortMode = sort switch
            {
                "DateModified" => SortMode.DateModified,
                "Size" => SortMode.Size,
                "Type" => SortMode.Type,
                _ => SortMode.Name
            };
        }
        if (ws.Custom.TryGetValue(SortDescendingKey, out var sortDescendingValue)
            && bool.TryParse(sortDescendingValue, out var sortDescending))
            _sortDescending = sortDescending;
        _isMinimized = ws.IsMinimized;
        _hasLoadedMinimizeMode = true;
        ws.Custom.Remove(MinimizedKey);
        _expandedHeight = ws.ExpandedHeight ?? 0;
        if (ws.Custom.TryGetValue(LegacyExpandedHeightKey, out var expandedValue)
            && double.TryParse(expandedValue, out var expandedHeight))
            _expandedHeight = expandedHeight;
        UpdateMinimizeButtonVisual();

        if (IsLoaded)
        {
            if (_isMinimized && !previousMinimized)
                CollapseToMinimize();
            else if (!_isMinimized && previousMinimized)
                ExpandFromMinimize();
        }

        BtnSortDir.Content = _sortDescending ? "↓" : "↑";
        BtnSortDir.ToolTip = _sortDescending ? "Descending" : "Ascending";
        CmbSort.SelectedIndex = (int)_sortMode;
        if (EnableStateDiagnostics)
            Debug.WriteLine($"[FolderWidget:{_widgetId}] Load settings ActiveFolder='{ws.ActiveFolder}', IsMinimized={ws.IsMinimized}, ExpandedHeight={ws.ExpandedHeight}, Initialized={IsMinimizeStateInitialized}");
        LoadFolder();
    }

    private void ApplySavedMinimizeState()
    {
        if (_isMinimized)
            CollapseToMinimize();

        IsMinimizeStateInitialized = true;
        if (EnableStateDiagnostics)
            Debug.WriteLine($"[FolderWidget:{_widgetId}] ApplySavedMinimizeState IsMinimized={_isMinimized}, Height={Height}");
    }

    private void SetActiveFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        _folderPath = folderPath;
        TxtPath.Text = _folderPath;
        _widgetSettings.ActiveFolder = _folderPath;
    }

    private void PersistFolderState()
    {
        if (_isInitializing)
            return;

        var ws = _widgetSettings;
        if (string.IsNullOrWhiteSpace(_folderPath))
            _folderPath = !string.IsNullOrWhiteSpace(TxtPath.Text)
                ? TxtPath.Text
                : !string.IsNullOrWhiteSpace(ws.ActiveFolder)
                    ? ws.ActiveFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        ws.Custom.Remove(LegacyFolderKey);
        ws.Custom.Remove(LegacyActiveFolderKey);
        ws.ActiveFolder = _folderPath;
        ws.Custom[SortKey] = _sortMode switch
        {
            SortMode.DateModified => "DateModified",
            SortMode.Size => "Size",
            SortMode.Type => "Type",
            _ => "Name"
        };
        ws.Custom[SortDescendingKey] = _sortDescending.ToString();
        ws.Custom.Remove(MinimizedKey);
        ws.IsMinimized = _isMinimized;
        _widgetSettings.IsMinimized = _isMinimized;
        ws.Custom.Remove(LegacyExpandedHeightKey);
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
        ws.ActiveFolder = _folderPath;
        _appSettings.Widgets[_widgetId] = ws;
        if (EnableStateDiagnostics)
            Debug.WriteLine($"[FolderWidget:{_widgetId}] Persist state ActiveFolder='{ws.ActiveFolder}', IsMinimized={ws.IsMinimized}, ExpandedHeight={ws.ExpandedHeight}");
        _appSettings.Save();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var ws = _widgetSettings;
        if (ws.Left.HasValue && ws.Top.HasValue)
        {
            Left = ws.Left.Value;
            Top = ws.Top.Value;
            EnsureOnScreen();
            return;
        }
        var screen = Screen.PrimaryScreen!.WorkingArea;
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

        bool onScreen = Screen.AllScreens
            .Any(s => rect.IntersectsWith(s.WorkingArea));

        if (!onScreen)
        {
            var primary = Screen.PrimaryScreen!.WorkingArea;
            Left = primary.Left;
            Top = primary.Top;
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

    private DockEdge GetAutoDockEdgeFromCursor()
    {
        var screen = Screen.PrimaryScreen!.Bounds;
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

    private void DockTo(DockEdge edge)
    {
        _currentEdge = edge;
        _isDocked = true;

        RootBorder.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        RootBorder.CornerRadius = new CornerRadius(0);
        RootBorder.Margin = new Thickness(0);
        RootBorder.Effect = null;

        double thickness = edge is DockEdge.Left or DockEdge.Right ? 320 : 200;

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

        Width = 320;
        Height = 420;
        if (preservePosition)
        {
            Left = currentLeft;
            Top = currentTop;
        }
        else
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;
            Left = (screen.Width - Width) / 2;
            Top = (screen.Height - Height) / 2;
        }
        HighlightActiveEdge(DockEdge.None);
        ResizeGrip.Visibility = Visibility.Collapsed;
    }

    // ── Resize grip positioning ─────────────────────────────
    private void ShowResizeGrip(DockEdge edge)
    {
        ResizeGrip.Visibility = Visibility.Visible;

        // Place the grip on the inner edge (the side facing the desktop)
        switch (edge)
        {
            case DockEdge.Left:
                ResizeGrip.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                ResizeGrip.VerticalAlignment = VerticalAlignment.Stretch;
                ResizeGrip.Width = 6;
                ResizeGrip.Height = double.NaN;
                ResizeGrip.Cursor = System.Windows.Input.Cursors.SizeWE;
                break;
            case DockEdge.Right:
                ResizeGrip.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                ResizeGrip.VerticalAlignment = VerticalAlignment.Stretch;
                ResizeGrip.Width = 6;
                ResizeGrip.Height = double.NaN;
                ResizeGrip.Cursor = System.Windows.Input.Cursors.SizeWE;
                break;
            case DockEdge.Top:
                ResizeGrip.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                ResizeGrip.VerticalAlignment = VerticalAlignment.Bottom;
                ResizeGrip.Width = double.NaN;
                ResizeGrip.Height = 6;
                ResizeGrip.Cursor = System.Windows.Input.Cursors.SizeNS;
                break;
            case DockEdge.Bottom:
                ResizeGrip.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                ResizeGrip.VerticalAlignment = VerticalAlignment.Top;
                ResizeGrip.Width = double.NaN;
                ResizeGrip.Height = 6;
                ResizeGrip.Cursor = System.Windows.Input.Cursors.SizeNS;
                break;
        }
    }

    // ── Resize drag handlers ────────────────────────────────
    private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isDocked || e.LeftButton != MouseButtonState.Pressed) return;
        _isResizing = true;
        GetCursorPos(out var pt);
        _resizeStart = new System.Windows.Point(pt.x, pt.y);
        _resizeOriginalThickness = MainWindow.DockManager.DipToPixel(
            _currentEdge is DockEdge.Left or DockEdge.Right ? 320 : 200);
        // Use actual current window size as the baseline
        if (_currentEdge is DockEdge.Left or DockEdge.Right)
            _resizeOriginalThickness = (int)ActualWidth;
        else
            _resizeOriginalThickness = (int)ActualHeight;
        // Convert to pixels for DPI
        _resizeOriginalThickness = MainWindow.DockManager.DipToPixel(_resizeOriginalThickness);

        ((Border)sender).CaptureMouse();
    }

    private void ResizeGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isResizing) return;

        GetCursorPos(out var pt);
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

    // ── Edge highlighting
    private void HighlightActiveEdge(DockEdge edge)
    {
    }

    // ── Folder browsing ─────────────────────────────────────
    private void LoadFolder()
    {
        if (!Directory.Exists(_folderPath))
            return;

        var items = new List<FolderItem>();

        try
        {
            foreach (var dir in Directory.GetDirectories(_folderPath))
            {
                var di = new DirectoryInfo(dir);
                items.Add(new FolderItem
                {
                    DisplayName = di.Name,
                    FullPath = dir,
                    IconImage = IconHelper.GetIconForPath(dir),
                    IsDirectory = true,
                    Modified = di.LastWriteTime,
                    Extension = ""
                });
            }

            foreach (var file in Directory.GetFiles(_folderPath))
            {
                var fi = new FileInfo(file);
                items.Add(new FolderItem
                {
                    DisplayName = fi.Name,
                    FullPath = file,
                    IconImage = IconHelper.GetIconForPath(file),
                    IsDirectory = false,
                    Size = fi.Length,
                    Modified = fi.LastWriteTime,
                    Extension = fi.Extension.ToLowerInvariant()
                });
            }
        }
        catch { }

        items = SortItems(items);

        TxtFolderName.Text = Path.GetFileName(_folderPath);
        if (string.IsNullOrEmpty(TxtFolderName.Text))
            TxtFolderName.Text = _folderPath;

        FileGrid.ItemsSource = items;
    }

    private List<FolderItem> SortItems(List<FolderItem> items)
    {
        // Directories always come first, then apply the selected sort
        IEnumerable<FolderItem> dirs = items.Where(i => i.IsDirectory);
        IEnumerable<FolderItem> files = items.Where(i => !i.IsDirectory);

        dirs = _sortMode switch
        {
            SortMode.Name => _sortDescending
                ? dirs.OrderByDescending(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                : dirs.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase),
            SortMode.DateModified => _sortDescending
                ? dirs.OrderByDescending(i => i.Modified)
                : dirs.OrderBy(i => i.Modified),
            _ => dirs.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
        };

        files = _sortMode switch
        {
            SortMode.Name => _sortDescending
                ? files.OrderByDescending(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase),
            SortMode.DateModified => _sortDescending
                ? files.OrderByDescending(i => i.Modified)
                : files.OrderBy(i => i.Modified),
            SortMode.Size => _sortDescending
                ? files.OrderByDescending(i => i.Size)
                : files.OrderBy(i => i.Size),
            SortMode.Type => _sortDescending
                ? files.OrderByDescending(i => i.Extension).ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(i => i.Extension).ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => files.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
        };

        return [.. dirs, .. files];
    }

    private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbSort.SelectedIndex < 0) return;
        _sortMode = (SortMode)CmbSort.SelectedIndex;
        LoadFolder();
        PersistFolderState();
    }

    private void BtnSortDir_Click(object sender, RoutedEventArgs e)
    {
        _sortDescending = !_sortDescending;
        BtnSortDir.Content = _sortDescending ? "↓" : "↑";
        BtnSortDir.ToolTip = _sortDescending ? "Descending" : "Ascending";
        LoadFolder();
        PersistFolderState();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder to display",
            SelectedPath = _folderPath
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SetActiveFolder(dialog.SelectedPath);
            LoadFolder();
            PersistFolderState();
        }
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

        UpdateMinimizeButtonVisual();
        PersistFolderState();
    }

    private void UpdateMinimizeButtonVisual()
    {
        BtnMinimize.Content = _isMinimized ? "▢" : "—";
        BtnMinimize.ToolTip = _isMinimized ? "Restore" : "Minimize";
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
        Height = _expandedHeight > 0 ? _expandedHeight : 420;
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

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Topmost = true;
        if (!_isMinimized)
            return;

        _minimizeTimer.Stop();

        var expandedHeight = _expandedHeight > 0 ? _expandedHeight : 420;
        var screen = Screen.FromPoint(new System.Drawing.Point((int)Left, (int)Top)).WorkingArea;

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

    private void MinimizeTimer_Tick(object? sender, EventArgs e)
    {
        _minimizeTimer.Stop();
        if (_isMinimized && !IsMouseOver)
            CollapseToMinimize();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_folderPath))
            return;

        var parent = Directory.GetParent(_folderPath);
        if (parent?.FullName is null)
            return;

        SetActiveFolder(parent.FullName);
        LoadFolder();
        PersistFolderState();
    }

    private void FileItem_Click(object sender, RoutedEventArgs e)
    {
        if (_didDrag)
        {
            _didDrag = false;
            return;
        }

        if (sender is Button btn && btn.Tag is string path)
        {
            if (Directory.Exists(path))
            {
                SetActiveFolder(path);
                LoadFolder();
                PersistFolderState();
            }
            else if (File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch { }
            }
        }
    }

    private void FileItem_PreviewLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            _dragStartPoint = e.GetPosition(this);
            _dragItemPath = path;
            _didDrag = false;
            _dragIsLeftButton = true;
        }
    }

    private void FileItem_PreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            _dragStartPoint = e.GetPosition(this);
            _dragItemPath = path;
            _didDrag = false;
            _dragIsLeftButton = false;
        }
    }

    private void FileItem_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragItemPath is null)
            return;

        bool buttonHeld = _dragIsLeftButton
            ? e.LeftButton == MouseButtonState.Pressed
            : e.RightButton == MouseButtonState.Pressed;
        if (!buttonHeld)
            return;

        var pos = e.GetPosition(this);
        var diff = pos - _dragStartPoint;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _didDrag = true;
            var path = _dragItemPath;
            _dragItemPath = null;
            var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { path });
            DragDrop.DoDragDrop(this, data, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Link);
        }
    }

    private void FileItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (_didDrag)
        {
            _didDrag = false;
            _dragItemPath = null;
            e.Handled = true;
            return;
        }

        _dragItemPath = null;

        if (sender is Button btn && btn.Tag is string path)
        {
            e.Handled = true;
            ShellContextMenu.Show(path);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.DockManager.Undock(WidgetId);
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
        // Clear icon image references from all items
        if (FileGrid.ItemsSource is IList<FolderItem> items)
        {
            foreach (var item in items)
                item.IconImage = null;
        }
        FileGrid.ItemsSource = null;

        // Release the drop shadow effect (holds unmanaged render resources)
        RootBorder.Effect = null;
        RootBorder.Child = null;
        Content = null;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (_isDocked)
            Undock(true);

        DragMove();
        ResetUpwardExpansion();
        TryAutoDockFromPosition();
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

public class FolderItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public ImageSource? IconImage { get; set; }
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime Modified { get; set; }
    public string Extension { get; set; } = string.Empty;
}

public enum SortMode
{
    Name,
    DateModified,
    Size,
    Type
}
