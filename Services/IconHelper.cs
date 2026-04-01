using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrismPane_Widgets.Services;

public static partial class IconHelper
{
    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconW")]
    private static partial IntPtr ExtractIcon(IntPtr hInst, [MarshalAs(UnmanagedType.LPWStr)] string lpszExeFileName, int nIconIndex);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbSizeFileInfo,
        uint uFlags);

    private const uint SHGFI_ICON       = 0x000000100;
    private const uint SHGFI_LARGEICON  = 0x000000000;
    private const uint SHGFI_SMALLICON  = 0x000000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public static ImageSource? GetIconForExe(string exePath)
    {
        if (!File.Exists(exePath))
            return null;

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
            if (hIcon == IntPtr.Zero || hIcon == (IntPtr)1)
                return null;

            var bmpSource = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bmpSource.Freeze();
            return bmpSource;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero && hIcon != (IntPtr)1)
                DestroyIcon(hIcon);
        }
    }

    /// <summary>
    /// Returns the shell icon for any file or folder path.
    /// </summary>
    public static ImageSource? GetIconForPath(string path)
    {
        var shfi = new SHFILEINFO();
        IntPtr result = SHGetFileInfo(
            path, 0, ref shfi,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_LARGEICON);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var bmpSource = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bmpSource.Freeze();
            return bmpSource;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }
}
