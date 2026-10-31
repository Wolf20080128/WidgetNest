using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WidgetNest;

/// <summary>
/// 主题（跟随系统 / 浅色 / 深色）。做法是把一整套语义色写进 Application.Resources，
/// XAML 里统一用 {DynamicResource ...} 取色；换主题时整批替换画刷，界面自动重绘。
/// 代码里现画的控件（主页网格、小组件）拿不到 XAML 资源，靠 <see cref="ThemeChanged"/>
/// 重建，配色统一走 <see cref="Ink"/> / <see cref="Brush(string)"/>。
/// </summary>
public static class ThemeManager {
    // ===== XAML 里引用的资源键 =====
    public const string PanelBackground = "PanelBackground";
    public const string PanelBorder = "PanelBorder";
    public const string Divider = "Divider";
    public const string TextPrimary = "TextPrimary";
    public const string TextSecondary = "TextSecondary";
    public const string TextTertiary = "TextTertiary";
    public const string TextFaint = "TextFaint";
    public const string TextOnAccent = "TextOnAccent";
    public const string SurfaceSubtle = "SurfaceSubtle";
    public const string SurfaceInset = "SurfaceInset";
    public const string SurfaceActive = "SurfaceActive";
    public const string BorderSubtle = "BorderSubtle";
    public const string BorderStrong = "BorderStrong";
    public const string Accent = "Accent";
    public const string AccentHover = "AccentHover";
    public const string AccentPressed = "AccentPressed";
    public const string AccentSoft = "AccentSoft";
    public const string AccentText = "AccentText";
    public const string Danger = "Danger";
    public const string DangerSoft = "DangerSoft";
    public const string PopupBackground = "PopupBackground";
    public const string MenuBackground = "MenuBackground";
    public const string Scrim = "Scrim";
    public const string WidgetCard = "WidgetCard";

    /// <summary>主题实际生效后触发（浅色/深色真正变了才发）。</summary>
    public static event EventHandler? ThemeChanged;

    /// <summary>当前是否浅色（跟随系统时取系统结果）。</summary>
    public static bool IsLight { get; private set; }

    private static bool _applied;

    /// <summary>按存档里的偏好套用主题。</summary>
    public static void Apply() {
        var mode = PrefStore.Mode;
        bool light = mode switch {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => SystemUsesLightTheme()
        };

        // 同一个亮度重复套用（例如系统设置里改的是别的东西）不做无谓的重建
        if (_applied && light == IsLight)
            return;

        var palette = light ? BuildLight() : BuildDark();
        var resources = Application.Current?.Resources;

        if (resources is not null) {
            foreach (var (key, color) in palette)
                resources[key] = Frozen(color);
        }

        IsLight = light;
        _applied = true;

        W.Repaint();

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>用户点了某个主题。</summary>
    public static void SetMode(AppTheme mode) {
        PrefStore.Mode = mode;
        Apply();
    }

    /// <summary>系统主题可能变了（跟随系统时才有动作）。</summary>
    public static void OnSystemThemeChanged() {
        if (PrefStore.Mode == AppTheme.System)
            Apply();
    }

    /// <summary>取语义色画刷；资源还没准备好时退回白色，不会炸。</summary>
    public static Brush Brush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.White;

    /// <summary>取语义色的颜色值（给需要 Color 的地方用，比如画刷改色）。</summary>
    public static Color ThemeColor(string key) =>
        (Application.Current?.TryFindResource(key) as SolidColorBrush)?.Color ?? Colors.White;

    /// <summary>
    /// 「白色叠加」在不同主题下的等价色：深色主题是半透明白，浅色主题是半透明黑。
    /// 代码里现画的边框、悬停底色都用它换算。
    /// </summary>
    public static Color Ink(byte alpha) => IsLight
        ? Color.FromArgb(alpha, 0x1B, 0x20, 0x26)
        : Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);

    public static SolidColorBrush InkBrush(byte alpha) => Frozen(Ink(alpha));

    public static SolidColorBrush Frozen(Color color) {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ===== 系统主题 =====

    private static bool SystemUsesLightTheme() {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            object? value = key?.GetValue("AppsUseLightTheme");

            return value is not null && Convert.ToInt32(value) != 0;
        }
        catch {
            return false;
        }
    }

    // ===== 调色板 =====

    private static Dictionary<string, Color> BuildDark() => new() {
        [PanelBackground] = Hex("F21F1F1F"),
        [PanelBorder] = Hex("33FFFFFF"),
        [Divider] = Hex("22FFFFFF"),
        [TextPrimary] = Hex("F0FFFFFF"),
        [TextSecondary] = Hex("99FFFFFF"),
        [TextTertiary] = Hex("77FFFFFF"),
        [TextFaint] = Hex("55FFFFFF"),
        [TextOnAccent] = Hex("FFFFFFFF"),
        [SurfaceSubtle] = Hex("1AFFFFFF"),
        [SurfaceInset] = Hex("26FFFFFF"),
        [SurfaceActive] = Hex("33FFFFFF"),
        [BorderSubtle] = Hex("33FFFFFF"),
        [BorderStrong] = Hex("44FFFFFF"),
        [Accent] = Hex("FF4C8DFF"),
        [AccentHover] = Hex("FF5C9AFF"),
        [AccentPressed] = Hex("FF3D7BE8"),
        [AccentSoft] = Hex("334C8DFF"),
        [AccentText] = Hex("CC9DB8FF"),
        [Danger] = Hex("FFFF8070"),
        [DangerSoft] = Hex("33FF6B6B"),
        [PopupBackground] = Hex("FF2C2C2C"),
        [MenuBackground] = Hex("F22A2A2A"),
        [Scrim] = Hex("B8000000"),
        [WidgetCard] = Hex("0FFFFFFF")
    };

    private static Dictionary<string, Color> BuildLight() => new() {
        [PanelBackground] = Hex("F2FAFAFB"),
        [PanelBorder] = Hex("1F000000"),
        [Divider] = Hex("14000000"),
        [TextPrimary] = Hex("E61B2026"),
        [TextSecondary] = Hex("9920262C"),
        [TextTertiary] = Hex("7720262C"),
        [TextFaint] = Hex("5520262C"),
        [TextOnAccent] = Hex("FFFFFFFF"),
        [SurfaceSubtle] = Hex("0F000000"),
        [SurfaceInset] = Hex("14000000"),
        [SurfaceActive] = Hex("1F000000"),
        [BorderSubtle] = Hex("1F000000"),
        [BorderStrong] = Hex("33000000"),
        [Accent] = Hex("FF2F6BFF"),
        [AccentHover] = Hex("FF4B84FF"),
        [AccentPressed] = Hex("FF1F5AE0"),
        [AccentSoft] = Hex("1F2F6BFF"),
        [AccentText] = Hex("FF2F6BFF"),
        [Danger] = Hex("FFD9453A"),
        [DangerSoft] = Hex("1FD9453A"),
        [PopupBackground] = Hex("FFFFFFFF"),
        [MenuBackground] = Hex("F7FFFFFF"),
        [Scrim] = Hex("66000000"),
        [WidgetCard] = Hex("FFFFFFFF")
    };

    /// <summary>把 "AARRGGBB" 或 "RRGGBB" 转成颜色。</summary>
    private static Color Hex(string text) {
        uint value = Convert.ToUInt32(text, 16);

        return text.Length switch {
            8 => Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value),
            _ => Color.FromArgb(0xFF, (byte)(value >> 16), (byte)(value >> 8), (byte)value)
        };
    }
}
