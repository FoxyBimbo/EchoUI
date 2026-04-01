using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PrismPane_Widgets.Models;

namespace PrismPane_Widgets.Services;

/// <summary>
/// Manages docked widgets on screen edges using the Windows AppBar API
/// (SHAppBarMessage) so that maximized applications respect the reserved space.
/// </summary>
public partial class WidgetDockManager
{
    // ── P/Invoke ────────────────────────────────────────────

    [LibraryImport("shell32.dll")]
    private static partial uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true)]
    private static partial uint RegisterWindowMessageW([MarshalAs(UnmanagedType.LPWStr)] string lpString);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight,
                                           [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    // ── Constants ───────────────────────────────────────────

    private const uint ABM_NEW        = 0x00000000;
    private const uint ABM_REMOVE     = 0x00000001;
    private const uint ABM_QUERYPOS   = 0x00000002;
    private const uint ABM_SETPOS     = 0x00000003;

    private const uint ABE_LEFT       = 0;
    private const uint ABE_TOP        = 1;
    private const uint ABE_RIGHT      = 2;
    private const uint ABE_BOTTOM     = 3;

    private const int ABN_POSCHANGED       = 1;
    private const int ABN_FULLSCREENAPP    = 2;

    private const int SM_CXSCREEN     = 0;
    private const int SM_CYSCREEN     = 1;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // ── Native structs ──────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    // ── Per-widget registration state ───────────────────────

    private sealed class AppBarState
    {
        public required IntPtr HWnd;
        public required uint CallbackMessage;
        public required DockEdge Edge;
        public required int ThicknessPx;
        public required HwndSource Source;
        public required HwndSourceHook Hook;
        public required Window Window;
        public bool HiddenForFullscreen;
    }

    private readonly Dictionary<string, AppBarState> _bars = [];
    private bool _allHiddenForFullscreen;

    // ── Public API ──────────────────────────────────────────

    public int DipToPixel(double dip)
    {
        double dpi = GetDpiForSystem();
        return (int)Math.Round(dip * dpi / 96.0);
    }

    /// <summary>
    /// Dock a widget to a screen edge. Registers it as a Windows AppBar
    /// so the system reserves the space automatically.
    /// </summary>
    public void Dock(string widgetId, Window window, DockEdge edge, double thicknessDip)
    {
        Undock(widgetId);

        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        int thicknessPx = DipToPixel(thicknessDip);

        // Each AppBar needs a unique callback message id
        uint callbackMsg = RegisterWindowMessageW($"PrismPane_Widgets_AppBar_{widgetId}_{hwnd}");

        // 1) Register with the system
        var abd = NewABD(hwnd, callbackMsg);
        uint result = SHAppBarMessage(ABM_NEW, ref abd);
        if (result == 0)
        {
            // Registration failed — try once more after a short delay
            Thread.Sleep(50);
            result = SHAppBarMessage(ABM_NEW, ref abd);
        }

        // 2) Query & set the position (this reserves the screen space)
        SetAppBarPosition(hwnd, edge, thicknessPx);

        // 3) Hook the WndProc so we respond to ABN_POSCHANGED
        var source = HwndSource.FromHwnd(hwnd);
        // Capture values for the closure
        var capturedEdge = edge;
        var capturedThickness = thicknessPx;
        var capturedMsg = callbackMsg;
        var capturedHwnd = hwnd;

        HwndSourceHook hook = (IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if ((uint)msg == capturedMsg)
            {
                int notification = wParam.ToInt32();
                if (notification == ABN_POSCHANGED)
                {
                    SetAppBarPosition(capturedHwnd, capturedEdge, capturedThickness);
                    handled = true;
                }
                else if (notification == ABN_FULLSCREENAPP)
                {
                    bool entering = lParam != IntPtr.Zero;
                    OnFullscreenChanged(entering);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        };
        source?.AddHook(hook);

        _bars[widgetId] = new AppBarState
        {
            HWnd = hwnd,
            CallbackMessage = callbackMsg,
            Edge = edge,
            ThicknessPx = thicknessPx,
            Source = source!,
            Hook = hook,
            Window = window
        };
    }

    /// <summary>
    /// Remove a widget from its docked AppBar position.
    /// </summary>
    public void Undock(string widgetId)
    {
        if (!_bars.Remove(widgetId, out var state)) return;

        // Remove WndProc hook
        try { state.Source.RemoveHook(state.Hook); } catch { }

        // Unregister the AppBar
        var abd = NewABD(state.HWnd, state.CallbackMessage);
        SHAppBarMessage(ABM_REMOVE, ref abd);
    }

    public DockEdge GetEdge(string widgetId) =>
        _bars.TryGetValue(widgetId, out var state) ? state.Edge : DockEdge.None;

    /// <summary>
    /// Resize a docked widget's reserved thickness and re-register with the AppBar.
    /// </summary>
    public void Resize(string widgetId, int newThicknessPx)
    {
        if (!_bars.TryGetValue(widgetId, out var state)) return;
        if (newThicknessPx < 100) newThicknessPx = 100;

        state.ThicknessPx = newThicknessPx;

        // Update the captured thickness in the WndProc hook by
        // removing the old hook and adding a new one.
        try { state.Source.RemoveHook(state.Hook); } catch { }

        var capturedEdge = state.Edge;
        var capturedThickness = newThicknessPx;
        var capturedMsg = state.CallbackMessage;
        var capturedHwnd = state.HWnd;

        HwndSourceHook hook = (IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if ((uint)msg == capturedMsg)
            {
                int notification = wParam.ToInt32();
                if (notification == ABN_POSCHANGED)
                {
                    SetAppBarPosition(capturedHwnd, capturedEdge, capturedThickness);
                    handled = true;
                }
                else if (notification == ABN_FULLSCREENAPP)
                {
                    bool entering = lParam != IntPtr.Zero;
                    OnFullscreenChanged(entering);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        };
        state.Source.AddHook(hook);
        state.Hook = hook;

        SetAppBarPosition(state.HWnd, state.Edge, newThicknessPx);
    }

    /// <summary>
    /// Unregister all AppBars (call on app exit).
    /// </summary>
    public void RestoreAll()
    {
        foreach (var id in _bars.Keys.ToList())
            Undock(id);
    }

    // ── Fullscreen hide/show ───────────────────────────────

    /// <summary>
    /// List of floating (non-docked) widget windows that should also
    /// be hidden/shown when a fullscreen app is detected.
    /// </summary>
    private readonly List<WeakReference<Window>> _floatingWidgets = [];

    public void TrackFloatingWidget(Window window)
    {
        _floatingWidgets.Add(new WeakReference<Window>(window));
    }

    public void UntrackFloatingWidget(Window window)
    {
        _floatingWidgets.RemoveAll(wr => !wr.TryGetTarget(out var w) || w == window);
    }

    private void OnFullscreenChanged(bool entering)
    {
        _allHiddenForFullscreen = entering;

        foreach (var state in _bars.Values)
        {
            state.HiddenForFullscreen = entering;
            ShowWindow(state.HWnd, entering ? SW_HIDE : SW_SHOW);
        }

        // Also hide/show floating widgets
        _floatingWidgets.RemoveAll(wr => !wr.TryGetTarget(out _));
        foreach (var wr in _floatingWidgets)
        {
            if (wr.TryGetTarget(out var w))
            {
                w.Dispatcher.Invoke(() =>
                {
                    if (entering) w.Hide();
                    else w.Show();
                });
            }
        }
    }

    /// <summary>
    /// Lets external code (e.g. FullscreenWatcher) trigger hide/show.
    /// </summary>
    public void SetFullscreenMode(bool entering) => OnFullscreenChanged(entering);

    // ── AppBar positioning ──────────────────────────────────

    private void SetAppBarPosition(IntPtr hwnd, DockEdge edge, int thicknessPx)
    {
        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);

        uint uEdge = edge switch
        {
            DockEdge.Left   => ABE_LEFT,
            DockEdge.Right  => ABE_RIGHT,
            DockEdge.Top    => ABE_TOP,
            DockEdge.Bottom => ABE_BOTTOM,
            _ => ABE_RIGHT
        };

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uEdge = uEdge,
            rc = new RECT { left = 0, top = 0, right = screenW, bottom = screenH }
        };

        // Set the proposed rectangle based on edge
        switch (edge)
        {
            case DockEdge.Left:
                abd.rc.right = thicknessPx;
                break;
            case DockEdge.Right:
                abd.rc.left = screenW - thicknessPx;
                break;
            case DockEdge.Top:
                abd.rc.bottom = thicknessPx;
                break;
            case DockEdge.Bottom:
                abd.rc.top = screenH - thicknessPx;
                break;
        }

        // Ask the system to adjust for other AppBars (like the taskbar)
        SHAppBarMessage(ABM_QUERYPOS, ref abd);

        // Re-apply our desired thickness after the system adjusted the rect
        switch (edge)
        {
            case DockEdge.Left:
                abd.rc.right = abd.rc.left + thicknessPx;
                break;
            case DockEdge.Right:
                abd.rc.left = abd.rc.right - thicknessPx;
                break;
            case DockEdge.Top:
                abd.rc.bottom = abd.rc.top + thicknessPx;
                break;
            case DockEdge.Bottom:
                abd.rc.top = abd.rc.bottom - thicknessPx;
                break;
        }

        // Commit — the system now reserves this screen region
        SHAppBarMessage(ABM_SETPOS, ref abd);

        // Move our window to exactly match the reserved area
        MoveWindow(hwnd,
            abd.rc.left, abd.rc.top,
            abd.rc.right - abd.rc.left,
            abd.rc.bottom - abd.rc.top,
            true);
    }

    // ── Helper ──────────────────────────────────────────────

    private static APPBARDATA NewABD(IntPtr hwnd, uint callbackMsg) => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(),
        hWnd = hwnd,
        uCallbackMessage = callbackMsg
    };
}
