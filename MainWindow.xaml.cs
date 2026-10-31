using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WidgetNest;

public partial class MainWindow : Window {
    private const double MenuCollapsedWidth = 36;
    private const double MenuExpandedWidth = 170;
    private const double MenuAnimMs = 150;

    private bool _moreMenuOpen;
    private AddAppPage? _addAppPage;
    private ProfilePage? _profilePage;
    private PreferencesPage? _preferencesPage;

    // 托盘「退出」需要真正关窗，否则会被下面的 Closing 取消逻辑拦住
    private bool _allowClose;

    public MainWindow() {
        InitializeComponent();

        PlaceAtBottomRight();

        HomeView.SelectionChanged += (_, _) => UpdateBottomBar();
        HomeView.EditModeChanged += (_, _) => {
            // 编辑模式结束（例如删空后自动退出）时把开关拨回去
            EditButton.IsChecked = HomeView.IsEditMode;
            UpdateBottomBar();
        };

        Closing += (_, e) => {
            if (_allowClose)
                return;

            e.Cancel = true;
            Hide();
        };

        // 面板被收起（热键隐藏等）时顺手关掉确认浮层，下次唤出不会顶着个弹窗
        IsVisibleChanged += (_, _) => {
            if (!IsVisible)
                ConfirmOverlay.Visibility = Visibility.Collapsed;
        };

        // 底栏、圆角外边距这些地方不属于 HomePage，滚轮本来到不了它，这里转一手
        PreviewMouseWheel += OnWindowPreviewMouseWheel;
    }

    private void OnWindowPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        // 功能页、更多菜单、确认浮层打开时不该偷偷翻底下那页
        if (DefaultPage.Visibility != Visibility.Visible || _moreMenuOpen)
            return;

        if (ConfirmOverlay.Visibility == Visibility.Visible)
            return;

        // 落在 HomeView 里的滚轮由 HomeView 自己处理，别再翻一次
        if (IsWithin(e.OriginalSource as DependencyObject, HomeView))
            return;

        if (HomeView.TryScrollPage(e.Delta))
            e.Handled = true;
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject root) {
        for (var current = node; current != null; current = VisualTreeHelper.GetParent(current)) {
            if (ReferenceEquals(current, root))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 放行关闭：此后 Close() 不再被拦成隐藏。
    /// </summary>
    public void AllowClose() => _allowClose = true;

    private void PlaceAtBottomRight(double margin = 16) {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - margin;
        Top = workArea.Bottom - Height - margin;
    }

    public void ShowFeature(object content) {
        FeatureContent.Content = content;
        DefaultPage.Visibility = Visibility.Collapsed;
    }

    public void ShowDefaultPage() {
        FeatureContent.Content = null;
        DefaultPage.Visibility = Visibility.Visible;
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) {
        // 主页按钮同时承担「退出编辑」的职责
        if (HomeView.IsEditMode)
            SetEditMode(false);

        ShowDefaultPage();
    }

    // ===== 编辑模式 =====

    private void SetEditMode(bool on) {
        if (on) {
            CloseMoreMenu();
            ShowDefaultPage();
        }

        EditButton.IsChecked = on;
        HomeView.SetEditMode(on);
        UpdateBottomBar();
    }

    /// <summary>
    /// 底栏在「常规」与「选中」两种形态间切换：有选中项时整条底栏换掉。
    /// </summary>
    private void UpdateBottomBar() {
        bool hasSelection = HomeView.SelectedCount > 0;

        NormalBar.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        SelectionBar.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;

        UpdateCombineButton();
    }

    /// <summary>
    /// 组合按钮默认隐藏，只有选中两个相邻小组件时才出现；
    /// 选中的正好是同一组的两块时，按钮变成「拆分」。
    /// </summary>
    private void UpdateCombineButton() {
        var action = HomeView.SelectionCombineAction;

        CombineButton.Visibility = action == CombineAction.None
            ? Visibility.Collapsed
            : Visibility.Visible;

        bool split = action == CombineAction.Split;

        CombineText.Text = split ? "拆分" : "组合";
        CombineButton.ToolTip = split ? "拆开这一组" : "把相邻的两个小组件合成一个";
        CombineIcon.Data = Geometry.Parse(split ? UngroupIconPath : GroupIconPath);
    }

    private const string GroupIconPath =
        "M17,20.41 L18.41,19 L15,15.59 L13.59,17 Z M7.5,8 H11 V13.59 L5.59,19 L7,20.41 L13,14.41 V8 H16.5 L12,3.5 Z";

    private const string UngroupIconPath =
        "M3,3 H9 V9 H3 Z M15,15 H21 V21 H15 Z M11.2,12.8 L12.8,11.2 L8.9,7.3 L7.3,8.9 Z";

    private void EditButton_Click(object sender, RoutedEventArgs e) {
        // ToggleButton 自己已经翻转了 IsChecked，这里只跟随它
        SetEditMode(EditButton.IsChecked == true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => SetEditMode(false);

    private void CombineButton_Click(object sender, RoutedEventArgs e) {
        HomeView.ApplyCombineAction();
        UpdateBottomBar();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e) {
        int count = HomeView.SelectedCount;
        if (count == 0)
            return;

        ShowDeleteConfirm(count);
    }

    // ===== 删除确认浮层 =====

    private void ShowDeleteConfirm(int count) {
        ConfirmTitle.Text = $"删除选中的 {count} 个图标？";
        ConfirmMessage.Text = "只会从面板移除，不会删除原始文件。";

        ConfirmOverlay.Visibility = Visibility.Visible;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        ConfirmOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = ease
        });

        ConfirmCardOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation {
            From = -12,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = ease
        });
    }

    private void HideDeleteConfirm() {
        var fade = new DoubleAnimation {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(110),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fade.Completed += (_, _) => ConfirmOverlay.Visibility = Visibility.Collapsed;

        ConfirmOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private void ConfirmDelete_Click(object sender, RoutedEventArgs e) {
        HideDeleteConfirm();

        HomeView.RemoveSelected();
        UpdateBottomBar();
    }

    private void CancelDelete_Click(object sender, RoutedEventArgs e) => HideDeleteConfirm();

    /// <summary>点击卡片外的遮罩 = 取消。</summary>
    private void ConfirmScrim_Click(object sender, MouseButtonEventArgs e) {
        HideDeleteConfirm();
        e.Handled = true;
    }

    /// <summary>浮层是否正在显示。</summary>
    private bool IsConfirmOpen => ConfirmOverlay.Visibility == Visibility.Visible;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (!IsConfirmOpen)
            return;

        switch (e.Key) {
            case Key.Escape:
                HideDeleteConfirm();
                e.Handled = true;
                break;

            case Key.Enter:
                ConfirmDelete_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    // ===== 更多菜单 =====

    private void MoreButton_Click(object sender, RoutedEventArgs e) {
        if (_moreMenuOpen) CloseMoreMenu();
        else OpenMoreMenu();
    }

    private void OpenMoreMenu() {
        _moreMenuOpen = true;

        // 清除旧动画，回到收起基线
        MoreMenu.BeginAnimation(FrameworkElement.WidthProperty, null);
        MoreMenu.BeginAnimation(FrameworkElement.HeightProperty, null);
        MoreMenu.Width = MenuCollapsedWidth;
        MoreMenu.Height = 0;
        MoreMenu.Visibility = Visibility.Visible;

        // 测量内容目标高度（StackPanel 固定宽度，不受动画宽度影响）
        MoreMenuItems.Measure(new Size(MenuExpandedWidth, double.PositiveInfinity));
        double targetHeight = MoreMenuItems.DesiredSize.Height;

        // 1) 先向左展开宽度
        var widthAnim = new DoubleAnimation {
            To = MenuExpandedWidth,
            Duration = TimeSpan.FromMilliseconds(MenuAnimMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        MoreMenu.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

        // 2) 宽度完成后再向上展开高度
        var heightAnim = new DoubleAnimation {
            To = targetHeight,
            BeginTime = TimeSpan.FromMilliseconds(MenuAnimMs),
            Duration = TimeSpan.FromMilliseconds(MenuAnimMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        MoreMenu.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
    }

    private void CloseMoreMenu() {
        _moreMenuOpen = false;

        // 1) 先收起高度
        var heightAnim = new DoubleAnimation {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(MenuAnimMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        MoreMenu.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);

        // 2) 高度完成后再收回宽度
        var widthAnim = new DoubleAnimation {
            To = MenuCollapsedWidth,
            BeginTime = TimeSpan.FromMilliseconds(MenuAnimMs),
            Duration = TimeSpan.FromMilliseconds(MenuAnimMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        widthAnim.Completed += (_, _) => {
            if (_moreMenuOpen) return;

            // 清理动画并重置为收起态
            MoreMenu.BeginAnimation(FrameworkElement.WidthProperty, null);
            MoreMenu.BeginAnimation(FrameworkElement.HeightProperty, null);
            MoreMenu.Width = MenuCollapsedWidth;
            MoreMenu.Height = 0;
            MoreMenu.Visibility = Visibility.Collapsed;
        };
        MoreMenu.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
    }

    // ===== 菜单项 =====

    /// <summary>
    /// 「我的主页」= 账号页（登录 / 注册 / 签到 / 资料），不是回到图标主页。
    /// </summary>
    private void MenuProfile_Click(object sender, RoutedEventArgs e) {
        CloseMoreMenu();

        _profilePage ??= new ProfilePage();

        // 每次进来都同步一次存档：跨天签到状态、退出后的登录态
        _profilePage.Refresh();
        ShowFeature(_profilePage);
    }

    private void MenuPreferences_Click(object sender, RoutedEventArgs e) {
        CloseMoreMenu();

        _preferencesPage ??= new PreferencesPage();
        ShowFeature(_preferencesPage);
    }

    private void MenuAddApp_Click(object sender, RoutedEventArgs e) {
        CloseMoreMenu();

        var page = EnsureAddAppPage();
        page.ResetToDefaultPage();
        ShowFeature(page);
    }

    private AddAppPage EnsureAddAppPage() {
        if (_addAppPage != null)
            return _addAppPage;

        _addAppPage = new AddAppPage();

        _addAppPage.AppDropRequested += path => {
            HomeView.AddApp(path);
            ShowDefaultPage();
        };

        // 小于一页的小组件加完就留在市场，方便连续添加；整页组件回主页让用户看到效果
        _addAppPage.WidgetAddRequested += def => {
            HomeView.AddWidget(def);

            if (def.Rows * def.Cols >= 18)
                ShowDefaultPage();
        };

        // 市场里卸掉自定义组件时，面板上的实例一起摘掉
        _addAppPage.WidgetRemoveRequested += key => HomeView.RemoveWidgetFamily(key);

        return _addAppPage;
    }
}