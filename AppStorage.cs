using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetNest;

/// <summary>
/// 本地持久化。目录结构：
///   %LOCALAPPDATA%\WidgetNest\
///     ├─ apps.json                 布局与当前页
///     └─ FilesDate\&lt;hash&gt;.png     图标本体
/// </summary>
internal static class AppStorage {
    public const string IconFolderName = "FilesDate";
    public const string StateFileName = "apps.json";

    private const string AppFolderName = "WidgetNest";

    /// <summary>改名前的目录名，用于一次性迁移用户已有数据。</summary>
    private const string LegacyFolderName = "RightBottomPopup";

    private static string LocalAppData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string RootDir { get; } = Path.Combine(LocalAppData, AppFolderName);

    public static string IconDir { get; } = Path.Combine(RootDir, IconFolderName);

    public static string StateFile { get; } = Path.Combine(RootDir, StateFileName);

    /// <summary>用户自己开发的组件：一个组件一个子目录，里面放 widget.json。</summary>
    public static string WidgetDir { get; } = Path.Combine(RootDir, "widgets");

    /// <summary>组件开发文档与示例包，供用户直接改了拖回来导入。</summary>
    public static string WidgetSampleDir { get; } = Path.Combine(RootDir, "widget-samples");

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // 不要 BOM：带 BOM 的 JSON 会让不少外部工具/脚本解析失败
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void EnsureCreated() {
        MigrateLegacyFolder();

        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(IconDir);
    }

    /// <summary>
    /// 从旧目录名迁移数据（改名 WidgetNest 之前叫 RightBottomPopup）。
    /// 仅在新目录尚不存在时执行一次；失败不影响本次运行，用户会看到空面板。
    /// </summary>
    private static void MigrateLegacyFolder() {
        try {
            string legacy = Path.Combine(LocalAppData, LegacyFolderName);

            if (Directory.Exists(RootDir) || !Directory.Exists(legacy))
                return;

            Directory.Move(legacy, RootDir);
        }
        catch {
            // 迁移失败就当作没有旧数据
        }
    }

    public static AppState Load() {
        try {
            EnsureCreated();

            if (!File.Exists(StateFile))
                return new AppState();

            string json = File.ReadAllText(StateFile, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return new AppState();

            return JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
        }
        catch {
            // 数据损坏不应导致程序起不来，退回空布局
            return new AppState();
        }
    }

    public static void Save(AppState state) {
        try {
            EnsureCreated();

            string json = JsonSerializer.Serialize(state, JsonOptions);
            string temp = StateFile + ".tmp";

            File.WriteAllText(temp, json, Utf8NoBom);

            // 先写临时文件再替换，避免写到一半崩溃把配置写坏
            if (File.Exists(StateFile))
                File.Replace(temp, StateFile, null);
            else
                File.Move(temp, StateFile);
        }
        catch {
            // 持久化失败不影响本次运行
        }
    }

    /// <summary>
    /// 把图标本体落盘到 FilesDate，返回图标文件名（不含目录）；失败返回 null。
    /// 用源文件全路径的哈希命名，重复添加同一文件不会产生多份图标。
    /// </summary>
    public static string? TryCacheIcon(string sourcePath) {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        try {
            EnsureCreated();

            string fileName = HashName(sourcePath) + ".png";
            string target = Path.Combine(IconDir, fileName);

            if (File.Exists(target) && new FileInfo(target).Length > 0)
                return fileName;

            var icon = ShellIcon.TryExtract(sourcePath);
            if (icon is null)
                return null;

            ShellIcon.SavePng(icon, target);

            return File.Exists(target) ? fileName : null;
        }
        catch {
            return null;
        }
    }

    public static string ResolveIconPath(string iconFile) =>
        string.IsNullOrWhiteSpace(iconFile) ? "" : Path.Combine(IconDir, iconFile);

    /// <summary>
    /// 删除 FilesDate 里的图标本体。调用方需自行确认没有其他条目还在引用它。
    /// </summary>
    public static void DeleteIcon(string iconFile) {
        if (string.IsNullOrWhiteSpace(iconFile))
            return;

        try {
            string path = ResolveIconPath(iconFile);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch {
            // 文件被占用等情况忽略：图标残留不影响功能
        }
    }

    private static string HashName(string text) {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));

        // 取前 16 个十六进制字符，足够区分且文件名短
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}

/// <summary>
/// 落盘的整体状态：图标列表 + 当前页。
/// </summary>
public sealed class AppState {
    public int CurrentPage { get; set; }

    public List<AppItem> Items { get; set; } = new();
}
