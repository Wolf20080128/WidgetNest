using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WidgetNest;

public partial class AddAppPage : UserControl {
    public event Action<string>? AppDropRequested;

    /// <summary>在小组件市场里点了「添加」。</summary>
    public event Action<WidgetDef>? WidgetAddRequested;

    /// <summary>在小组件市场里移除了一个自定义组件（参数是家族键），面板上的实例要一起摘掉。</summary>
    public event Action<string>? WidgetRemoveRequested;

    private DispatcherTimer? _statusTimer;

    public AddAppPage() {
        InitializeComponent();

        MarketView.AddRequested += def => WidgetAddRequested?.Invoke(def);
        MarketView.RemoveRequested += key => WidgetRemoveRequested?.Invoke(key);

        // 第一次进「添加组件」就把开发说明和示例铺到本地：想自己写组件的人随手就能改
        WidgetExtensions.EnsureSamples();

        ResetToDefaultPage();
    }

    /// <summary>
    /// 重置为默认状态：选中“自定义组件”页。
    /// </summary>
    public void ResetToDefaultPage() {
        CustomComponentsTab.IsChecked = true;
        MarketTab.IsChecked = false;
        ShowCustomComponentsPage();
    }

    private void CustomComponentsTab_Checked(object sender, RoutedEventArgs e) => ShowCustomComponentsPage();

    private void MarketTab_Checked(object sender, RoutedEventArgs e) => ShowMarketPage();

    private void ShowCustomComponentsPage() {
        if (CustomComponentsPage is null || MarketPage is null) return;

        CustomComponentsPage.Visibility = Visibility.Visible;
        MarketPage.Visibility = Visibility.Collapsed;
    }

    private void ShowMarketPage() {
        if (CustomComponentsPage is null || MarketPage is null) return;

        CustomComponentsPage.Visibility = Visibility.Collapsed;
        MarketPage.Visibility = Visibility.Visible;
    }

    // ===== 拖放 =====

    private void DropZone_DragOver(object sender, DragEventArgs e) {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
            e.Effects = DragDropEffects.Copy;
            DropZone.Stroke = new SolidColorBrush(ThemeManager.Ink(0xAA));
        }
        else {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) => RestoreStroke();

    private void DropZone_Drop(object sender, DragEventArgs e) {
        RestoreStroke();

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
            e.Handled = true;
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;

        var widgets = new List<WidgetImportItem>();
        var failures = new List<string>();

        foreach (string path in files) {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            // 不是组件包就按原来的逻辑当应用添加，图标从源文件里提
            if (!WidgetExtensions.LooksLikeWidget(path)) {
                AppDropRequested?.Invoke(path);
                continue;
            }

            var widget = WidgetExtensions.ReadSource(path, out string error);

            if (widget == null)
                failures.Add($"{Path.GetFileName(path)}：{error}");
            else
                widgets.Add(new WidgetImportItem(widget, path));
        }

        if (widgets.Count > 0)
            ImportWidgets(widgets);

        if (failures.Count > 0)
            ShowStatus(string.Join("\n", failures), error: true);

        e.Handled = true;
    }

    /// <summary>
    /// 确认 → 落盘 → 刷新目录 → 跳到市场并标出新组件。
    /// 组件清单里可能带命令，所以必须先让用户看清要执行什么。
    /// </summary>
    private void ImportWidgets(List<WidgetImportItem> widgets) {
        if (!WidgetImportDialog.Confirm(Window.GetWindow(this), widgets)) {
            ShowStatus("已取消导入", error: false);
            return;
        }

        var installed = new List<UserWidget>();
        var failures = new List<string>();

        foreach (var item in widgets) {
            if (WidgetExtensions.Install(item.Widget, out string error))
                installed.Add(item.Widget);
            else
                failures.Add($"{item.Widget.Name}：{error}");
        }

        if (installed.Count > 0) {
            WidgetCatalog.Refresh();
            MarketView.Reload();

            // 跳到市场并把新组件闪一下，比留在原地弹一行字更直观
            MarketTab.IsChecked = true;
            MarketView.FocusFamily(installed[0].FamilyKey);
        }

        if (failures.Count > 0)
            ShowStatus(string.Join("\n", failures), error: true);
    }

    // ===== 提示 =====

    private void ShowStatus(string text, bool error) {
        if (string.IsNullOrWhiteSpace(text)) {
            DropStatus.Visibility = Visibility.Collapsed;
            DropHint.Visibility = Visibility.Visible;
            return;
        }

        // 提示文字在「自定义组件」页里，用户此刻可能停在市场页，先切回去才看得见
        if (MarketTab.IsChecked == true)
            CustomComponentsTab.IsChecked = true;

        // 有提示时让出中间的提示区，长错误信息也不会压到加号上
        DropHint.Visibility = Visibility.Collapsed;

        DropStatus.Text = text;
        DropStatus.Foreground = ThemeManager.Brush(error ? ThemeManager.Danger : ThemeManager.TextTertiary);
        DropStatus.Visibility = Visibility.Visible;

        _statusTimer?.Stop();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(error ? 7 : 4) };

        _statusTimer.Tick += (_, _) => {
            _statusTimer?.Stop();
            DropStatus.Visibility = Visibility.Collapsed;
            DropHint.Visibility = Visibility.Visible;
        };

        _statusTimer.Start();
    }

    private void RestoreStroke() => DropZone.Stroke = ThemeManager.Brush(ThemeManager.BorderStrong);

    private void DevLink_Click(object sender, MouseButtonEventArgs e) {
        try {
            WidgetExtensions.EnsureSamples();

            Process.Start(new ProcessStartInfo("explorer.exe",
                $"\"{AppStorage.WidgetSampleDir}\"") { UseShellExecute = true });
        }
        catch {
            ShowStatus("打不开示例目录", error: true);
        }

        e.Handled = true;
    }
}
