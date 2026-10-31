using System;
using System.IO;
using System.Reflection;
using System.Security;

using Microsoft.Win32;

namespace WidgetNest;

/// <summary>
/// 开机自启：往 HKCU 的 Run 键写一条启动项，登录后自动拉起本程序（只进托盘，不弹面板）。
///
/// 启动项不做本地副本 —— 注册表就是唯一真相。用户完全可能在
/// 「任务管理器 → 启动应用」里把它关掉，那种情况下还按 preferences.json 显示「已开启」就是骗人。
/// </summary>
internal static class StartupRegistration {
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>任务管理器里那个开关记在这儿：首字节最低位为 1 表示被禁用。</summary>
    private const string ApprovedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ValueName = "WidgetNest";

    /// <summary>
    /// 写进启动项的命令行。路径带引号，装在「Program Files」这类带空格的目录也能启动。
    /// </summary>
    public static string? Command {
        get {
            string? exe = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(exe))
                return null;

            // 开发时用 dotnet run 起来的话进程是 dotnet.exe，得把 dll 一起带上才能启动
            if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet",
                              StringComparison.OrdinalIgnoreCase)) {
                string? assembly = Assembly.GetEntryAssembly()?.Location;

                return string.IsNullOrWhiteSpace(assembly)
                    ? null
                    : $"\"{exe}\" \"{assembly}\"";
            }

            return $"\"{exe}\"";
        }
    }

    /// <summary>
    /// 启动项是否存在、是否指向当前这个程序、是否没被任务管理器禁用。
    /// 换过安装位置后旧路径其实已经不会启动了，所以按「没开」处理。
    /// </summary>
    public static bool IsEnabled {
        get {
            try {
                string? registered = ReadRegisteredCommand();
                string? command = Command;

                if (string.IsNullOrWhiteSpace(registered) || command is null)
                    return false;

                return string.Equals(registered.Trim(), command, StringComparison.OrdinalIgnoreCase)
                    && !IsBlockedInTaskManager;
            }
            catch {
                // 注册表读不了（组策略、权限）时保守点：当没开
                return false;
            }
        }
    }

    /// <summary>
    /// 打开开机自启。失败时 <paramref name="error"/> 是可以直接显示给用户的原因。
    /// </summary>
    public static bool TryEnable(out string error) {
        error = "";

        string? command = Command;

        if (command is null) {
            error = "拿不到程序路径，开机自启暂时设置不了。";
            return false;
        }

        try {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key is null) {
                error = "打不开注册表的启动项，可能被安全软件拦住了。";
                return false;
            }

            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) {
            error = "系统拒绝了写入启动项，可能被安全软件拦住。";
            return false;
        }

        // 以前在任务管理器里禁用过的话，那条记录会让新写的启动项照样不生效
        ClearTaskManagerBlock();

        return true;
    }

    /// <summary>
    /// 关闭开机自启。删不掉也不报错，调用方用 <see cref="IsEnabled"/> 回读真实状态即可。
    /// </summary>
    public static void Disable() {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch {
            // 删不掉说明权限或安全软件在拦，回读状态时会显示成「还开着」
        }

        ClearTaskManagerBlock();
    }

    /// <summary>
    /// 程序搬了家（从调试目录换到发布目录之类）时，把启动项里的旧路径改成现在的。
    /// 只在启动项本来就存在时动手，不会把用户主动删掉的重新加回来。
    /// </summary>
    public static void RepairPathIfNeeded() {
        try {
            string? registered = ReadRegisteredCommand();
            string? command = Command;

            if (string.IsNullOrWhiteSpace(registered) || command is null)
                return;

            if (string.Equals(registered.Trim(), command, StringComparison.OrdinalIgnoreCase))
                return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(ValueName, command, RegistryValueKind.String);
        }
        catch {
            // 修不好就算了，偏好设置里的开关会显示回真实状态
        }
    }

    private static string? ReadRegisteredCommand() {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);

        return key?.GetValue(ValueName) as string;
    }

    private static bool IsBlockedInTaskManager {
        get {
            try {
                using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: false);

                if (key?.GetValue(ValueName) is not byte[] state || state.Length == 0)
                    return false;

                // 0x02 = 启用，0x03 = 禁用，只看最低位
                return (state[0] & 1) != 0;
            }
            catch {
                return false;
            }
        }
    }

    /// <summary>删掉任务管理器留下的禁用标记，让启动项恢复生效。</summary>
    private static void ClearTaskManagerBlock() {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch {
            // 没有这条记录本来就是常态；删不掉也不影响别的
        }
    }
}
