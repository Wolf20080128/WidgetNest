using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WidgetNest;

/// <summary>一条待导入的组件：解析出来的清单 + 用户拖进来的路径。</summary>
internal sealed record WidgetImportItem(UserWidget Widget, string Source);

/// <summary>
/// 导入确认窗：把拖进来的组件清单先给用户看清楚——叫什么、支持哪些尺寸、
/// 会执行哪些命令，确认后才落盘。
/// </summary>
internal sealed class WidgetImportDialog : Window {
    private readonly List<WidgetImportItem> _items;

    private WidgetImportDialog(List<WidgetImportItem> items) {
        _items = items;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 392;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildContent();

        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Escape)
                Close();
            else if (e.Key == Key.Enter)
                Accept();
        };
    }

    /// <summary>弹窗确认；返回 true 表示用户点了「安装」。</summary>
    public static bool Confirm(Window? owner, List<WidgetImportItem> items) {
        var dialog = new WidgetImportDialog(items);

        if (owner != null && owner.IsLoaded)
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return dialog.ShowDialog() == true;
    }

    private void Accept() {
        DialogResult = true;
        Close();
    }

    private FrameworkElement BuildContent() {
        var host = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };

        // ---- 标题 ----
        var title = W.Text(
            _items.Count > 1 ? $"导入 {_items.Count} 个小组件" : "导入小组件",
            13, ThemeManager.Brush(ThemeManager.TextPrimary), FontWeights.SemiBold);

        host.Children.Add(title);

        var subtitle = W.Text("导入后出现在「小组件市场」，可以选尺寸放到面板上，也可以右键移除。",
            9.5, ThemeManager.Brush(ThemeManager.TextTertiary));
        subtitle.TextWrapping = TextWrapping.Wrap;
        subtitle.Margin = new Thickness(0, 5, 0, 0);
        host.Children.Add(subtitle);

        // ---- 组件清单 ----
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

        foreach (var item in _items)
            list.Children.Add(BuildItem(item.Widget, item.Source));

        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 240,
            Content = list
        };

        host.Children.Add(scroll);

        // ---- 安全提示 ----
        if (_items.Any(item => item.Widget.Data.Any(data => data.Kind == WidgetDataKind.Command) ||
                               item.Widget.ActionRun.Length > 0)) {
            var warning = W.Text("自定义组件可以执行命令，请只导入你自己写的或信任的来源。",
                9, W.WarnBrush);
            warning.TextWrapping = TextWrapping.Wrap;
            warning.Margin = new Thickness(0, 10, 0, 0);
            host.Children.Add(warning);
        }

        // ---- 按钮 ----
        var buttons = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        buttons.Children.Add(BuildButton("取消", primary: false, () => Close()));
        buttons.Children.Add(BuildButton("安装", primary: true, Accept));

        host.Children.Add(buttons);

        var card = new Border {
            Background = ThemeManager.Brush(ThemeManager.PopupBackground),
            BorderBrush = ThemeManager.Brush(ThemeManager.BorderSubtle),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.5 },
            Child = host
        };

        card.MouseLeftButtonDown += (_, e) => {
            // 只处理点在本体空白处的情况，别抢按钮的点击
            if (ReferenceEquals(e.OriginalSource, card))
                DragMove();
        };

        return card;
    }

    private FrameworkElement BuildItem(UserWidget widget, string source) {
        var card = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = W.Icon(widget.IconPath, 18, ThemeManager.Brush(ThemeManager.TextSecondary));
        icon.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(icon);

        var name = W.Text(widget.Name, 12, ThemeManager.Brush(ThemeManager.TextPrimary),
            FontWeights.SemiBold);
        Grid.SetColumn(name, 1);
        header.Children.Add(name);

        string sizes = string.Join(" · ", widget.Sizes
            .OrderBy(size => size.Rows * size.Cols)
            .ThenBy(size => size.Cols)
            .Select(size => $"{size.Rows}×{size.Cols}"));
        var sizeText = W.Text(sizes, 9, ThemeManager.Brush(ThemeManager.TextFaint));
        sizeText.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(sizeText, 2);
        header.Children.Add(sizeText);

        card.Children.Add(header);

        if (!string.IsNullOrWhiteSpace(widget.Description)) {
            var description = W.Text(widget.Description, 9.5, ThemeManager.Brush(ThemeManager.TextSecondary));
            description.TextWrapping = TextWrapping.Wrap;
            description.Margin = new Thickness(26, 3, 0, 0);
            card.Children.Add(description);
        }

        string byline = string.Join(" · ",
            new[] { widget.Author, widget.Version.Length > 0 ? "v" + widget.Version : "" }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        if (byline.Length > 0) {
            var author = W.Text(byline, 8.5, ThemeManager.Brush(ThemeManager.TextFaint));
            author.Margin = new Thickness(26, 2, 0, 0);
            card.Children.Add(author);
        }

        var commands = widget.Data
            .Where(data => data.Kind == WidgetDataKind.Command)
            .Select(data => "运行 " + data.Run)
            .ToList();

        if (widget.ActionRun.Length > 0)
            commands.Add("点击执行 " + widget.ActionRun);

        foreach (string command in commands) {
            var box = new Border {
                Background = ThemeManager.Brush(ThemeManager.SurfaceInset),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(26, 4, 0, 0),
                Child = new TextBlock {
                    Text = command,
                    FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
                    FontSize = 8.5,
                    Foreground = ThemeManager.Brush(ThemeManager.TextTertiary),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 300
                }
            };

            card.Children.Add(box);
        }

        // 目录导入时标一下来源，方便用户确认是哪一份
        if (widget.FromFolder) {
            var path = W.Text(source, 8, ThemeManager.Brush(ThemeManager.TextFaint));
            path.Margin = new Thickness(26, 3, 0, 0);
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            card.Children.Add(path);
        }

        var separator = new Border {
            Height = 1,
            Background = ThemeManager.Brush(ThemeManager.Divider),
            Margin = new Thickness(0, 8, 0, 0)
        };

        card.Children.Add(separator);
        return card;
    }

    private FrameworkElement BuildButton(string text, bool primary, Action click) {
        var label = W.Text(text, 10.5,
            primary ? ThemeManager.Brush(ThemeManager.TextOnAccent) : ThemeManager.Brush(ThemeManager.TextSecondary),
            primary ? FontWeights.SemiBold : FontWeights.Normal);

        label.HorizontalAlignment = HorizontalAlignment.Center;

        var button = new Border {
            Height = 28,
            MinWidth = primary ? 68 : 56,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(8, 0, 0, 0),
            Background = primary
                ? ThemeManager.Brush(ThemeManager.Accent)
                : ThemeManager.Brush(ThemeManager.SurfaceSubtle),
            Cursor = Cursors.Hand,
            Child = label
        };

        button.MouseEnter += (_, _) => button.Opacity = 0.86;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        button.MouseLeftButtonUp += (_, e) => {
            click();
            e.Handled = true;
        };

        return button;
    }
}
