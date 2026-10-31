using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WidgetNest;

/// <summary>
/// 偏好设置：主题（跟随系统 / 浅色 / 深色）、全局快捷键、开机自启、是否显示图标名称、是否显示页码。
/// 改动即时生效并落盘到 preferences.json，不需要「保存」按钮。
/// 开机自启不进配置文件，它的真相在注册表里。
/// </summary>
public partial class PreferencesPage : UserControl {
    /// <summary>同步控件状态时不要反过来写配置。</summary>
    private bool _syncing;

    /// <summary>是否正在录制新的快捷键。</summary>
    private bool _capturing;

    /// <summary>各设置行的默认副标题：状态提示借用这行文字，用完要能还原。</summary>
    private readonly Dictionary<TextBlock, string> _hintDefaults = new();

    /// <summary>当前用错误色显示的副标题（换主题时按它重新取画刷）。</summary>
    private readonly HashSet<TextBlock> _errorHints = new();

    public PreferencesPage() {
        InitializeComponent();

        // 副标题兼作状态行，默认文案以 XAML 为准
        RegisterHint(HotkeyHint);
        RegisterHint(StartupHint);

        // 构造时先对齐一次，页面还没挂上窗口时（比如预览）也不会显示成空的
        SyncFromPrefs();

        Loaded += (_, _) => SyncFromPrefs();

        // 页面被切走（回主页、隐藏面板）时结束录制，免得热键一直处于挂起状态
        Unloaded += (_, _) => EndCapture();
        IsVisibleChanged += (_, _) => {
            if (!IsVisible)
                EndCapture();
        };

        // 主题变了（比如跟随系统时系统切了深色），提示文字跟着更新
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) {
        if (!IsLoaded)
            return;

        // 提示行、热键胶囊的配色是代码里指定的画刷，换主题后要重新取一次
        UpdateThemeHint();

        if (!_capturing)
            UpdateHotkeyLabel();

        ApplyHintColors();
    }

    // ===== 读取当前配置 =====

    private void SyncFromPrefs() {
        _syncing = true;

        var mode = PrefStore.Mode;

        ThemeFollowSystem.IsChecked = mode == AppTheme.System;
        ThemeLight.IsChecked = mode == AppTheme.Light;
        ThemeDark.IsChecked = mode == AppTheme.Dark;

        ShowPageSwitch.IsChecked = PrefStore.ShowPageIndicator;
        ShowIconLabelSwitch.IsChecked = PrefStore.ShowIconLabels;

        // 开机自启以注册表为准：用户可能在「任务管理器 → 启动」里直接关掉过
        LaunchAtStartupSwitch.IsChecked = StartupRegistration.IsEnabled;

        _syncing = false;

        UpdateThemeHint();
        UpdateHotkeyLabel();
        ResetHints();
    }

    private void UpdateThemeHint() {
        ThemeHint.Text = PrefStore.Mode switch {
            AppTheme.Light => "固定浅色",
            AppTheme.Dark => "固定深色",
            _ => ThemeManager.IsLight ? "跟随系统 · 当前是浅色" : "跟随系统 · 当前是深色"
        };
    }

    private void UpdateHotkeyLabel() {
        HotkeyText.Text = PrefStore.Hotkey.Display;
        HotkeyPill.BorderBrush = ThemeManager.Brush(ThemeManager.BorderSubtle);
        HotkeyPill.Background = ThemeManager.Brush(ThemeManager.SurfaceInset);
        ChangeHotkeyButton.Content = "更改";
    }

    // ===== 主题 =====

    private void Theme_Checked(object sender, RoutedEventArgs e) {
        if (_syncing || !IsLoaded)
            return;

        var mode = ReferenceEquals(sender, ThemeLight) ? AppTheme.Light
            : ReferenceEquals(sender, ThemeDark) ? AppTheme.Dark
            : AppTheme.System;

        ThemeManager.SetMode(mode);
        UpdateThemeHint();
    }

    // ===== 图标名称 =====

    private void ShowIconLabelSwitch_Click(object sender, RoutedEventArgs e) {
        if (_syncing)
            return;

        // 开关本身就是反馈，这里不再多写一行提示
        PrefStore.ShowIconLabels = ShowIconLabelSwitch.IsChecked == true;
    }

    // ===== 页码 =====

    private void ShowPageSwitch_Click(object sender, RoutedEventArgs e) {
        if (_syncing)
            return;

        PrefStore.ShowPageIndicator = ShowPageSwitch.IsChecked == true;
    }

    // ===== 开机自启 =====

    private void LaunchAtStartupSwitch_Click(object sender, RoutedEventArgs e) {
        if (_syncing)
            return;

        if (LaunchAtStartupSwitch.IsChecked == true) {
            if (!StartupRegistration.TryEnable(out string error)) {
                SetHint(StartupHint, error, isError: true);
                RevertStartupSwitch();
                return;
            }

            ResetHint(StartupHint);
            return;
        }

        StartupRegistration.Disable();

        // 删不掉（安全软件锁了注册表）就把开关拨回去，别让界面替系统撒谎
        if (StartupRegistration.IsEnabled) {
            RevertStartupSwitch();
            SetHint(StartupHint, "启动项删不掉，可能被安全软件锁住了。", isError: true);
            return;
        }

        SetHint(StartupHint, "已关闭，登录时不再自动启动。", isError: false);
    }

    /// <summary>把开关拨回注册表里的真实状态。</summary>
    private void RevertStartupSwitch() {
        _syncing = true;
        LaunchAtStartupSwitch.IsChecked = StartupRegistration.IsEnabled;
        _syncing = false;
    }

    // ===== 快捷键录制 =====

    private void ChangeHotkey_Click(object sender, RoutedEventArgs e) {
        // 录制中这个按钮变成「取消」
        if (_capturing) {
            EndCapture();
            SetHint(HotkeyHint, $"已取消，快捷键保持为 {PrefStore.Hotkey.Display}。", isError: false);
            return;
        }

        BeginCapture();
    }

    private void HotkeyPill_Click(object sender, MouseButtonEventArgs e) {
        e.Handled = true;
        BeginCapture();
    }

    private void BeginCapture() {
        if (_capturing)
            return;

        _capturing = true;

        // 录制期间先摘掉旧热键，否则按下旧组合会直接把面板收起来
        (Application.Current as App)?.Hotkey?.Suspend();

        HotkeyText.Text = "按下组合键…";
        HotkeyPill.BorderBrush = ThemeManager.Brush(ThemeManager.Accent);
        HotkeyPill.Background = ThemeManager.Brush(ThemeManager.AccentSoft);
        ChangeHotkeyButton.Content = "取消";

        SetHint(HotkeyHint, "至少包含 Ctrl / Alt / Shift / Win 之一，Esc 取消。", isError: false);

        // 快捷键靠键盘焦点收，控件本身要拿到焦点
        if (!Focus())
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));

        Keyboard.Focus(this);
    }

    private void Page_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (!_capturing)
            return;

        // Alt 组合会以 SystemKey 的形式送过来
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 只按了修饰键：继续等真正的按键，别急着报错
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin) {
            e.Handled = true;
            return;
        }

        e.Handled = true;

        if (key == Key.Escape) {
            EndCapture();
            SetHint(HotkeyHint, "已取消，快捷键保持为 " + PrefStore.Hotkey.Display + "。", isError: false);
            return;
        }

        var spec = new HotkeySpec(CurrentModifiers(), key);

        if (!spec.IsValid) {
            SetHint(HotkeyHint, "需要至少一个修饰键（Ctrl / Alt / Shift / Win）。", isError: true);
            return;
        }

        var app = Application.Current as App;

        if (app is null) {
            EndCapture();
            SetHint(HotkeyHint, "快捷键服务没起来，暂时改不了。", isError: true);
            return;
        }

        if (!app.TryChangeHotkey(spec, out string error)) {
            SetHint(HotkeyHint, error, isError: true);
            return;
        }

        EndCapture();
        UpdateHotkeyLabel();
        SetHint(HotkeyHint, $"已改为 {spec.Display}。", isError: false);
        FlashHotkeyPill();
    }

    /// <summary>当前按住哪些修饰键（用键盘状态，比 e.KeyboardDevice 更稳）。</summary>
    private static uint CurrentModifiers() {
        uint modifiers = 0;
        var pressed = Keyboard.Modifiers;

        if ((pressed & ModifierKeys.Control) != 0) modifiers |= NativeMethods.ModControl;
        if ((pressed & ModifierKeys.Alt) != 0) modifiers |= NativeMethods.ModAlt;
        if ((pressed & ModifierKeys.Shift) != 0) modifiers |= NativeMethods.ModShift;
        if ((pressed & ModifierKeys.Windows) != 0) modifiers |= NativeMethods.ModWin;

        return modifiers;
    }

    /// <summary>结束录制：恢复旧热键（如果这次没改成），还原输入框样式。</summary>
    private void EndCapture() {
        if (!_capturing)
            return;

        _capturing = false;

        (Application.Current as App)?.Hotkey?.Resume();

        UpdateHotkeyLabel();
    }

    /// <summary>改成功后闪一下，给个「确实生效了」的反馈。</summary>
    private void FlashHotkeyPill() {
        var accent = ThemeManager.Frozen(ThemeManager.ThemeColor(ThemeManager.Accent));

        HotkeyPill.BorderBrush = accent;

        var fade = new ColorAnimation {
            To = ThemeManager.ThemeColor(ThemeManager.BorderSubtle),
            Duration = TimeSpan.FromMilliseconds(700),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var brush = new SolidColorBrush(accent.Color);
        HotkeyPill.BorderBrush = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, fade);

        fade.Completed += (_, _) => UpdateHotkeyLabel();
    }

    // ===== 提示行 =====
    // 面板高度固定，腾不出单独一行做状态栏，于是让设置项的副标题兼作状态行：
    // 出错时变红说原因，正常时还原成原本的描述。

    private void RegisterHint(TextBlock hint) {
        _hintDefaults[hint] = hint.Text;
    }

    private void SetHint(TextBlock hint, string text, bool isError) {
        hint.Text = text;

        if (isError)
            _errorHints.Add(hint);
        else
            _errorHints.Remove(hint);

        ApplyHintColor(hint);
    }

    private void ResetHint(TextBlock hint) {
        if (!_hintDefaults.TryGetValue(hint, out string? text))
            return;

        _errorHints.Remove(hint);
        hint.Text = text;
        ApplyHintColor(hint);
    }

    private void ResetHints() {
        foreach (var hint in _hintDefaults.Keys)
            ResetHint(hint);
    }

    private void ApplyHintColor(TextBlock hint) {
        hint.Foreground = ThemeManager.Brush(_errorHints.Contains(hint)
            ? ThemeManager.Danger
            : ThemeManager.TextFaint);
    }

    /// <summary>换主题后画刷换了，正在用的副标题要重新取一次颜色。</summary>
    private void ApplyHintColors() {
        foreach (var hint in _hintDefaults.Keys)
            ApplyHintColor(hint);
    }

    // ===== 导航 =====

    private void Back_Click(object sender, RoutedEventArgs e) {
        EndCapture();

        if (Window.GetWindow(this) is MainWindow window)
            window.ShowDefaultPage();
    }
}
