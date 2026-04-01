using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;

namespace PrismPane_Widgets.Views;

public partial class TitleBarWidget : Window
{
    private const uint WM_CLOSE = 0x0010;
    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _activeWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IntPtr _lastExternalWindow;
    private int _lastExternalProcessId;
    private string _lastExternalTitle = "No active app";

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public string WidgetId => _widgetId;

    public TitleBarWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;

        ApplyWidgetSettingsFromModel();

        _activeWindowTimer.Tick += (_, _) => UpdateActiveApp();
        Loaded += (_, _) => _activeWindowTimer.Start();
        Closed += (_, _) => _activeWindowTimer.Stop();
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

        if (ws.Height is > 0)
            Height = ws.Height.Value;
        else
            Height = 36;

        ws.Height = Height;
        ws.DockEdge = DockEdge.Top;
        ws.DockThickness = Height;
        _appSettings.Save();
    }

    private void UpdateActiveApp()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            TxtActiveApp.Text = _lastExternalTitle;
            return;
        }

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            TxtActiveApp.Text = _lastExternalTitle;
            return;
        }

        var title = GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                title = string.IsNullOrWhiteSpace(process.MainWindowTitle)
                    ? process.ProcessName
                    : process.MainWindowTitle;
            }
            catch
            {
                title = _lastExternalTitle;
            }
        }

        _lastExternalWindow = hwnd;
        _lastExternalProcessId = (int)processId;
        _lastExternalTitle = string.IsNullOrWhiteSpace(title) ? "No active app" : title;
        TxtActiveApp.Text = _lastExternalTitle;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var len = GetWindowTextLengthW(hwnd);
        if (len <= 0)
            return string.Empty;

        var builder = new StringBuilder(len + 1);
        _ = GetWindowTextW(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private async void ForceClose_Click(object sender, RoutedEventArgs e)
    {
        if (_lastExternalWindow == IntPtr.Zero || !IsWindow(_lastExternalWindow) || _lastExternalProcessId == 0)
            return;

        if (_lastExternalProcessId == Environment.ProcessId)
            return;

        try
        {
            _ = PostMessageW(_lastExternalWindow, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(1200);

            using var process = Process.GetProcessById(_lastExternalProcessId);
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
        }
    }

    private async void ShowProperties_Click(object sender, RoutedEventArgs e)
    {
        if (_lastExternalProcessId == 0)
        {
            System.Windows.MessageBox.Show(this, "No active external process selected.", "Properties", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            using var process = Process.GetProcessById(_lastExternalProcessId);
            process.Refresh();

            var cpuUsage = await GetCpuUsagePercentAsync(process.Id);

            string TryGet(Func<string> getter)
            {
                try { return getter(); }
                catch { return "N/A"; }
            }

            var details =
                $"Title: {_lastExternalTitle}\n" +
                $"Process: {process.ProcessName}\n" +
                $"PID: {process.Id}\n" +
                $"CPU: {(cpuUsage.HasValue ? $"{cpuUsage.Value:F1}%" : "N/A")}\n" +
                $"RAM (Working Set): {TryGet(() => FormatBytes(process.WorkingSet64))}\n" +
                $"RAM (Private): {TryGet(() => FormatBytes(process.PrivateMemorySize64))}\n" +
                $"Virtual Memory: {TryGet(() => FormatBytes(process.VirtualMemorySize64))}\n" +
                $"Threads: {TryGet(() => process.Threads.Count.ToString())}\n" +
                $"Handles: {TryGet(() => process.HandleCount.ToString())}\n" +
                $"Priority: {TryGet(() => process.PriorityClass.ToString())}\n" +
                $"Responding: {TryGet(() => process.Responding ? "Yes" : "No")}\n" +
                $"Start Time: {TryGet(() => process.StartTime.ToString("yyyy-MM-dd HH:mm:ss"))}\n" +
                $"Path: {TryGet(() => process.MainModule?.FileName ?? "N/A")}";

            System.Windows.MessageBox.Show(this, details, "Active App Properties", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            System.Windows.MessageBox.Show(this, "Unable to read properties for the active process.", "Properties", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task<double?> GetCpuUsagePercentAsync(int processId, int sampleMilliseconds = 500)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return null;

            process.Refresh();
            var startCpu = process.TotalProcessorTime;
            var startTime = DateTime.UtcNow;

            await Task.Delay(sampleMilliseconds);

            process.Refresh();
            if (process.HasExited)
                return null;

            var endCpu = process.TotalProcessorTime;
            var endTime = DateTime.UtcNow;

            var cpuUsedMs = (endCpu - startCpu).TotalMilliseconds;
            var elapsedMs = (endTime - startTime).TotalMilliseconds * Environment.ProcessorCount;
            if (elapsedMs <= 0)
                return null;

            return Math.Clamp(cpuUsedMs / elapsedMs * 100.0, 0, 100);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private void BtnAppMenu_Click(object sender, RoutedEventArgs e)
    {
        AppMenu.PlacementTarget = BtnAppMenu;
        AppMenu.IsOpen = true;
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
