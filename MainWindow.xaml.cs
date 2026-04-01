using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;
using PrismPane_Widgets.Views;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using Screen = System.Windows.Forms.Screen;

namespace PrismPane_Widgets;

public partial class MainWindow : Window
{
    private const bool EnableStateDiagnostics = true;
    private readonly AppSettings _settings;
    private readonly ExtensionManager _extManager;
    private readonly ScriptEngine _scriptEngine;
    private readonly ObservableCollection<AppNotification> _notifications = [];
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly DispatcherTimer _settingsSaveTimer;
    private readonly System.Threading.Timer _showDesktopGuardTimer;
    private readonly HashSet<IntPtr> _closingWidgetHandles = [];
    private bool _widgetsRestored;
    private bool _isRestoringWidgetsState;
    private bool _showDesktopOverrideActive;

    /// <summary>All open widget windows keyed by their unique instance ID.</summary>
    private readonly Dictionary<string, Window> _widgets = [];
    private bool _isShuttingDown;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public int flags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetShellWindow();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    private const int SW_RESTORE = 9;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int DWMWA_CLOAKED = 14;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;
    private const int SWP_HIDEWINDOW = 0x0080;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_TOOLWINDOW = 0x00000080;
    private const nint WS_EX_APPWINDOW = 0x00040000;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public static WidgetDockManager DockManager { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _extManager = new ExtensionManager(_settings);
        _scriptEngine = new ScriptEngine();

        var api = new TaskbarScriptApi(AddNotification, (_, _) => { });
        _scriptEngine.ExposeApi("echo", api);

        _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            if (!_isShuttingDown)
                _settings.Save();
        };

        _showDesktopGuardTimer = new System.Threading.Timer(
            _ => Dispatcher.BeginInvoke(RestoreHiddenWidgets),
            null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(300));

        // -- System tray icon --------------------------------
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = new Icon(Application.GetResourceStream(new Uri("pack://application:,,,/AppIcon.ico"))!.Stream),
            Text = "PrismPane Widgets",
            Visible = true
        };

        RefreshTrayMenu();
        _trayIcon.DoubleClick += (_, _) => OpenSettings();

        Application.Current.Dispatcher.ShutdownStarted += (_, _) =>
        {
            if (_isShuttingDown)
                return;

            _isShuttingDown = true;
            PersistOpenWidgets();
            _settings.Save();
        };

        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        SourceInitialized += (_, _) => EnsureWidgetsRestored();
        Dispatcher.BeginInvoke(EnsureWidgetsRestored, DispatcherPriority.Loaded);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        PersistOpenWidgets();
        _settings.Save();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureWidgetsRestored();
    }

    private void EnsureWidgetsRestored()
    {
        if (_widgetsRestored)
            return;

        _widgetsRestored = true;
        _isRestoringWidgetsState = true;
        RestoreOpenWidgets();
        _isRestoringWidgetsState = false;
        Hide();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _showDesktopGuardTimer.Dispose();
        CloseAllWidgets();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        DockManager.RestoreAll();
    }

    // -- Widget management -----------------------------------

    private static string NormalizeWidgetKind(string kind) =>
        kind == "DesktopFolder" ? "Folder" : kind;

    private void SpawnWidget(string kind)
    {
        var normalizedKind = NormalizeWidgetKind(kind);

        if (normalizedKind == "TitleBar" && TryActivateExistingTitleBar())
            return;

        if (normalizedKind == "MediaControl")
        {
            var existingMediaControl = EnsureSingleWidgetEntry("MediaControl");
            if (existingMediaControl is { } existing)
            {
                if (!_widgets.ContainsKey(existing.Id))
                {
                    existing.Settings.IsOpen = true;
                    OpenWidget(existing.Id, existing.Settings);
                    _settings.Save();
                }
                else if (TryActivateExistingMediaControl())
                {
                    return;
                }

                return;
            }
        }

        if (normalizedKind == "VideoBackground")
        {
            var existingVideoBackground = EnsureSingleWidgetEntry("VideoBackground");
            if (existingVideoBackground is { } existing)
            {
                if (!_widgets.ContainsKey(existing.Id))
                {
                    existing.Settings.IsOpen = true;
                    OpenWidget(existing.Id, existing.Settings);
                    _settings.Save();
                }

                OpenVideoBackgroundSettings(existing.Id, existing.Settings);
                return;
            }
        }

        var (id, ws) = _settings.CreateWidgetInstance(normalizedKind);
        if (normalizedKind == "TitleBar")
            EnsureTitleBarDefaults(ws);

        ws.IsOpen = true;
        OpenWidget(id, ws);
        _settings.Save();

        if (normalizedKind == "VideoBackground")
            OpenVideoBackgroundSettings(id, ws);
    }

    private void RestoreOpenWidgets()
    {
        var singleVideoBackground = EnsureSingleWidgetEntry("VideoBackground");
        var singleMediaControl = EnsureSingleWidgetEntry("MediaControl");
        foreach (var (id, ws) in _settings.Widgets.Where(pair => pair.Value.IsOpen).ToList())
        {
            if (NormalizeWidgetKind(ws.Kind) == "VideoBackground" && singleVideoBackground is { } existing && !string.Equals(id, existing.Id, StringComparison.Ordinal))
                continue;

            if (NormalizeWidgetKind(ws.Kind) == "MediaControl" && singleMediaControl is { } existingMedia && !string.Equals(id, existingMedia.Id, StringComparison.Ordinal))
                continue;

            OpenWidget(id, ws);
        }
    }

    private void OpenWidget(string id, WidgetSettings ws)
    {
        var normalizedKind = NormalizeWidgetKind(ws.Kind);

        if (normalizedKind == "TitleBar")
        {
            if (TryActivateExistingTitleBar())
            {
                _settings.RemoveWidgetInstance(id);
                _settings.Save();
                return;
            }

            EnsureTitleBarDefaults(ws);
        }

        if (normalizedKind == "VideoBackground")
        {
            var existingVideoBackground = _widgets
                .Where(pair => pair.Value is VideoBackgroundWidget)
                .Select(pair => pair.Key)
                .FirstOrDefault(existingId => !string.Equals(existingId, id, StringComparison.Ordinal));

            if (existingVideoBackground is not null)
            {
                _settings.RemoveWidgetInstance(id);
                _settings.Save();
                return;
            }
        }

        if (normalizedKind == "MediaControl")
        {
            var existingMediaControl = _widgets
                .Where(pair => pair.Value is MediaControlWidget)
                .Select(pair => pair.Key)
                .FirstOrDefault(existingId => !string.Equals(existingId, id, StringComparison.Ordinal));

            if (existingMediaControl is not null)
            {
                _settings.RemoveWidgetInstance(id);
                _settings.Save();
                return;
            }
        }

        Window? widget = normalizedKind switch
        {
            "ShortcutPanel" => new ShortcutPanelWidget(id, ws, _settings),
            "Folder" => new DesktopFolderWidget(id, ws, _settings),
            "TitleBar" => new TitleBarWidget(id, ws, _settings),
            "Clock" => new ClockWidget(id, ws, _settings),
            "CpuMonitor" => new CpuMonitorWidget(id, ws, _settings),
            "RamMonitor" => new RamMonitorWidget(id, ws, _settings),
            "Weather" => new WeatherWidget(id, ws, _settings),
            "VideoBackground" => new VideoBackgroundWidget(id, ws, _settings),
            "MediaControl" => new MediaControlWidget(id, ws, _settings),
            _ => null
        };

        if (widget is null)
            return;

        RegisterWidget(id, widget);
        ApplyWidgetPlacement(id, ws, widget);
        widget.Show();

        if (normalizedKind == "Folder")
        {
            foreach (var ext in _extManager.GetWidgets())
            {
                var code = _extManager.ReadScript(ext);
                _scriptEngine.Run(code, ext.ScriptType);
            }
        }
    }

    private void UpdateWidgetPlacement(string id, Window win)
    {
        var ws = _settings.GetWidgetSettings(id);
        ws.IsOpen = true;

        var edge = DockManager.GetEdge(id);
        ws.DockEdge = edge;

        var width = win.Width;
        var height = win.Height;
        if (win is DesktopFolderWidget folderWidget)
        {
            height = folderWidget.GetPersistedHeight();
            var activeFolder = folderWidget.ActiveFolderPath;
            if (!string.IsNullOrWhiteSpace(activeFolder))
            {
                ws.ActiveFolder = activeFolder;
                ws.Custom["DefaultFolder"] = activeFolder;
                ws.Custom.Remove("ActiveFolder");
            }
            if (folderWidget.IsMinimizeStateInitialized)
                ws.IsMinimized = folderWidget.IsWidgetMinimized;
            ws.Custom.Remove("IsMinimized");

            if (EnableStateDiagnostics)
                Debug.WriteLine($"[MainWindow:{id}] UpdateWidgetPlacement ActiveFolder='{ws.ActiveFolder}', IsMinimized={ws.IsMinimized}, WidgetMin={folderWidget.IsWidgetMinimized}, Initialized={folderWidget.IsMinimizeStateInitialized}");
        }

        ws.Width = width;
        ws.Height = height;
        ws.Left = win.Left;
        ws.Top = win.Top;

        if (edge != DockEdge.None)
        {
            var thickness = edge is DockEdge.Left or DockEdge.Right
                ? (win.ActualWidth > 0 ? win.ActualWidth : width)
                : (win.ActualHeight > 0 ? win.ActualHeight : height);
            ws.DockThickness = thickness;
        }
        else
        {
            ws.DockThickness = null;
        }

        ScheduleSettingsSave();
    }

    private void ScheduleSettingsSave()
    {
        if (_isShuttingDown || _isRestoringWidgetsState)
            return;

        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void RegisterWidget(string id, Window widget)
    {
        _widgets[id] = widget;
        widget.Closed += OnWidgetClosed;
        widget.LocationChanged += (_, _) => UpdateWidgetPlacement(id, widget);
        widget.SizeChanged += (_, _) => UpdateWidgetPlacement(id, widget);
        widget.Closing += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(widget).Handle;
            if (hwnd != IntPtr.Zero)
                _closingWidgetHandles.Add(hwnd);
        };

        widget.StateChanged += (_, _) =>
        {
            if (!_isShuttingDown && widget.WindowState == WindowState.Minimized)
            {
                widget.WindowState = WindowState.Normal;
                var hwnd = new WindowInteropHelper(widget).Handle;
                if (hwnd != IntPtr.Zero)
                    ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            }
        };

        widget.SourceInitialized += (_, _) =>
        {
            var helper = new WindowInteropHelper(widget);
            var hwnd = helper.Handle;

            // Set WS_EX_TOOLWINDOW so the shell excludes this window from
            // Show Desktop and detach the hidden WPF owner that
            // ShowInTaskbar="False" creates. Without this, Show Desktop
            // minimises the hidden owner which cascades to the widget,
            // bypassing any WndProc hooks on the widget itself.
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            exStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle);
            helper.Owner = IntPtr.Zero;

            var source = (HwndSource)PresentationSource.FromVisual(widget)!;
            source.AddHook(ShowDesktopGuardHook);
        };

        if (widget is not MediaControlWidget)
            DockManager.TrackFloatingWidget(widget);
    }

    private IntPtr ShowDesktopGuardHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_isShuttingDown || _closingWidgetHandles.Contains(hwnd))
            return IntPtr.Zero;

        switch (msg)
        {
            case WM_WINDOWPOSCHANGING:
            {
                var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((wp.flags & SWP_HIDEWINDOW) != 0)
                {
                    wp.flags &= ~SWP_HIDEWINDOW;
                    Marshal.StructureToPtr(wp, lParam, false);
                }
                break;
            }
            case WM_SYSCOMMAND:
            {
                var command = wParam.ToInt32() & 0xFFF0;
                if (command == SC_MINIMIZE)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            }
        }

        return IntPtr.Zero;
    }

    private void RestoreHiddenWidgets()
    {
        if (_isShuttingDown)
            return;

        bool showDesktopActive = IsShowDesktopActive();

        foreach (var (_, widget) in _widgets)
        {
            var hwnd = new WindowInteropHelper(widget).Handle;
            if (hwnd == IntPtr.Zero || _closingWidgetHandles.Contains(hwnd))
                continue;

            bool needsRestore = !IsWindowVisible(hwnd) || IsIconic(hwnd);

            if (!needsRestore
                && DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0)
            {
                needsRestore = true;
            }

            if (needsRestore)
            {
                ShowWindow(hwnd, SW_RESTORE);
                widget.WindowState = WindowState.Normal;
            }

            // Show Desktop pushes windows behind the desktop in z-order
            // without minimizing or hiding them. Force them back on top.
            if (needsRestore || showDesktopActive)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW | SWP_NOACTIVATE);
                if (!widget.Topmost)
                    SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        // Show Desktop ended – restore non-topmost widgets to normal z-order
        if (_showDesktopOverrideActive && !showDesktopActive)
        {
            foreach (var (_, widget) in _widgets)
            {
                if (widget.Topmost)
                    continue;

                var hwnd = new WindowInteropHelper(widget).Handle;
                if (hwnd == IntPtr.Zero || _closingWidgetHandles.Contains(hwnd))
                    continue;

                SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        _showDesktopOverrideActive = showDesktopActive;
    }

    private static bool IsShowDesktopActive()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
            return false;

        if (fg == GetShellWindow())
            return true;

        var progman = FindWindowW("Progman", null);
        if (progman != IntPtr.Zero && fg == progman)
            return true;

        var workerW = FindWindowW("WorkerW", null);
        return workerW != IntPtr.Zero && fg == workerW;
    }

    private static void EnsureTitleBarDefaults(WidgetSettings ws)
    {
        var height = ws.Height is > 0 ? ws.Height.Value : 36;
        ws.Height = height;
        ws.DockEdge = DockEdge.Top;
        ws.DockThickness = height;
        ws.Top = 0;
    }

    private bool TryActivateExistingTitleBar()
    {
        var existing = _widgets.Values.OfType<TitleBarWidget>().FirstOrDefault();
        if (existing is null)
            return false;

        if (existing.WindowState == WindowState.Minimized)
            existing.WindowState = WindowState.Normal;

        existing.Activate();
        return true;
    }

    private void ApplyWidgetPlacement(string id, WidgetSettings ws, Window widget)
    {
        if (ws.Width is > 0)
            widget.Width = ws.Width.Value;
        if (ws.Height is > 0)
            widget.Height = ws.Height.Value;

        if (ws.Left.HasValue && ws.Top.HasValue)
        {
            widget.WindowStartupLocation = WindowStartupLocation.Manual;
            widget.Left = ws.Left.Value;
            widget.Top = ws.Top.Value;
            EnsureWidgetOnScreen(widget);
        }

        widget.SourceInitialized += (_, _) =>
        {
            var edge = ws.DockEdge;
            var thickness = ws.DockThickness ?? (ws.DockEdge is DockEdge.Left or DockEdge.Right
                ? widget.Width
                : widget.Height);

            if (widget is TitleBarWidget)
            {
                edge = DockEdge.Top;
                thickness = widget.Height > 0 ? widget.Height : (ws.Height ?? 36);
                ws.DockEdge = edge;
                ws.DockThickness = thickness;
            }

            if (edge == DockEdge.None)
                return;

            switch (widget)
            {
                case DesktopFolderWidget folderWidget:
                    folderWidget.RestoreDock(edge, thickness);
                    break;
                case ShortcutPanelWidget shortcutWidget:
                    shortcutWidget.RestoreDock(edge, thickness);
                    break;
                default:
                    DockManager.Dock(id, widget, edge, thickness);
                    break;
            }
        };
    }

    private static void EnsureWidgetOnScreen(Window widget)
    {
        if (double.IsNaN(widget.Width) || double.IsNaN(widget.Height))
            return;

        var rect = new Rectangle(
            (int)Math.Round(widget.Left),
            (int)Math.Round(widget.Top),
            (int)Math.Round(widget.Width),
            (int)Math.Round(widget.Height));

        bool intersects = Screen.AllScreens
            .Any(screen => rect.IntersectsWith(screen.WorkingArea));

        if (!intersects)
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;
            widget.Left = screen.Left;
            widget.Top = screen.Top;
        }
    }

    private void OnWidgetClosed(object? sender, EventArgs e)
    {
        if (sender is not Window win) return;

        string? id = sender switch
        {
            DesktopFolderWidget dfw => dfw.WidgetId,
            ShortcutPanelWidget spw => spw.WidgetId,
            TitleBarWidget tbw => tbw.WidgetId,
            ClockWidget cw => cw.WidgetId,
            CpuMonitorWidget cmw => cmw.WidgetId,
            RamMonitorWidget rmw => rmw.WidgetId,
            WeatherWidget ww => ww.WidgetId,
            VideoBackgroundWidget vbw => vbw.WidgetId,
            MediaControlWidget mcw => mcw.WidgetId,
            _ => null
        };
        if (id is null) return;

        var isAppClosing = _isShuttingDown || Application.Current.Dispatcher.HasShutdownStarted;

        if (win is DesktopFolderWidget folderWidget)
            folderWidget.PersistState();

        if (isAppClosing)
        {
            UpdateWidgetPlacement(id, win);
            _settings.GetWidgetSettings(id).IsOpen = false;
        }
        else
        {
            _settings.RemoveWidgetInstance(id);
        }

        DockManager.Undock(id);
        DockManager.UntrackFloatingWidget(win);
        win.Closed -= OnWidgetClosed;
        _widgets.Remove(id);

        _settings.Save();

        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
    }

    private void PersistOpenWidgets()
    {
        foreach (var (id, win) in _widgets)
        {
            if (win is DesktopFolderWidget folderWidget)
            {
                folderWidget.PersistState();
                var ws = _settings.GetWidgetSettings(id);
                ws.IsMinimized = folderWidget.IsWidgetMinimized;
            }
            UpdateWidgetPlacement(id, win);
            _settings.GetWidgetSettings(id).IsOpen = true;
        }
    }

    private void CloseAllWidgets()
    {
        _isShuttingDown = true;
        PersistOpenWidgets();
        foreach (var (id, win) in _widgets.ToList())
        {
            DockManager.Undock(id);
            DockManager.UntrackFloatingWidget(win);
            win.Closed -= OnWidgetClosed;
            win.Close();
        }
        _widgets.Clear();
        _settings.Save();

        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
    }

    private void AddNotification(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            _notifications.Insert(0, new AppNotification
            {
                Title = title,
                Message = message,
                Timestamp = DateTime.Now
            });
        });
    }

    // -- Settings --------------------------------------------
    private void OpenSettings()
    {
        var win = new SettingsWindow(
            _settings,
            _extManager,
            spawnWidget: SpawnWidget,
            getActiveWidgets: GetActiveWidgets,
            closeWidget: CloseWidget,
            openWidgetSettings: OpenWidgetSettings,
            resetWidgetLocation: ResetWidgetLocation,
            settingsApplied: RefreshTrayMenu);
        if (win.ShowDialog() == true)
        {
            ApplyWidgetSettings();
        }
    }

    public void OpenSettingsFromWidget()
    {
        OpenSettings();
    }

    private (string Id, WidgetSettings Settings)? EnsureSingleWidgetEntry(string kind)
    {
        var widgetEntries = _settings.Widgets
            .Where(pair => NormalizeWidgetKind(pair.Value.Kind) == kind)
            .ToList();

        if (widgetEntries.Count == 0)
            return null;

        var keep = widgetEntries[0];
        var removedAny = false;
        foreach (var duplicate in widgetEntries.Skip(1))
        {
            _settings.RemoveWidgetInstance(duplicate.Key);
            removedAny = true;
        }

        if (removedAny)
            _settings.Save();

        return (keep.Key, keep.Value);
    }

    private bool TryActivateExistingMediaControl()
    {
        var existing = _widgets.Values.OfType<MediaControlWidget>().FirstOrDefault();
        if (existing is null)
            return false;

        existing.Activate();
        return true;
    }

    private IReadOnlyList<ActiveWidgetListItem> GetActiveWidgets() =>
        _widgets.Keys
            .Select(id => new ActiveWidgetListItem
            {
                Id = id,
                DisplayName = BuildWidgetDisplayName(id)
            })
            .OrderBy(widget => widget.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private void CloseWidget(string widgetId)
    {
        if (_widgets.TryGetValue(widgetId, out var widget))
            widget.Close();
    }

    private void ResetWidgetLocation(string widgetId)
    {
        if (!_widgets.TryGetValue(widgetId, out var widget))
            return;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        widget.Left = screen.Left;
        widget.Top = screen.Top;
    }

    private void OpenWidgetSettings(string widgetId)
    {
        if (!_widgets.TryGetValue(widgetId, out var widget))
            return;

        if (!_settings.Widgets.TryGetValue(widgetId, out var ws))
            return;

        Action? apply = widget switch
        {
            DesktopFolderWidget folderWidget => folderWidget.ApplyWidgetSettingsFromModel,
            ShortcutPanelWidget shortcutPanelWidget => shortcutPanelWidget.ApplyWidgetSettingsFromModel,
            TitleBarWidget titleBarWidget => titleBarWidget.ApplyWidgetSettingsFromModel,
            ClockWidget clockWidget => clockWidget.ApplyWidgetSettingsFromModel,
            CpuMonitorWidget cpuMonitorWidget => cpuMonitorWidget.ApplyWidgetSettingsFromModel,
            RamMonitorWidget ramMonitorWidget => ramMonitorWidget.ApplyWidgetSettingsFromModel,
            WeatherWidget weatherWidget => weatherWidget.ApplyWidgetSettingsFromModel,
            VideoBackgroundWidget videoBackgroundWidget => videoBackgroundWidget.ApplyWidgetSettingsFromModel,
            MediaControlWidget mediaControlWidget => mediaControlWidget.ApplyWidgetSettingsFromModel,
            _ => null
        };

        var settings = new SettingsWindow(_settings, null, widgetId, ws, apply);
        if (widget.IsLoaded && PresentationSource.FromVisual(widget) is not null)
            settings.Owner = widget;

        settings.ShowDialog();
    }

    private static string BuildWidgetDisplayName(string widgetId)
    {
        var separatorIndex = widgetId.LastIndexOf('_');
        var kind = separatorIndex > 0 ? widgetId[..separatorIndex] : widgetId;

        var displayKind = NormalizeWidgetKind(kind) switch
        {
            "ShortcutPanel" => "Shortcut Panel",
            "TitleBar" => "Title Bar",
            "CpuMonitor" => "CPU Monitor",
            "RamMonitor" => "RAM Monitor",
            "VideoBackground" => "Video Widget",
            "MediaControl" => "Media Control",
            _ => NormalizeWidgetKind(kind)
        };

        return $"{displayKind} ({widgetId})";
    }

    private void RefreshTrayMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        var builtinWidgets = new[]
        {
            (Kind: "Folder", DisplayName: "Folder"),
            (Kind: "ShortcutPanel", DisplayName: "Shortcut Panel"),
            (Kind: "TitleBar", DisplayName: "Title Bar"),
            (Kind: "Clock", DisplayName: "Clock"),
            (Kind: "CpuMonitor", DisplayName: "CPU Monitor"),
            (Kind: "RamMonitor", DisplayName: "RAM Monitor"),
            (Kind: "Weather", DisplayName: "Weather"),
            (Kind: "VideoBackground", DisplayName: "Video"),
            (Kind: "MediaControl", DisplayName: "Media Control")
        };
        var builtinWidgetKinds = new HashSet<string>(builtinWidgets.Select(widget => widget.Kind), StringComparer.OrdinalIgnoreCase);
        foreach (var widget in builtinWidgets)
            menu.Items.Add($"New {widget.DisplayName} Widget", null, (_, _) => SpawnWidget(widget.Kind));

        var extensionWidgets = _extManager.GetWidgets()
            .Where(w => !builtinWidgetKinds.Contains(w.Name))
            .ToList();

        if (builtinWidgets.Length > 0 && extensionWidgets.Count > 0)
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        foreach (var widget in extensionWidgets)
            menu.Items.Add($"New {widget.Name} Widget", null, (_, _) => SpawnWidget(widget.Name));

        if (menu.Items.Count > 0)
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon.ContextMenuStrip = menu;
    }

    private void ApplyWidgetSettings()
    {
        foreach (var (id, win) in _widgets)
        {
            var ws = _settings.GetWidgetSettings(id);

            if (win is VideoBackgroundWidget videoBackgroundWidget)
                videoBackgroundWidget.ApplyWidgetSettingsFromModel();
            else if (win is MediaControlWidget mediaControlWidget)
                mediaControlWidget.ApplyWidgetSettingsFromModel();
            else
            {
                win.Topmost = ws.Topmost;
                win.Opacity = ws.Opacity;
            }

            ThemeHelper.ApplyToElement(win, ws.CustomColors);
        }
    }

    private void OpenVideoBackgroundSettings(string widgetId, WidgetSettings ws)
    {
        Action? apply = null;
        Window? owner = null;

        if (_widgets.TryGetValue(widgetId, out var widget) && widget is VideoBackgroundWidget backgroundWidget)
        {
            apply = backgroundWidget.ApplyWidgetSettingsFromModel;
            if (backgroundWidget.IsLoaded && PresentationSource.FromVisual(backgroundWidget) is not null)
                owner = backgroundWidget;
        }

        var settings = new SettingsWindow(_settings, null, widgetId, ws, apply);
        if (owner is not null)
            settings.Owner = owner;

        settings.ShowDialog();
    }

    // -- Exit ------------------------------------------------
    private void ExitApp()
    {
        if (!_isShuttingDown)
            CloseAllWidgets();
        DockManager.RestoreAll();
        Application.Current.Shutdown();
    }
}