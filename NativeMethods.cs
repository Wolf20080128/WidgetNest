using System.Runtime.InteropServices;

internal static class NativeMethods {
    public const int WmHotkey = 0x0312;

    /// <summary>系统设置变更（深浅色主题切换也会发这条）。</summary>
    public const int WmSettingChange = 0x001A;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>按住不放时不重复触发。</summary>
    public const uint ModNoRepeat = 0x4000;

    public const uint VkA = 0x41;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// 把「可以设置前台窗口」的权限让给指定进程（单实例里第二个实例唤出老实例的用）。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int processId);
}
