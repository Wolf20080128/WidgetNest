using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace WidgetNest;

/// <summary>
/// 全局热键的按键组合：Win32 修饰键位（MOD_*）+ WPF 按键。
/// 存档里用 "Ctrl+Shift+A" 这种可读形式，换机器/改配置都好认。
/// </summary>
public readonly record struct HotkeySpec(uint Modifiers, Key Key) {
    public static HotkeySpec Default =>
        new(NativeMethods.ModControl | NativeMethods.ModShift, Key.A);

    /// <summary>至少有一个修饰键，否则会抢占普通打字。</summary>
    public bool HasModifier =>
        (Modifiers & (NativeMethods.ModControl | NativeMethods.ModAlt |
                      NativeMethods.ModShift | NativeMethods.ModWin)) != 0;

    public bool IsValid => Key != Key.None && HasModifier;

    /// <summary>发给 RegisterHotKey 的虚拟键码。</summary>
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>界面上显示的形式：Ctrl + Shift + A。</summary>
    public string Display {
        get {
            if (Key == Key.None)
                return "未设置";

            var parts = new List<string>(5);

            if ((Modifiers & NativeMethods.ModControl) != 0) parts.Add("Ctrl");
            if ((Modifiers & NativeMethods.ModAlt) != 0) parts.Add("Alt");
            if ((Modifiers & NativeMethods.ModShift) != 0) parts.Add("Shift");
            if ((Modifiers & NativeMethods.ModWin) != 0) parts.Add("Win");

            parts.Add(KeyName(Key));

            return string.Join(" + ", parts);
        }
    }

    /// <summary>存档形式：Ctrl+Shift+A。</summary>
    public string ToStorage() {
        var parts = new List<string>(5);

        if ((Modifiers & NativeMethods.ModControl) != 0) parts.Add("Ctrl");
        if ((Modifiers & NativeMethods.ModAlt) != 0) parts.Add("Alt");
        if ((Modifiers & NativeMethods.ModShift) != 0) parts.Add("Shift");
        if ((Modifiers & NativeMethods.ModWin) != 0) parts.Add("Win");

        parts.Add(KeyName(Key));

        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out HotkeySpec spec) {
        spec = Default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        uint modifiers = 0;
        var key = Key.None;

        foreach (string token in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            switch (token.ToLowerInvariant()) {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.ModControl;
                    continue;

                case "alt":
                    modifiers |= NativeMethods.ModAlt;
                    continue;

                case "shift":
                    modifiers |= NativeMethods.ModShift;
                    continue;

                case "win":
                case "windows":
                    modifiers |= NativeMethods.ModWin;
                    continue;
            }

            if (!TryParseKey(token, out key))
                return false;
        }

        spec = new HotkeySpec(modifiers, key);

        return spec.IsValid;
    }

    private static bool TryParseKey(string token, out Key key) {
        key = Key.None;

        // 数字键在枚举里叫 D0–D9，存档里只写 0–9
        if (token.Length == 1 && char.IsDigit(token[0])) {
            key = Key.D0 + (token[0] - '0');
            return true;
        }

        if (token.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(token[3..], out int num) && num is >= 0 and <= 9) {
            key = Key.NumPad0 + num;
            return true;
        }

        return Enum.TryParse(token, ignoreCase: true, out key) && key != Key.None;
    }

    private static string KeyName(Key key) => key switch {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        _ => key.ToString()
    };
}

/// <summary>
/// 全局热键的注册与回收。用一个隐藏消息窗口接收 WM_HOTKEY，
/// 这样面板隐藏/失焦时热键依然有效。
/// </summary>
internal sealed class HotkeyService : IDisposable {
    private const int HotkeyId = 0x2024;

    private readonly HwndSource _sink;

    private HotkeySpec? _registered;
    private bool _isRegistered;

    /// <summary>热键被按下。</summary>
    public event Action? Pressed;

    /// <summary>系统设置变了（含深浅色主题切换）。</summary>
    public event Action? SettingChanged;

    public HotkeyService() {
        var parameters = new HwndSourceParameters("WidgetNestHotkeySink") {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _sink = new HwndSource(parameters);
        _sink.AddHook(WndProc);
    }

    public HotkeySpec? Registered => _registered;

    /// <summary>
    /// 换成新的组合键。失败时会尽量把旧的注册回来，保证用户原来的热键还能用。
    /// </summary>
    public bool TryRegister(HotkeySpec spec, out string error) {
        error = "";

        if (!spec.IsValid) {
            error = "快捷键需要至少一个修饰键（Ctrl / Alt / Shift / Win）。";
            return false;
        }

        if (_isRegistered && _registered == spec)
            return true;

        var previous = _registered;
        Unregister();

        if (Register(spec)) {
            _registered = spec;
            return true;
        }

        error = $"「{spec.Display}」已被其他程序占用，换一个组合试试。";

        // 回滚：旧组合注册不回来也只能认了，至少不要卡在「没有热键」的错觉里
        if (previous is { } restore && Register(restore)) {
            _registered = restore;
        }
        else {
            _registered = null;
        }

        return false;
    }

    /// <summary>暂停注册（录制新快捷键期间，避免按到旧组合把面板收起来）。</summary>
    public void Suspend() => Unregister();

    /// <summary>恢复上一次成功注册的组合。</summary>
    public void Resume() {
        if (_isRegistered || _registered is not { } spec)
            return;

        if (Register(spec))
            _isRegistered = true;
    }

    private bool Register(HotkeySpec spec) {
        // MOD_NOREPEAT：按住不放时不要连发，否则面板会被反复开关
        bool ok = NativeMethods.RegisterHotKey(_sink.Handle, HotkeyId,
            spec.Modifiers | NativeMethods.ModNoRepeat, spec.VirtualKey);

        if (ok)
            _isRegistered = true;

        return ok;
    }

    private void Unregister() {
        if (!_isRegistered)
            return;

        NativeMethods.UnregisterHotKey(_sink.Handle, HotkeyId);
        _isRegistered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId) {
            Pressed?.Invoke();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WmSettingChange) {
            SettingChanged?.Invoke();
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    public void Dispose() {
        Unregister();

        _sink.RemoveHook(WndProc);
        _sink.Dispose();
    }
}
