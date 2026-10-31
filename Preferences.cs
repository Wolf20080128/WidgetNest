using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WidgetNest;

/// <summary>主题模式：跟随系统 / 浅色 / 深色。</summary>
public enum AppTheme {
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>
/// 偏好设置的落盘结构：%LOCALAPPDATA%\WidgetNest\preferences.json
/// 用属性而不是字段，方便以后加项时旧配置照样能读。
/// </summary>
public sealed class Preferences {
    /// <summary>system / light / dark；不认识的值按 system 处理。</summary>
    public string Theme { get; set; } = "system";

    /// <summary>多页时是否在面板底部显示「2/3」这样的页码。</summary>
    public bool ShowPageIndicator { get; set; } = true;

    /// <summary>主页图标下面是否显示名称。</summary>
    public bool ShowIconLabels { get; set; } = true;

    /// <summary>唤出/隐藏面板的全局热键，例如 Ctrl+Shift+A。</summary>
    public string Hotkey { get; set; } = HotkeySpec.Default.ToStorage();
}

/// <summary>
/// 偏好设置读写。进程内单例，改动后立刻落盘并广播 <see cref="Changed"/>，
/// 界面各处（主页、托盘提示）据此刷新。
/// </summary>
internal static class PrefStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static Preferences? _current;

    /// <summary>任意一项偏好发生变化。</summary>
    public static event EventHandler? Changed;

    public static string FilePath => Path.Combine(AppStorage.RootDir, "preferences.json");

    public static Preferences Current => _current ??= Load();

    /// <summary>当前主题偏好。</summary>
    public static AppTheme Mode {
        get => ParseTheme(Current.Theme);
        set {
            string text = value switch {
                AppTheme.Light => "light",
                AppTheme.Dark => "dark",
                _ => "system"
            };

            if (string.Equals(Current.Theme, text, StringComparison.Ordinal))
                return;

            Current.Theme = text;
            Save();
        }
    }

    public static bool ShowPageIndicator {
        get => Current.ShowPageIndicator;
        set {
            if (Current.ShowPageIndicator == value)
                return;

            Current.ShowPageIndicator = value;
            Save();
        }
    }

    public static bool ShowIconLabels {
        get => Current.ShowIconLabels;
        set {
            if (Current.ShowIconLabels == value)
                return;

            Current.ShowIconLabels = value;
            Save();
        }
    }

    /// <summary>当前热键；存档损坏时退回默认的 Ctrl+Shift+A。</summary>
    public static HotkeySpec Hotkey =>
        HotkeySpec.TryParse(Current.Hotkey, out var spec) ? spec : HotkeySpec.Default;

    public static void SetHotkey(HotkeySpec spec) {
        string text = spec.ToStorage();

        if (string.Equals(Current.Hotkey, text, StringComparison.Ordinal))
            return;

        Current.Hotkey = text;
        Save();
    }

    private static AppTheme ParseTheme(string? text) => text?.Trim().ToLowerInvariant() switch {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => AppTheme.System
    };

    public static void Save() {
        try {
            AppStorage.EnsureCreated();

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions), Utf8NoBom);

            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, null);
            else
                File.Move(temp, FilePath);
        }
        catch {
            // 写盘失败不影响当前会话，改过的设置在本次运行内依然生效
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static Preferences Load() {
        try {
            AppStorage.EnsureCreated();

            if (!File.Exists(FilePath))
                return new Preferences();

            string json = File.ReadAllText(FilePath, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(json))
                return new Preferences();

            var prefs = JsonSerializer.Deserialize<Preferences>(json, JsonOptions);

            if (prefs is null)
                return new Preferences();

            // 字段缺失（旧版本配置文件）时回落到默认值
            prefs.Theme ??= "system";
            prefs.Hotkey = string.IsNullOrWhiteSpace(prefs.Hotkey)
                ? HotkeySpec.Default.ToStorage()
                : prefs.Hotkey;

            return prefs;
        }
        catch {
            return new Preferences();
        }
    }
}
