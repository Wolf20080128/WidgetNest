using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace WidgetNest;

/// <summary>
/// 小组件市场：6 列网格，一屏三行（18 格），组件多了上下滚。
/// 一个格子 = 一个小组件；尺寸不再做顶部筛选条，改成单击格子后在二级菜单里选，再点「添加到面板」。
/// </summary>
public partial class WidgetMarketView : UserControl {
    private const double TileHeight = 40;
    private const double MenuWidth = 172;

    private readonly Popup _menu = new() {
        AllowsTransparency = true,
        StaysOpen = false,
        Placement = PlacementMode.Custom,
        PopupAnimation = PopupAnimation.Fade
    };

    // 尺寸选项要按选中态统一重刷，留住引用
    private readonly List<(Border Chip, WidgetDef Variant)> _sizeChips = new();

    // 这些底色跟着主题走，主题一变整块重建
    private Brush _tileIdle = W.Frozen(W.Ink(0x0F));
    private Brush _tileHover = W.Frozen(W.Ink(0x1F));
    private Brush _chipIdle = W.Frozen(W.Ink(0x14));
    private Brush _chipActive = ThemeManager.Brush(ThemeManager.AccentSoft);

    private WidgetFamily? _menuFamily;
    private WidgetDef? _menuSelection;
    private TextBlock? _menuDescription;
    private bool _confirmRemove;

    private readonly Dictionary<string, Border> _tiles = new(StringComparer.Ordinal);

    public event Action<WidgetDef>? AddRequested;

    /// <summary>卸载了一个自定义组件（参数是家族键）。</summary>
    public event Action<string>? RemoveRequested;

    public WidgetMarketView() {
        InitializeComponent();

        ApplyPalette();
        Reload();

        // 格子与二级菜单都是现画的，主题变了整段重建
        ThemeManager.ThemeChanged += (_, _) => {
            CloseMenu();
            ApplyPalette();
            Reload();
        };

        // 切到「自定义组件」页、收起面板时，浮着的菜单不该留在屏幕上
        IsVisibleChanged += (_, _) => {
            if (!IsVisible)
                CloseMenu();
        };

        Unloaded += (_, _) => CloseMenu();

        // 点空白处自动关掉时也要把菜单状态清干净，下次点格子重新生成
        _menu.Closed += (_, _) => ResetMenuState();

        // 优先贴在被点的格子下方、水平居中；下面放不下就翻到格子上方
        _menu.CustomPopupPlacementCallback = (popupSize, targetSize, _) => new[] {
            new CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, targetSize.Height + 2),
                PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 2),
                PopupPrimaryAxis.Horizontal)
        };
    }

    private void ApplyPalette() {
        _tileIdle = W.Frozen(W.Ink(0x0F));
        _tileHover = W.Frozen(W.Ink(0x1F));
        _chipIdle = W.Frozen(W.Ink(0x14));
        _chipActive = ThemeManager.Brush(ThemeManager.AccentSoft);
    }

    // ===== 网格 =====

    /// <summary>重建网格。导入 / 移除自定义组件后要重读一遍目录。</summary>
    public void Reload() {
        TileGrid.Children.Clear();
        _tiles.Clear();

        var families = WidgetCatalog.Families;
        CountText.Text = $"{families.Count} 个";

        foreach (var family in families) {
            var tile = BuildTile(family);
            _tiles[family.Key] = tile;
            TileGrid.Children.Add(tile);
        }
    }

    /// <summary>把某个组件格子滚进视野并闪一下，导入完用来告诉用户「加在这里了」。</summary>
    public void FocusFamily(string familyKey) {
        if (!_tiles.TryGetValue(familyKey, out var tile))
            return;

        Dispatcher.BeginInvoke(new Action(() => {
            tile.BringIntoView();
            FlashAdded(tile);
        }), DispatcherPriority.Background);
    }

    private Border BuildTile(WidgetFamily family) {
        bool userWidget = family.Default.IsUserWidget;

        var icon = W.Icon(family.IconPath, 15, W.Frozen(W.Ink(0xD8)));
        icon.HorizontalAlignment = HorizontalAlignment.Center;

        // 自己导入的组件用强调色标出来，和内置组件区分开
        var name = W.Text(family.Name, 8.5,
            userWidget ? ThemeManager.Brush(ThemeManager.AccentText) : W.DimBrush);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.TextAlignment = TextAlignment.Center;
        name.Margin = new Thickness(1, 3, 1, 0);

        var stack = new StackPanel {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 3, 0)
        };

        stack.Children.Add(icon);
        stack.Children.Add(name);

        var tile = new Border {
            Height = TileHeight,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(7),
            Background = _tileIdle,
            Child = stack,
            Cursor = Cursors.Hand,
            ToolTip = userWidget
                ? $"{family.Name} · 自定义组件 · {family.SizeHint}"
                : $"{family.Name} · {family.SizeHint}"
        };

        tile.MouseEnter += (_, _) => tile.Background = _tileHover;
        tile.MouseLeave += (_, _) => tile.Background = _tileIdle;

        tile.MouseLeftButtonUp += (_, e) => {
            OpenMenu(family, tile);
            e.Handled = true;
        };

        // 右键也开同一张菜单：自定义组件在菜单底部多两个入口（打开目录 / 移除）
        tile.MouseRightButtonUp += (_, e) => {
            OpenMenu(family, tile);
            e.Handled = true;
        };

        return tile;
    }

    // ===== 二级菜单：选尺寸 + 添加 =====

    private void OpenMenu(WidgetFamily family, FrameworkElement anchor) {
        _menuFamily = family;
        _menuSelection = family.Default;
        _sizeChips.Clear();

        _menu.PlacementTarget = anchor;
        _menu.Child = BuildMenu();
        _menu.IsOpen = true;
    }

    private FrameworkElement BuildMenu() {
        var family = _menuFamily!;
        var selection = _menuSelection ?? family.Default;

        var host = new StackPanel { Width = MenuWidth };

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(W.Icon(family.IconPath, 13, W.DimBrush));

        var title = W.Text(family.Name, 12, W.TextBrush, FontWeights.SemiBold);
        title.Margin = new Thickness(5, 0, 0, 0);
        header.Children.Add(title);

        host.Children.Add(header);

        // 说明文字跟着选中的尺寸走
        var description = W.Text(selection.Description, 9, W.FaintBrush);
        description.TextWrapping = TextWrapping.Wrap;
        description.Margin = new Thickness(0, 4, 0, 0);
        host.Children.Add(description);
        _menuDescription = description;

        var caption = W.Text("选择大小", 8.5, W.FaintBrush);
        caption.Margin = new Thickness(0, 8, 0, 4);
        host.Children.Add(caption);

        var chips = new WrapPanel();

        foreach (var variant in family.Variants)
            chips.Children.Add(BuildSizeChip(variant));

        host.Children.Add(chips);

        var addText = W.Text("添加到面板", 10, ThemeManager.Brush(ThemeManager.TextOnAccent),
            FontWeights.SemiBold);
        addText.HorizontalAlignment = HorizontalAlignment.Center;

        var add = new Border {
            Height = 26,
            Margin = new Thickness(0, 8, 0, 0),
            CornerRadius = new CornerRadius(6),
            // 实底强调色：和上面「已选尺寸」的浅色胶囊拉开层级
            Background = ThemeManager.Brush(ThemeManager.Accent),
            Child = addText,
            Cursor = Cursors.Hand
        };

        add.MouseEnter += (_, _) => add.Opacity = 0.88;
        add.MouseLeave += (_, _) => add.Opacity = 1;
        add.MouseLeftButtonUp += (_, e) => {
            CommitAdd();
            e.Handled = true;
        };

        host.Children.Add(add);

        // 自定义组件的额外入口：看目录、卸掉
        if (family.Default.IsUserWidget) {
            host.Children.Add(BuildLink("打开组件文件夹",
                ThemeManager.Brush(ThemeManager.TextTertiary), () => {
                    CloseMenu();
                    OpenWidgetFolder(family);
                }));

            var removeLink = BuildLink("移除组件",
                ThemeManager.Brush(ThemeManager.TextTertiary), null);

            var label = (TextBlock)removeLink.Child;

            removeLink.MouseLeftButtonUp += (_, e) => {
                if (!_confirmRemove) {
                    // 二次确认就地点一下，不弹窗打断
                    _confirmRemove = true;
                    label.Text = "再点一次，移除组件";
                    label.Foreground = ThemeManager.Brush(ThemeManager.Danger);
                    return;
                }

                CloseMenu();
                RemoveFamily(family);
            };

            host.Children.Add(removeLink);
        }

        RefreshSizeChips();

        return new Border {
            Background = ThemeManager.Brush(ThemeManager.PopupBackground),
            BorderBrush = ThemeManager.Brush(ThemeManager.BorderSubtle),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 9, 10, 9),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.45 },
            Child = host
        };
    }

    private FrameworkElement BuildSizeChip(WidgetDef variant) {
        var label = new TextBlock {
            Text = $"{variant.SizeText} · {variant.SizeLabel}",
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center
        };

        var chip = new Border {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, 0, 4, 4),
            Child = label,
            Cursor = Cursors.Hand
        };

        var captured = variant;

        chip.MouseLeftButtonUp += (_, e) => {
            SelectSize(captured);
            e.Handled = true;
        };

        _sizeChips.Add((chip, variant));
        return chip;
    }

    private void SelectSize(WidgetDef variant) {
        _menuSelection = variant;

        if (_menuDescription != null)
            _menuDescription.Text = variant.Description;

        RefreshSizeChips();
    }

    private void RefreshSizeChips() {
        foreach (var (chip, variant) in _sizeChips) {
            bool active = ReferenceEquals(variant, _menuSelection);
            chip.Background = active ? _chipActive : _chipIdle;

            if (chip.Child is not TextBlock label)
                continue;

            label.Foreground = active ? ThemeManager.Brush(ThemeManager.AccentText) : W.DimBrush;
            label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void CommitAdd() {
        if (_menuSelection is not { } variant)
            return;

        var tile = _menu.PlacementTarget as Border;

        CloseMenu();
        AddRequested?.Invoke(variant);

        if (tile != null)
            FlashAdded(tile);
    }

    private void CloseMenu() {
        if (!_menu.IsOpen && _menu.Child == null)
            return;

        _menu.IsOpen = false;
        _menu.Child = null;
        ResetMenuState();
    }

    private void ResetMenuState() {
        _menuFamily = null;
        _menuSelection = null;
        _menuDescription = null;
        _confirmRemove = false;
        _sizeChips.Clear();
    }

    /// <summary>加完之后让格子闪一下，表示已经落到面板上了。</summary>
    private void FlashAdded(Border tile) {
        tile.Background = _chipActive;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };

        timer.Tick += (_, _) => {
            timer.Stop();

            // 鼠标可能还停在这个格子上
            tile.Background = tile.IsMouseOver ? _tileHover : _tileIdle;
        };

        timer.Start();
    }

    // ===== 自定义组件：看目录 / 移除 =====

    private Border BuildLink(string text, Brush brush, Action? click) {
        var label = W.Text(text, 9, brush);

        var link = new Border {
            Height = 22,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 4, 0, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = label
        };

        link.MouseEnter += (_, _) => link.Background = _chipIdle;
        link.MouseLeave += (_, _) => link.Background = Brushes.Transparent;

        if (click != null) {
            link.MouseLeftButtonUp += (_, e) => {
                click();
                e.Handled = true;
            };
        }

        return link;
    }

    private static void OpenWidgetFolder(WidgetFamily family) {
        var widget = WidgetExtensions.FindByFamily(family.Key);

        if (widget == null)
            return;

        string folder = Path.Combine(WidgetExtensions.RootDir, widget.Id);

        try {
            if (Directory.Exists(folder))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") {
                    UseShellExecute = true
                });
        }
        catch {
            // 打不开资源管理器就算了
        }
    }

    /// <summary>卸掉一个自定义组件：先让面板摘掉实例，再删磁盘上的目录。</summary>
    private void RemoveFamily(WidgetFamily family) {
        var widget = WidgetExtensions.FindByFamily(family.Key);

        if (widget == null)
            return;

        RemoveRequested?.Invoke(family.Key);

        if (!WidgetExtensions.Uninstall(widget, out _))
            return;

        WidgetCatalog.Refresh();
        Reload();
    }
}
