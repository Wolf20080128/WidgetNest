using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WidgetNest;

/// <summary>
/// 从文件（exe / lnk / 文档等）提取系统关联图标。
/// 只依赖 WPF 与 shell32，不引入 System.Drawing。
/// </summary>
internal static class ShellIcon {
    private const uint ShgfiIcon = 0x00000100;
    private const uint ShgfiLargeIcon = 0x00000000;
    private const uint ShgfiUseFileAttributes = 0x00000010;

    private const uint FileAttributeNormal = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// 取文件的系统图标（32×32）。取不到返回 null。
    /// </summary>
    public static BitmapSource? TryExtract(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        uint flags = ShgfiIcon | ShgfiLargeIcon;
        uint attributes = 0;

        // 文件已经不存在时，按扩展名向系统要关联图标，尽量别让已有图标变空
        if (!File.Exists(path) && !Directory.Exists(path)) {
            flags |= ShgfiUseFileAttributes;
            attributes = FileAttributeNormal;
        }

        var info = default(ShFileInfo);
        IntPtr result = SHGetFileInfo(path, attributes, ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(), flags);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try {
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch {
            return null;
        }
        finally {
            // HICON 由系统分配，必须归还，否则会持续泄漏 GDI 句柄
            DestroyIcon(info.hIcon);
        }
    }

    /// <summary>
    /// 以 PNG 保存（保留 alpha 通道）。
    /// </summary>
    public static void SavePng(BitmapSource source, string filePath) {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }
}
