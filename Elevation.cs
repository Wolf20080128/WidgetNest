using System;
using System.Diagnostics;
using System.Security.Principal;

namespace WidgetNest;

/// <summary>
/// 管理员权限检测与自我提权。
/// CPU 温度（ACPI 热区）和硬盘温度（物理磁盘 IOCTL）只有管理员能读，
/// 其余功能在普通权限下都能正常工作，所以只在用户主动点击时才提权。
/// </summary>
internal static class Elevation {
    private static bool? _cached;

    public static bool IsElevated => _cached ??= Detect();

    private static bool Detect() {
        try {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch {
            return false;
        }
    }

    /// <summary>
    /// 以管理员身份重新拉起自己。返回 true 表示新进程已启动（调用方应退出当前实例）；
    /// 用户点了「否」或系统拒绝时返回 false，调用方保持原状即可。
    /// </summary>
    public static bool RestartElevated() {
        string? path = Environment.ProcessPath;

        if (string.IsNullOrEmpty(path))
            return false;

        try {
            Process.Start(new ProcessStartInfo {
                FileName = path,
                // 旧实例还没退完，让新实例知道要等它放掉单实例锁
                Arguments = SingleInstance.RestartArgument,
                UseShellExecute = true,
                Verb = "runas"
            });

            return true;
        }
        catch {
            // UAC 取消 / 策略不允许：安静地什么都不做
            return false;
        }
    }
}
