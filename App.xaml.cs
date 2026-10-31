using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace WidgetNest;

public partial class App : System.Windows.Application {
    private MainWindow? _mainWindow;
    private HotkeyService? _hotkey;
    private Forms.NotifyIcon? _trayIcon;

    /// <summary>全局热键服务（偏好设置页录制新组合时要用）。</summary>
    internal HotkeyService? Hotkey => _hotkey;

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        // 单实例：第二个实例只负责把老实例叫出来，然后自己退出。
        // 提权重启时老实例还在退，新实例得等它放锁（否则会把自己当成第二个实例踢掉）。
        bool restarting = e.Args.Any(arg =>
            string.Equals(arg, SingleInstance.RestartArgument, StringComparison.OrdinalIgnoreCase));

        if (!SingleInstance.TryAcquire(restarting ? TimeSpan.FromSeconds(6) : TimeSpan.Zero)) {
            SingleInstance.SignalRunningInstance();
            Shutdown();
            return;
        }

        // 让托盘右键菜单用系统主题样式（必须在创建任何 WinForms 控件之前调用）
        try {
            Forms.Application.EnableVisualStyles();
        }
        catch {
            // 样式化失败不影响功能
        }

        // 启动即建好数据目录（同时触发旧目录的一次性迁移）
        AppStorage.EnsureCreated();

        // 开机自启的启动项里存的是绝对路径：程序搬过家（比如从调试目录换到发布目录）时顺手改过来
        StartupRegistration.RepairPathIfNeeded();

        // 先定主题再建窗口，避免第一帧闪一下深色
        ThemeManager.Apply();

        // 有第二个实例来敲门时，把面板亮出来
        SingleInstance.StartListening(Dispatcher, ShowPanel);

        CreateHotkeySink();
        SetupTrayIcon();
    }

    // ===== 全局热键 =====

    private void CreateHotkeySink() {
        _hotkey = new HotkeyService();
        _hotkey.Pressed += ToggleMainWindow;

        // 系统设置变更时同步一次主题（跟随系统模式下才真的会变）
        _hotkey.SettingChanged += ThemeManager.OnSystemThemeChanged;

        RegisterHotkeyWithRetry(0);
    }

    /// <summary>
    /// 注册唤出热键。从托盘「以管理员身份重启」时，新实例可能比旧实例先起来，
    /// 热键还占着就会注册失败；这里退避重试几次，真的被别的程序占用才提示。
    /// </summary>
    private void RegisterHotkeyWithRetry(int attempt) {
        if (_hotkey is null)
            return;

        if (_hotkey.TryRegister(PrefStore.Hotkey, out string error)) {
            UpdateTrayText();
            return;
        }

        if (attempt < 5) {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

            timer.Tick += (_, _) => {
                timer.Stop();
                RegisterHotkeyWithRetry(attempt + 1);
            };

            timer.Start();
            return;
        }

        System.Windows.MessageBox.Show(
            error + "\n可以先在「更多 → 偏好设置」里换一个组合。",
            "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// 换成新的唤出快捷键：注册成功才写配置，失败保留原组合。
    /// </summary>
    public bool TryChangeHotkey(HotkeySpec spec, out string error) {
        error = "";

        if (_hotkey is null) {
            error = "快捷键服务还没准备好。";
            return false;
        }

        if (!_hotkey.TryRegister(spec, out error))
            return false;

        PrefStore.SetHotkey(spec);
        UpdateTrayText();

        return true;
    }

    private void UpdateTrayText() {
        if (_trayIcon is null)
            return;

        _trayIcon.Text = $"WidgetNest · {PrefStore.Hotkey.Display} 唤出/隐藏";
    }

    // ===== 托盘图标 =====

    private void SetupTrayIcon() {
        var menu = new Forms.ContextMenuStrip();

        var showItem = new Forms.ToolStripMenuItem("显示面板");
        showItem.Click += (_, _) => ShowPanel();
        menu.Items.Add(showItem);

        // CPU / 硬盘温度要管理员权限，没提权时在这里给一条现成的路
        var adminItem = new Forms.ToolStripMenuItem(
            Elevation.IsElevated ? "已以管理员身份运行" : "以管理员身份重启（读取温度）") {
            Enabled = !Elevation.IsElevated
        };

        adminItem.Click += (_, _) => {
            if (Elevation.RestartElevated())
                ExitApp();
        };

        menu.Items.Add(adminItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _trayIcon = new Forms.NotifyIcon {
            Icon = CreateTrayIcon(),
            Text = $"WidgetNest · {PrefStore.Hotkey.Display} 唤出/隐藏",
            ContextMenuStrip = menu,
            Visible = true
        };

        // 左键点一下等同于热键：唤出/隐藏面板
        _trayIcon.MouseClick += (_, args) => {
            if (args.Button == Forms.MouseButtons.Left)
                ToggleMainWindow();
        };
    }

    /// <summary>
    /// 运行时绘制托盘图标：深色圆角底 + 2×2 白色圆角方块，对应面板的图标网格。
    /// </summary>
    private static Drawing.Icon CreateTrayIcon() {
        using var bitmap = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var g = Drawing.Graphics.FromImage(bitmap)) {
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            using (var back = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 38, 38, 38)))
                FillRoundedRect(g, back, new Drawing.Rectangle(0, 0, 32, 32), 7f);

            using (var cell = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 242, 242, 242))) {
                const int size = 8;
                const int gap = 2;
                const int offset = 7;

                FillRoundedRect(g, cell, new Drawing.Rectangle(offset, offset, size, size), 2f);
                FillRoundedRect(g, cell, new Drawing.Rectangle(offset + size + gap, offset, size, size), 2f);
                FillRoundedRect(g, cell, new Drawing.Rectangle(offset, offset + size + gap, size, size), 2f);
                FillRoundedRect(g, cell, new Drawing.Rectangle(offset + size + gap, offset + size + gap, size, size), 2f);
            }
        }

        // GetHicon 返回的句柄必须自己归还，Icon 只保留一份克隆
        IntPtr handle = bitmap.GetHicon();
        try {
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();
        }
        finally {
            DestroyIcon(handle);
        }
    }

    private static void FillRoundedRect(Drawing.Graphics g, Drawing.Brush brush,
                                        Drawing.Rectangle rect, float radius) {
        float d = radius * 2;

        using var path = new Drawing2D.GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        g.FillPath(brush, path);
    }

    // ===== 窗口与退出 =====

    private void ToggleMainWindow() {
        _mainWindow ??= new MainWindow();  // 构造时已完成一次性定位

        if (_mainWindow.IsVisible) {
            _mainWindow.Hide();
        }
        else {
            ShowPanel();
        }
    }

    private void ShowPanel() {
        _mainWindow ??= new MainWindow();

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ExitApp() {
        // 主窗口 Closing 里会取消关闭改成隐藏，退出前必须先放行
        _mainWindow?.AllowClose();

        DisposeTrayIcon();
        Shutdown();
    }

    private void DisposeTrayIcon() {
        if (_trayIcon is null)
            return;

        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    protected override void OnExit(ExitEventArgs e) {
        DisposeTrayIcon();

        _hotkey?.Dispose();
        _hotkey = null;

        // 放掉单实例锁，下次启动才不会被自己挡住
        SingleInstance.Release();

        base.OnExit(e);
    }

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
