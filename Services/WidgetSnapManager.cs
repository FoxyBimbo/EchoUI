using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PrismPane_Widgets.Services;

/// <summary>
/// Detects when a widget is dropped on top of another for tab-grouping.
/// </summary>
public class WidgetSnapManager
{
    // ── P/Invoke ────────────────────────────────────────────

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    // ── Native structs ──────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    // ── Per-window tracking state ───────────────────────────

    private readonly Dictionary<Window, IntPtr> _tracked = [];

    // ── Events ──────────────────────────────────────────────

    /// <summary>
    /// Raised when a drag ends with the cursor over another widget,
    /// requesting tab-grouping.  Parameters: (dragged, target).
    /// </summary>
    public event Action<Window, Window>? GroupRequested;

    // ── Public API ──────────────────────────────────────────

    /// <summary>
    /// Register a widget window so it participates in grouping detection.
    /// Call after the window's <c>SourceInitialized</c> event has fired.
    /// </summary>
    public void Track(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        _tracked[window] = hwnd;
    }

    /// <summary>
    /// Unregister a widget window from grouping detection.
    /// </summary>
    public void Untrack(Window window)
    {
        _tracked.Remove(window);
    }

    /// <summary>
    /// Call immediately after <c>DragMove()</c> returns.
    /// Checks whether the cursor is over another tracked widget and,
    /// if so, raises <see cref="GroupRequested"/>.
    /// Returns <c>true</c> when grouping was triggered.
    /// </summary>
    public bool OnDragCompleted(Window draggedWindow)
    {
        if (!GetCursorPos(out var cursor)) return false;

        foreach (var (other, hwnd) in _tracked)
        {
            if (other == draggedWindow || !other.IsVisible) continue;
            if (!GetWindowRect(hwnd, out var otherRect)) continue;

            if (cursor.X >= otherRect.left && cursor.X <= otherRect.right &&
                cursor.Y >= otherRect.top && cursor.Y <= otherRect.bottom)
            {
                GroupRequested?.Invoke(draggedWindow, other);
                return true;
            }
        }

        return false;
    }
}
