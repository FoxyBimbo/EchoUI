using System.IO;
using System.Runtime.InteropServices;

namespace PrismPane_Widgets.Services;

/// <summary>
/// Shows the native Windows Explorer right-click context menu for a file or folder
/// using the Shell COM interfaces (IShellFolder / IContextMenu).
/// </summary>
public static partial class ShellContextMenu
{
    // ── COM interfaces ──────────────────────────────────────

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        void EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        void BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, [In] ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref uint rgfInOut);
        void GetUIObjectOf(IntPtr hwndOwner, uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            [In] ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
        void SetNameOf(IntPtr hwnd, IntPtr pidl,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst,
            uint idCmdLast, uint uFlags);
        void InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        void GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved,
            IntPtr pszName, uint cchMax);
    }

    // ── Native structs ──────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpTitleW;
        public POINT ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x, y;
    }

    // ── P/Invoke ────────────────────────────────────────────

    [DllImport("shell32.dll")]
    private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc,
        out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, [In] ref Guid riid,
        out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags,
        int x, int y, IntPtr hwnd, IntPtr lptpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle,
        string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    private const uint TPM_RETURNCMD   = 0x0100;
    private const uint TPM_LEFTALIGN   = 0x0000;
    private const uint CMF_NORMAL      = 0x00000000;
    private const uint CMF_EXPLORE     = 0x00000004;
    private const uint CMIC_MASK_UNICODE       = 0x00004000;
    private const uint CMIC_MASK_PTINVOKE      = 0x20000000;
    private const int  SW_SHOWNORMAL   = 1;

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    // ── Public API ──────────────────────────────────────────

    /// <summary>
    /// Shows the native Windows Explorer context menu for the given path
    /// at the current cursor position.
    /// </summary>
    public static void Show(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;

        IntPtr pidlFull = IntPtr.Zero;
        IntPtr hMenu = IntPtr.Zero;
        IntPtr hwndDummy = IntPtr.Zero;

        try
        {
            // 1. Parse the full path into an absolute PIDL
            int hr = SHParseDisplayName(path, IntPtr.Zero, out pidlFull, 0, out _);
            if (hr != 0 || pidlFull == IntPtr.Zero) return;

            // 2. Bind to parent folder and get the child PIDL
            Guid iidFolder = IID_IShellFolder;
            hr = SHBindToParent(pidlFull, ref iidFolder, out IntPtr ppv, out IntPtr pidlChild);
            if (hr != 0) return;

            var parentFolder = (IShellFolder)Marshal.GetObjectForIUnknown(ppv);
            Marshal.Release(ppv);

            // 3. Get IContextMenu for the item
            var apidl = new[] { pidlChild };
            Guid iidCtxMenu = IID_IContextMenu;
            parentFolder.GetUIObjectOf(IntPtr.Zero, 1, apidl, ref iidCtxMenu, IntPtr.Zero, out IntPtr ctxPtr);
            var contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(ctxPtr);
            Marshal.Release(ctxPtr);

            // 4. Create popup menu and let the shell populate it
            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            contextMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF, CMF_NORMAL | CMF_EXPLORE);

            // 5. Show the menu at the cursor position
            //    Need a message-pump window for TrackPopupMenuEx
            GetCursorPos(out var pt);
            hwndDummy = CreateWindowEx(0, "Static", "", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            int cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_LEFTALIGN,
                pt.x, pt.y, hwndDummy, IntPtr.Zero);

            if (cmd > 0)
            {
                // 6. Invoke the selected command
                var invoke = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                    hwnd = hwndDummy,
                    lpVerb = (IntPtr)(cmd - 1),
                    lpVerbW = IntPtr.Zero,
                    nShow = SW_SHOWNORMAL,
                    ptInvoke = pt,
                    lpDirectoryW = Path.GetDirectoryName(path)
                };
                contextMenu.InvokeCommand(ref invoke);
            }
        }
        catch
        {
            // Silently ignore — shell menu failed to show
        }
        finally
        {
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (hwndDummy != IntPtr.Zero) DestroyWindow(hwndDummy);
            if (pidlFull != IntPtr.Zero) CoTaskMemFree(pidlFull);
        }
    }
}
