using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PrismPane_Widgets.Models;

namespace PrismPane_Widgets.Services;

public static partial class ShellInterop
{
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr FindWindowW([MarshalAs(UnmanagedType.LPWStr)] string? lpClassName,
                                              [MarshalAs(UnmanagedType.LPWStr)] string? lpWindowName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr FindWindowExW(IntPtr hwndParent, IntPtr hwndChildAfter,
                                                [MarshalAs(UnmanagedType.LPWStr)] string? lpszClass,
                                                [MarshalAs(UnmanagedType.LPWStr)] string? lpszWindow);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);


    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SendMessageTimeoutW(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight,
                                           [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    [DllImport("shell32.dll")]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetParent(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_SHOWNOACTIVATE = 8;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint SPI_GETWORKAREA = 0x0030;
    private const uint SPI_SETWORKAREA = 0x002F;
    private const uint SPIF_SENDCHANGE = 0x0002;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint SMTO_NORMAL = 0x0000;
    private const uint PROGMAN_SPAWN_WORKER = 0x052C;
    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int GWL_STYLE = -16;
    private const long WS_CHILD = 0x40000000L;
    private const long WS_POPUP = unchecked((long)0x80000000L);
    private const uint ABE_LEFT = 0;
    private const uint ABE_TOP = 1;
    private const uint ABE_RIGHT = 2;
    private const uint ABE_BOTTOM = 3;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
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

    public readonly record struct TaskbarBounds(int X, int Y, int Width, int Height, DockEdge Edge);

    private static RECT _originalWorkArea;
    private static bool _workAreaModified;

    // ── Windows Taskbar Hide / Show ─────────────────────────
    public static void HideWindowsTaskbar()
    {
        var hwnd = FindWindowW("Shell_TrayWnd", null);
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, SW_HIDE);

        var hwnd2 = FindWindowW("Shell_SecondaryTrayWnd", null);
        if (hwnd2 != IntPtr.Zero)
            ShowWindow(hwnd2, SW_HIDE);
    }

    public static void ShowWindowsTaskbar()
    {
        var hwnd = FindWindowW("Shell_TrayWnd", null);
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, SW_SHOW);

        var hwnd2 = FindWindowW("Shell_SecondaryTrayWnd", null);
        if (hwnd2 != IntPtr.Zero)
            ShowWindow(hwnd2, SW_SHOW);
    }

    // ── Work Area Reservation ───────────────────────────────
    // Shrinks the desktop work area so that maximized windows
    // leave room for the PrismPane Widgets bar at the bottom. Uses real
    // screen pixels (not WPF DIPs) to avoid DPI-scaling issues.

    public static void ReserveWorkArea(int barHeightPx)
    {
        // Save the current work area so we can restore it later
        var current = new RECT();
        SystemParametersInfoW(SPI_GETWORKAREA, 0, ref current, 0);
        _originalWorkArea = current;

        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);

        var reserved = new RECT
        {
            left = 0,
            top = 0,
            right = screenW,
            bottom = screenH - barHeightPx
        };

        SystemParametersInfoW(SPI_SETWORKAREA, 0, ref reserved, SPIF_SENDCHANGE);
        _workAreaModified = true;
    }

    public static void RestoreWorkArea()
    {
        if (!_workAreaModified) return;
        SystemParametersInfoW(SPI_SETWORKAREA, 0, ref _originalWorkArea, SPIF_SENDCHANGE);
        _workAreaModified = false;
    }

    // ── Position the PrismPane Widgets window using raw pixels ─────────
    // WPF positions use DIPs; we bypass that by calling
    // MoveWindow directly with pixel coordinates so the bar
    // sits flush at the true bottom of the screen.

    public static void PositionBarAtBottom(Window window, int barHeightPx)
    {
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);

        MoveWindow(hwnd, 0, screenH - barHeightPx, screenW, barHeightPx, true);
    }

    public static void PositionWindow(Window window, int x, int y, int width, int height)
    {
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero)
            return;

        MoveWindow(hwnd, x, y, Math.Max(1, width), Math.Max(1, height), true);
    }

    public static void PositionWindowRelativeToTaskbar(Window window, int x, int y, int width, int height)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        MoveWindow(hwnd, x, y, Math.Max(1, width), Math.Max(1, height), true);
    }

    public static void MoveWindowToBottom(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    // ── DPI helper ──────────────────────────────────────────
    // Returns the pixel height that corresponds to the desired
    // WPF height, accounting for the system DPI scale factor.

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    public static int DipToPixel(double dip)
    {
        double dpi = GetDpiForSystem();
        return (int)Math.Round(dip * dpi / 96.0);
    }

    public static bool TryGetTaskbarBounds(out TaskbarBounds bounds)
    {
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = taskbar
        };

        if (taskbar != IntPtr.Zero && SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) != 0)
        {
            bounds = new TaskbarBounds(
                abd.rc.left,
                abd.rc.top,
                Math.Max(1, abd.rc.right - abd.rc.left),
                Math.Max(1, abd.rc.bottom - abd.rc.top),
                abd.uEdge switch
                {
                    ABE_LEFT => DockEdge.Left,
                    ABE_TOP => DockEdge.Top,
                    ABE_RIGHT => DockEdge.Right,
                    _ => DockEdge.Bottom
                });
            return true;
        }

        var screenW = GetSystemMetrics(SM_CXSCREEN);
        var screenH = GetSystemMetrics(SM_CYSCREEN);
        var workArea = new RECT();
        SystemParametersInfoW(SPI_GETWORKAREA, 0, ref workArea, 0);

        if (workArea.bottom < screenH)
        {
            bounds = new TaskbarBounds(0, workArea.bottom, screenW, screenH - workArea.bottom, DockEdge.Bottom);
            return true;
        }

        if (workArea.top > 0)
        {
            bounds = new TaskbarBounds(0, 0, screenW, workArea.top, DockEdge.Top);
            return true;
        }

        if (workArea.right < screenW)
        {
            bounds = new TaskbarBounds(workArea.right, 0, screenW - workArea.right, screenH, DockEdge.Right);
            return true;
        }

        if (workArea.left > 0)
        {
            bounds = new TaskbarBounds(0, 0, workArea.left, screenH, DockEdge.Left);
            return true;
        }

        bounds = default;
        return false;
    }

    public static bool TryAttachToTaskbar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero || taskbar == IntPtr.Zero)
            return false;

        if (GetParent(hwnd) == taskbar)
            return true;

        var style = (long)GetWindowLongPtrW(hwnd, GWL_STYLE);
        style = (style & ~WS_POPUP) | WS_CHILD;
        SetWindowLongPtrW(hwnd, GWL_STYLE, (IntPtr)style);

        _ = SetParent(hwnd, taskbar);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        return true;
    }

    public static void DetachFromTaskbar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || IsTopLevelWindow(hwnd))
            return;

        var style = (long)GetWindowLongPtrW(hwnd, GWL_STYLE);
        style = (style & ~WS_CHILD) | WS_POPUP;
        SetWindowLongPtrW(hwnd, GWL_STYLE, (IntPtr)style);
        _ = SetParent(hwnd, IntPtr.Zero);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    public static void OpenStartMenu()
    {
        try
        {
            // Simulate Win key press via keybd_event
            keybd_event(0x5B, 0, 0, UIntPtr.Zero); // VK_LWIN down
            keybd_event(0x5B, 0, 2, UIntPtr.Zero); // VK_LWIN up
        }
        catch { }
    }

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public static void SendMediaKey(byte virtualKey)
    {
        try
        {
            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
            keybd_event(virtualKey, 0, 2, UIntPtr.Zero);
        }
        catch
        {
        }
    }

    public static void ToggleSystemTray()
    {
        try
        {
            // Find the system tray overflow window and click it
            var tray = FindWindowW("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
            {
                var notify = FindWindowExW(tray, IntPtr.Zero, "TrayNotifyWnd", null);
                if (notify != IntPtr.Zero)
                {
                    var chevron = FindWindowExW(notify, IntPtr.Zero, "Button", null);
                    if (chevron != IntPtr.Zero)
                    {
                        SetForegroundWindow(chevron);
                        SendMessageW(chevron, 0x0201, IntPtr.Zero, IntPtr.Zero); // WM_LBUTTONDOWN
                        SendMessageW(chevron, 0x0202, IntPtr.Zero, IntPtr.Zero); // WM_LBUTTONUP
                        return;
                    }
                }
            }
            // Fallback: open notification area icons settings
            Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
        }
        catch { }
    }

    public static void ToggleNotificationCenter()
    {
        try
        {
            // Win+N opens Windows notification center
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);
            keybd_event(0x4E, 0, 0, UIntPtr.Zero); // 'N'
            keybd_event(0x4E, 0, 2, UIntPtr.Zero);
            keybd_event(0x5B, 0, 2, UIntPtr.Zero);
        }
        catch { }
    }

    public static void LaunchApp(string exePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }
        catch { }
    }

    public static bool TryAttachBehindDesktopIcons(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return false;

        var progman = FindWindowW("Progman", null);
        if (progman == IntPtr.Zero)
            return false;

        // Send the spawn-WorkerW message exactly once.  0x052C is a toggle,
        // so resending it would destroy the layer we just created.
        if (!_workerWSpawned)
        {
            _ = SendMessageTimeoutW(progman, PROGMAN_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);
            _workerWSpawned = true;
        }

        // After 0x052C the desktop hierarchy splits:
        //   WorkerW_A  –  hosts SHELLDLL_DefView (desktop icons)
        //   WorkerW_B  –  spawned empty layer between icons and Progman
        //   Progman    –  wallpaper bitmap
        //
        // Hide WorkerW_B so it doesn't cover our video, then parent the
        // video window directly into Progman.  Because Progman sits below
        // WorkerW_A in z-order the video renders behind desktop icons and
        // behind all regular application windows.
        var workerw = FindDesktopWorkerW();
        if (workerw != IntPtr.Zero)
            ShowWindow(workerw, SW_HIDE);

        // Skip if already correctly parented into Progman.
        if (GetParent(hwnd) == progman)
            return true;

        // Convert from WS_POPUP (top-level) to WS_CHILD before reparenting.
        var style = (long)GetWindowLongPtrW(hwnd, GWL_STYLE);
        style = (style & ~WS_POPUP) | WS_CHILD;
        SetWindowLongPtrW(hwnd, GWL_STYLE, (IntPtr)style);

        _ = SetParent(hwnd, progman);

        // Notify the system about the style change and re-show the window.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        return true;
    }

    private static bool _workerWSpawned;

    /// <summary>
    /// Restores the spawned WorkerW visibility and un-parents the window
    /// so the desktop returns to its normal state.
    /// </summary>
    public static void DetachFromDesktop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Un-child: restore WS_POPUP so the window can be closed normally.
        var style = (long)GetWindowLongPtrW(hwnd, GWL_STYLE);
        style = (style & ~WS_CHILD) | WS_POPUP;
        SetWindowLongPtrW(hwnd, GWL_STYLE, (IntPtr)style);
        _ = SetParent(hwnd, IntPtr.Zero);

        // Re-show the spawned WorkerW so the desktop renders normally again.
        var workerw = FindDesktopWorkerW();
        if (workerw != IntPtr.Zero)
            ShowWindow(workerw, SW_SHOW);
    }

    private static IntPtr FindDesktopWorkerW()
    {
        IntPtr workerw = IntPtr.Zero;
        EnumWindows((topHandle, _) =>
        {
            var shellView = FindWindowExW(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView == IntPtr.Zero)
                return true;

            // Return only the dedicated WorkerW that sits behind the
            // desktop-icons host.  Do NOT return Progman as a fallback;
            // parenting into Progman covers the desktop icons.
            workerw = FindWindowExW(IntPtr.Zero, topHandle, "WorkerW", null);
            return false;
        }, IntPtr.Zero);
        return workerw;
    }

    /// <summary>Returns true when the given HWND has no parent (is a top-level window).</summary>
    public static bool IsTopLevelWindow(IntPtr hwnd)
        => hwnd != IntPtr.Zero && GetParent(hwnd) == IntPtr.Zero;
}
