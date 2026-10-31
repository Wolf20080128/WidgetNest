using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WidgetNest;

/// <summary>
/// 天气组件的位置选择窗：省 → 市 → 区县 三级联选。
/// 点到最后一级即生效；直辖市、无下级的地级市、没有下级的省，点到就算是最后一级。
/// 结果写回 <see cref="WidgetDataStore.Weather"/>。
/// </summary>
public partial class LocationWindow : Window {
    private readonly List<(RegionNode Node, Border Row)> _provinceRows = new();
    private readonly List<(RegionNode Node, Border Row)> _cityRows = new();
    private readonly List<(RegionNode Node, Border Row)> _countyRows = new();

    /// <summary>当前高亮的省 / 市（只有真的存在市级时才非空）/ 末级。</summary>
    private RegionNode? _province;
    private RegionNode? _city;
    private RegionNode? _leaf;

    private bool _applied;

    public LocationWindow(WeatherPlace? current) {
        InitializeComponent();

        WeatherPlace? place = current ?? WidgetDataStore.Weather;

        FillColumn(ProvincePanel, _provinceRows, ChinaRegions.Provinces, SelectProvince, "行政区划数据缺失");
        FillColumn(CityPanel, _cityRows, Array.Empty<RegionNode>(), SelectCity, "先选省份");
        FillColumn(CountyPanel, _countyRows, Array.Empty<RegionNode>(), SelectCounty, "先选城市");

        RestoreSelection(place);
        UpdateHint(place);

        Loaded += (_, _) => {
            PlaceNearOwner();
            Dispatcher.BeginInvoke(new Action(ScrollToSelection), DispatcherPriority.Loaded);
        };
    }

    // ===== 三级联选 =====

    private void SelectProvince(RegionNode node) {
        _province = node;
        _city = null;
        _leaf = node;

        Paint(_provinceRows, _province);
        FillColumn(CityPanel, _cityRows, node.Children, SelectCity, "该省无下级");
        FillColumn(CountyPanel, _countyRows, Array.Empty<RegionNode>(), SelectCounty, "先选城市");
        Paint(_cityRows, null);
        Paint(_countyRows, null);
        UpdateHint();

        // 台湾省这类没有下级的省，点一下就是最终结果
        if (!node.HasChildren)
            Apply(node);
    }

    private void SelectCity(RegionNode node) {
        _city = node;
        _leaf = node;

        Paint(_cityRows, _city);
        FillColumn(CountyPanel, _countyRows, node.Children, SelectCounty, "该市无下级");
        Paint(_countyRows, null);
        UpdateHint();

        // 东莞、中山这类不设区的地级市，点到市就是最终结果
        if (!node.HasChildren)
            Apply(node);
    }

    private void SelectCounty(RegionNode node) {
        _leaf = node;

        Paint(_countyRows, _leaf);
        UpdateHint();
        Apply(node);
    }

    private void Apply(RegionNode leaf) {
        if (_applied || _province is null)
            return;

        _applied = true;

        // 「杭州市 · 浙江省」；直辖市的区只挂省名；地级市自己就是末级时也只挂省名
        string region =
            ReferenceEquals(leaf, _province) ? "" :
            _city is null || ReferenceEquals(_city, leaf) ? _province.Name :
            $"{_city.Name} · {_province.Name}";

        var place = new WeatherPlace {
            Name = leaf.Name,
            Region = region,
            Country = "中国",
            Code = leaf.Code,
            Lat = leaf.Lat,
            Lon = leaf.Lon,
            HasCoords = true
        };

        WidgetDataStore.Weather = place;
        WeatherView.NotifyLocationChanged();

        DialogResult = true;
    }

    // ===== 列表渲染 =====

    private void FillColumn(Panel panel, List<(RegionNode Node, Border Row)> rows,
        IReadOnlyList<RegionNode> nodes, Action<RegionNode> pick, string emptyHint) {

        panel.Children.Clear();
        rows.Clear();

        if (nodes.Count == 0) {
            panel.Children.Add(new TextBlock {
                Text = emptyHint,
                FontSize = 11,
                Margin = new Thickness(9, 9, 9, 9),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextFaint")
            });
            return;
        }

        foreach (RegionNode node in nodes) {
            Border row = BuildRow(node, pick);
            rows.Add((node, row));
            panel.Children.Add(row);
        }
    }

    private Border BuildRow(RegionNode node, Action<RegionNode> pick) {
        var row = new Border {
            Child = new TextBlock {
                Text = node.Name,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("TextPrimary"),
                TextTrimming = TextTrimming.CharacterEllipsis
            },
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(2, 1, 2, 1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            // 自治县这类名字很长，截断后至少悬停能看到全名
            ToolTip = node.Name
        };

        row.MouseEnter += (_, _) => {
            if (!IsSelected(node))
                row.Background = (Brush)FindResource("SurfaceActive");
        };

        row.MouseLeave += (_, _) => {
            if (!IsSelected(node))
                row.Background = Brushes.Transparent;
        };

        row.MouseLeftButtonUp += (_, e) => {
            pick(node);
            e.Handled = true;
        };

        // 行内按下不交给窗口去拖窗，否则点一下变成拖动、松手收不到
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;

        return row;
    }

    private bool IsSelected(RegionNode node) =>
        ReferenceEquals(node, _province) || ReferenceEquals(node, _city) || ReferenceEquals(node, _leaf);

    private void Paint(List<(RegionNode Node, Border Row)> rows, RegionNode? selected) {
        var accent = (Brush)FindResource("AccentSoft");

        foreach (var (node, row) in rows)
            row.Background = ReferenceEquals(node, selected) ? accent : Brushes.Transparent;
    }

    /// <summary>重新打开窗口时，按存档里的区划代码把三级都选好。</summary>
    private void RestoreSelection(WeatherPlace? place) {
        if (place is null || place.Code.Length == 0)
            return;

        var path = ChinaRegions.PathOf(place.Code);
        if (path is null)
            return;

        var (province, city, leaf) = path.Value;

        _province = province;
        _leaf = leaf;
        Paint(_provinceRows, _province);

        FillColumn(CityPanel, _cityRows, province.Children, SelectCity, "该省无下级");

        if (city is not null) {
            _city = city;
            FillColumn(CountyPanel, _countyRows, city.Children, SelectCounty, "该市无下级");
        }
        else {
            // 直辖市的区就落在「城市」这一列，区县列没有东西可选
            FillColumn(CountyPanel, _countyRows, Array.Empty<RegionNode>(), SelectCounty,
                ReferenceEquals(leaf, province) ? "先选城市" : "已到最末一级");
        }

        Paint(_cityRows, _city ?? _leaf);
        Paint(_countyRows, _leaf);
    }

    private void ScrollToSelection() {
        Bring(_provinceRows, _province);
        Bring(_cityRows, _city ?? _leaf);
        Bring(_countyRows, _leaf);
    }

    private static void Bring(List<(RegionNode Node, Border Row)> rows, RegionNode? node) {
        if (node is null)
            return;

        foreach (var (item, row) in rows) {
            if (!ReferenceEquals(item, node))
                continue;

            row.BringIntoView();
            return;
        }
    }

    private void UpdateHint(WeatherPlace? saved = null) {
        if (_province is null) {
            CurrentText.Text = saved is { IsEmpty: false }
                ? "当前：" + (saved.Region.Length > 0 ? $"{saved.Name} · {saved.Region}" : saved.Name)
                : "当前：未选择位置";
            return;
        }

        var parts = new List<string>(3) { _province.Name };

        if (_city is not null)
            parts.Add(_city.Name);

        if (_leaf is not null && !ReferenceEquals(_leaf, _city) && !ReferenceEquals(_leaf, _province))
            parts.Add(_leaf.Name);

        CurrentText.Text = "当前：" + string.Join(" · ", parts);
    }

    // ===== 窗口行为 =====

    /// <summary>贴在主面板上方展开；上方放不下就翻到下方，再不行贴屏幕内。</summary>
    private void PlaceNearOwner() {
        var owner = Owner;
        if (owner == null)
            return;

        var area = SystemParameters.WorkArea;

        double left = owner.Left + (owner.ActualWidth - ActualWidth) / 2;
        left = Math.Clamp(left, area.Left + 8, Math.Max(area.Left + 8, area.Right - ActualWidth - 8));

        double top = owner.Top - ActualHeight - 10;
        if (top < area.Top + 8)
            top = owner.Top + owner.ActualHeight + 10;

        if (top + ActualHeight > area.Bottom - 8)
            top = Math.Max(area.Top + 8, area.Bottom - ActualHeight - 8);

        Left = left;
        Top = top;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) {
        if (_applied)
            return;

        _applied = true;

        WidgetDataStore.Weather = null;
        WeatherView.NotifyLocationChanged();

        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // 无边框窗口自带的拖动：只处理落在空白处的按下，列表行和按钮自己会消费掉
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        try {
            DragMove();
        }
        catch (InvalidOperationException) {
            // 鼠标已经松开，忽略
        }
    }
}
