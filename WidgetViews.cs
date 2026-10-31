using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;

namespace WidgetNest;

internal readonly record struct MetricSample(double? Value, string Unit, double? Percent, string Note);

// ===== 1×1 数值类（占用率 / 温度） =====

internal sealed class MetricView : WidgetView {
    private readonly TextBlock _caption;
    private readonly TextBlock _value;
    private readonly TextBlock _hint;
    private readonly MiniBar _bar;
    private readonly Func<MetricSample> _read;

    public MetricView(string caption, string iconPath, Func<MetricSample> read, bool showBar, WidgetBox box) {
        _read = read;

        double s = box.Scale;
        double pad = Math.Min(s, 1.3);

        _caption = W.Text(caption, 8.5 * s, W.DimBrush);
        _value = W.Text("—", 15 * s * (box.Tall && !box.Wide ? 1.25 : 1), W.TextBrush,
            FontWeights.SemiBold);
        _hint = W.Text("", 7.5 * s, W.FaintBrush);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.TextAlignment = TextAlignment.Center;
        _hint.Visibility = Visibility.Collapsed;
        _bar = new MiniBar(3 * Math.Min(s, 1.5));

        var header = new StackPanel {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        header.Children.Add(W.Icon(iconPath, 9 * Math.Min(s, 1.5), W.FaintBrush));
        _caption.Margin = new Thickness(3 * s, 0, 0, 0);
        header.Children.Add(_caption);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.Children.Add(header);

        if (showBar && box.Cols > 1 && !box.Tall) {
            // 横向单行：数值靠左，进度条占满右侧
            _value.HorizontalAlignment = HorizontalAlignment.Left;
            _value.TextAlignment = TextAlignment.Left;

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            body.Children.Add(_value);

            _bar.Margin = new Thickness(9, 0, 0, 0);
            _bar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_bar, 1);
            body.Children.Add(_bar);

            Grid.SetRow(body, 1);
            grid.Children.Add(body);
        }
        else {
            _value.HorizontalAlignment = HorizontalAlignment.Center;
            _value.TextAlignment = TextAlignment.Center;

            Grid.SetRow(_value, 1);
            grid.Children.Add(_value);

            if (showBar) {
                Grid.SetRow(_bar, 2);
                grid.Children.Add(_bar);
            }
            else {
                // 没有读数时（传感器缺失 / 缺权限）在数值下面补一句说明
                Grid.SetRow(_hint, 2);
                grid.Children.Add(_hint);
            }
        }

        Root = W.Pad(grid, 6 * pad, 5 * pad, 6 * pad, 5 * pad);

        Update();
    }

    public override void Update() {
        var sample = _read();

        if (sample.Value.HasValue) {
            _value.Text = sample.Value.Value.ToString("0", CultureInfo.InvariantCulture) + sample.Unit;
            _value.Foreground = W.Severity(sample.Percent ?? sample.Value);
            _hint.Visibility = Visibility.Collapsed;
        }
        else {
            _value.Text = "—";
            _value.Foreground = W.FaintBrush;

            string reason = ShortNote(sample.Note);
            _hint.Text = reason;
            _hint.Visibility = reason.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        _bar.SetValue(sample.Percent ?? 0, W.Severity(sample.Percent));

        if (!string.IsNullOrEmpty(sample.Note))
            Root.ToolTip = sample.Note;
        else
            Root.ToolTip = null;
    }

    /// <summary>把长说明压成两三个字，塞进 1×1 的小格子里。</summary>
    private static string ShortNote(string note) {
        if (string.IsNullOrEmpty(note))
            return "";

        if (note.Contains("权限") || note.Contains("管理员"))
            return "需权限";

        if (note.Contains("没有") || note.Contains("不支持") || note.Contains("检测到"))
            return "不支持";

        if (note.Contains("传感器") || note.Contains("无热区") || note.Contains("数据"))
            return "无数据";

        return "不可用";
    }
}

// ===== 时间 =====

internal sealed class ClockView : WidgetView {
    private static readonly string[] Weekdays = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

    private readonly TextBlock _time;
    private readonly TextBlock _weekday;
    private readonly TextBlock _date;
    private readonly MiniBar _seconds;

    public ClockView(WidgetBox box) {
        bool compact = box.Narrow && !box.Tall;
        bool wide = box.Wide;
        double s = box.Scale;

        // 1×1 用 14 号，横排 1×6 用 24 号，其余按行高放大
        _time = W.Text("00:00", compact ? 14 : (wide && !box.Tall ? 24 : 23 * s),
            W.TextBrush, FontWeights.SemiBold);
        _time.TextTrimming = TextTrimming.None;
        _weekday = W.Text("", compact ? 8.5 : (wide ? 10.5 : 10 * s), W.DimBrush);
        _date = W.Text("", wide ? 9.5 : 8.5 * s, W.FaintBrush);
        _seconds = new MiniBar(compact ? 2 : 2.5);

        if (compact) {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _time.HorizontalAlignment = HorizontalAlignment.Center;
            _weekday.HorizontalAlignment = HorizontalAlignment.Center;
            _weekday.Margin = new Thickness(0, 1, 0, 0);

            stack.Children.Add(_time);
            stack.Children.Add(_weekday);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(stack);

            Grid.SetRow(_seconds, 1);
            grid.Children.Add(_seconds);

            Root = W.Pad(grid, 6, 5, 6, 5);
            Update();
            return;
        }

        // 2×2：时间居中，星期与日期并成一行放下面（并排会被右边的星期挤到截断）
        if (box.Tall && !wide) {
            _time.HorizontalAlignment = HorizontalAlignment.Center;
            _weekday.TextAlignment = TextAlignment.Center;
            _date.TextAlignment = TextAlignment.Center;

            var line = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            line.Children.Add(_weekday);
            _date.Margin = new Thickness(8, 0, 0, 0);
            line.Children.Add(_date);

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_time);
            line.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(line);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(stack);

            Grid.SetRow(_seconds, 1);
            grid.Children.Add(_seconds);

            Root = W.Pad(grid, 10, 8, 10, 8);
            Update();
            return;
        }

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_time);

        var right = new StackPanel {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        _weekday.HorizontalAlignment = HorizontalAlignment.Right;
        _date.HorizontalAlignment = HorizontalAlignment.Right;
        _date.Margin = new Thickness(0, wide ? 1 : 2, 0, 0);

        right.Children.Add(_weekday);
        right.Children.Add(_date);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(left);

        Grid.SetColumn(right, 1);
        body.Children.Add(right);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(body);

        Grid.SetRow(_seconds, 1);
        root.Children.Add(_seconds);

        // 横条留白多一点，小格子收紧
        double padX = wide ? 14 : 9 * Math.Min(s, 1.2);
        double padY = wide ? 7 : 6 * Math.Min(s, 1.2);

        Root = W.Pad(root, padX, padY, padX, padY);

        Update();
    }

    public override int IntervalMs => 500;

    public override void Update() {
        var now = DateTime.Now;

        _time.Text = now.ToString("HH:mm");
        _weekday.Text = Weekdays[(int)now.DayOfWeek];
        _date.Text = now.ToString("M/d");

        double percent = (now.Second + now.Millisecond / 1000.0) / 60.0 * 100;
        _seconds.SetValue(percent, W.Frozen(W.Ink(0x66)));
    }
}

// ===== 日期 =====

internal sealed class DateView : WidgetView {
    private static readonly string[] Weekdays = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

    private readonly TextBlock _day;
    private readonly TextBlock _sub;

    public DateView(WidgetBox box) {
        double s = box.Scale;

        _day = W.Text("1", 21 * s, W.TextBrush, FontWeights.SemiBold);
        _sub = W.Text("", 8.5 * s, W.DimBrush);

        if (box.Cols > 1 && !box.Tall) {
            // 横向：日号靠左，月份/星期叠在右边
            var right = new StackPanel {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _sub.TextAlignment = TextAlignment.Right;
            _sub.TextWrapping = TextWrapping.Wrap;
            right.Children.Add(_sub);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(_day);

            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            Root = W.Pad(grid, 9, 5, 9, 5);
        }
        else {
            _day.HorizontalAlignment = HorizontalAlignment.Center;
            _sub.HorizontalAlignment = HorizontalAlignment.Center;

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_day);
            stack.Children.Add(_sub);

            Root = W.Pad(stack, 4 * Math.Min(s, 1.4), 4, 4 * Math.Min(s, 1.4), 4);
        }

        Update();
    }

    public override int IntervalMs => 10_000;

    public override void Update() {
        var now = DateTime.Now;
        _day.Text = now.Day.ToString();
        _sub.Text = $"{now.Month}月 · {Weekdays[(int)now.DayOfWeek]}";
    }
}

// ===== 天气（2×2） =====

internal sealed class WeatherView : WidgetView {
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static WeatherSnapshot? _cache;
    private static long _cacheStamp;

    /// <summary>位置被改过：所有天气实例立刻换数据源，不用等心跳。</summary>
    private static event Action? LocationChanged;

    public static void NotifyLocationChanged() => LocationChanged?.Invoke();

    private readonly TextBlock _area = W.Text("位置未知", 9.5, W.DimBrush);
    private readonly TextBlock _desc = W.Text("点击选择位置", 9, W.FaintBrush);
    private readonly TextBlock _temp = W.Text("—", 26, W.TextBrush, FontWeights.SemiBold);
    private readonly TextBlock _detail = W.Text("", 8, W.FaintBrush);
    private readonly ShapePath _icon;
    private readonly Border _locationHit;
    private readonly Dispatcher _dispatcher;

    private bool _fetching;
    private bool _disposed;

    private sealed class WeatherSnapshot {
        /// <summary>这份数据对应的位置键，换位置后旧缓存直接作废。</summary>
        public string PlaceKey = "";
        public string Desc = "";
        public double Temp;
        public double Humidity;
        public double Wind;
        public bool IsDay = true;
        public bool Valid;
    }

    public WeatherView(WidgetBox box) {
        bool wide = box.Wide;
        bool compact = box.Rows == 1 && !wide && box.Cols > 1;
        double s = box.Scale;

        _area.FontSize = wide ? 11 : (compact ? 8.5 : 9.5 * s);
        _desc.FontSize = wide ? 10 : (compact ? 7.5 : 9 * s);
        _temp.FontSize = wide ? 22 : (compact ? 16 : 26 * s);
        _detail.FontSize = 8 * s;

        _dispatcher = Dispatcher.CurrentDispatcher;
        _icon = W.Icon(WidgetFactory.WeatherIconPath("sun"), wide ? 24 : (compact ? 15 : 22 * s),
            W.FaintBrush);

        var topText = new StackPanel();
        topText.Children.Add(_area);
        _desc.Margin = new Thickness(0, 2, 0, 0);
        topText.Children.Add(_desc);

        // 位置区域本身就是选择入口：悬停高亮，点一下打开位置选择窗
        _locationHit = new Border {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(3, 1, 3, 1),
            Margin = new Thickness(-3, -1, 0, -1),
            Child = topText,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "点击选择位置"
        };

        _locationHit.MouseEnter += (_, _) => {
            if (!WidgetRuntime.EditMode)
                _locationHit.Background = W.Frozen(W.Ink(0x1A));
        };
        _locationHit.MouseLeave += (_, _) => _locationHit.Background = Brushes.Transparent;
        _locationHit.MouseLeftButtonUp += (_, e) => {
            if (WidgetRuntime.EditMode)
                return;

            _locationHit.Background = Brushes.Transparent;

            // 等这次鼠标事件走完再开窗，避免在事件处理里阻塞消息循环
            _dispatcher.BeginInvoke(new Action(OpenLocationPicker));
            e.Handled = true;
        };

        W.Interactive(_locationHit);

        if (wide) {
            // 1×6 横条：图标、温度、位置/描述、湿度风速，一行排开
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _icon.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(_icon);

            _locationHit.Margin = new Thickness(0);

            Grid.SetColumn(_temp, 1);
            _temp.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(_temp);

            Grid.SetColumn(_locationHit, 2);
            row.Children.Add(_locationHit);

            _detail.TextAlignment = TextAlignment.Right;
            _detail.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_detail, 3);
            row.Children.Add(_detail);

            Root = W.Pad(row, 14, 5, 14, 5);
        }
        else if (compact) {
            // 1×2 紧凑：温度叠在位置上靠左，图标垫在右边
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _temp.VerticalAlignment = VerticalAlignment.Center;
            _locationHit.Margin = new Thickness(0, 1, 0, 0);

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_temp);
            stack.Children.Add(_locationHit);
            body.Children.Add(stack);

            _icon.VerticalAlignment = VerticalAlignment.Center;
            _icon.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(_icon, 1);
            body.Children.Add(_icon);

            Root = W.Pad(body, 9, 4, 9, 4);
        }
        else {
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(_locationHit);

            Grid.SetColumn(_icon, 1);
            top.Children.Add(_icon);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(top);

            _temp.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(_temp, 1);
            grid.Children.Add(_temp);

            Grid.SetRow(_detail, 2);
            grid.Children.Add(_detail);

            Root = W.Pad(grid, 9 * Math.Min(s, 1.2), 7, 9 * Math.Min(s, 1.2), 7);
        }

        LocationChanged += OnLocationChanged;

        ApplyCache();

        // 没选位置就不发请求：不按 IP 猜，状态明确显示「位置未知」
        if (WidgetDataStore.Weather is { IsEmpty: false })
            _ = RefreshAsync();
    }

    public override int IntervalMs => 60_000;

    public override void Update() {
        var place = WidgetDataStore.Weather;
        string key = place?.Key ?? "";

        // 位置在别处被改过（多组件、重新打开面板）：先把画面切过去
        if (!string.Equals(key, _appliedKey, StringComparison.Ordinal)) {
            ApplyCache();

            if (place is { IsEmpty: false })
                _ = RefreshAsync();

            return;
        }

        if (place is { IsEmpty: false } && Environment.TickCount64 - _cacheStamp > 30 * 60_000)
            _ = RefreshAsync();
    }

    public override void Dispose() {
        _disposed = true;
        LocationChanged -= OnLocationChanged;
    }

    private string _appliedKey = "";

    private void OnLocationChanged() {
        if (_disposed)
            return;

        _dispatcher.InvokeAsync(() => {
            if (_disposed)
                return;

            ApplyCache();

            if (WidgetDataStore.Weather is { IsEmpty: false })
                _ = RefreshAsync();
        });
    }

    private void ApplyCache() {
        var place = WidgetDataStore.Weather;
        _appliedKey = place?.Key ?? "";

        if (place is null || place.IsEmpty) {
            _area.Text = "位置未知";
            _desc.Text = "点击选择位置";
            _temp.Text = "—";
            _temp.Foreground = W.FaintBrush;
            _detail.Text = "";
            _locationHit.ToolTip = "点击选择位置";
            SetIcon("sun", false);
            return;
        }

        // 城市名可能很长，超出部分由 W.Text 的省略号接住
        _area.Text = place.Name;
        _locationHit.ToolTip = $"{place.FullLabel}\n点击更换位置";

        var snapshot = _cache;
        bool samePlace = snapshot != null &&
                         string.Equals(snapshot.PlaceKey, place.Key, StringComparison.Ordinal);

        if (!samePlace || snapshot is not { Valid: true }) {
            _desc.Text = samePlace ? "天气不可用" : "加载中…";
            _temp.Text = "—";
            _temp.Foreground = W.FaintBrush;
            _detail.Text = "";
            SetIcon("sun", false);
            return;
        }

        _desc.Text = snapshot.Desc;
        _temp.Text = $"{snapshot.Temp:0}°";
        _temp.Foreground = W.TextBrush;
        _detail.Text = $"湿度 {snapshot.Humidity:0}% · 风 {snapshot.Wind:0} km/h";

        SetIcon(WidgetFactory.WeatherIconKind(snapshot.Desc, snapshot.IsDay), true);
    }

    private void SetIcon(string kind, bool colored) {
        _icon.Data = Geometry.Parse(WidgetFactory.WeatherIconPath(kind));

        if (!colored) {
            _icon.Fill = W.FaintBrush;
            return;
        }

        _icon.Fill = W.Frozen(kind switch {
            "sun" => Color.FromArgb(0xE6, 0xFF, 0xD5, 0x6B),
            "rain" => Color.FromArgb(0xE6, 0x8A, 0xC6, 0xFF),
            "snow" => Color.FromArgb(0xE6, 0xCF, 0xE9, 0xFF),
            _ => Color.FromArgb(0xE6, 0xD8, 0xDD, 0xE5)
        });
    }

    private void OpenLocationPicker() {
        var dialog = new LocationWindow(WidgetDataStore.Weather);

        var owner = Window.GetWindow(Root);
        if (owner != null)
            dialog.Owner = owner;

        dialog.ShowDialog();
    }

    private async System.Threading.Tasks.Task RefreshAsync() {
        if (_fetching || _disposed)
            return;

        var place = WidgetDataStore.Weather;
        if (place is null || place.IsEmpty)
            return;

        string key = place.Key;
        _fetching = true;

        try {
            // 有坐标就按坐标查：全球任意城市都能定位，同名城市也不会查错；
            // 只有名字的老存档退回按名称查
            string url = "https://wttr.in/" + Uri.EscapeDataString(place.QueryTarget) + "?format=j1";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "curl/8.4.0");

            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            var snapshot = Parse(json);
            snapshot.PlaceKey = key;

            // 请求期间用户又换了位置：这份结果已经过期，别覆盖新位置的数据
            if (!string.Equals(key, WidgetDataStore.Weather?.Key, StringComparison.Ordinal))
                return;

            _cache = snapshot;
            _cacheStamp = Environment.TickCount64;

            if (!_disposed)
                await _dispatcher.InvokeAsync(ApplyCache);
        }
        catch {
            if (!string.Equals(key, WidgetDataStore.Weather?.Key, StringComparison.Ordinal))
                return;

            // 只记录失败状态，上一份同位置的好数据保留着
            if (_cache == null || !string.Equals(_cache.PlaceKey, key, StringComparison.Ordinal)) {
                _cache = new WeatherSnapshot { PlaceKey = key, Valid = false };
                _cacheStamp = Environment.TickCount64;
            }

            if (!_disposed)
                await _dispatcher.InvokeAsync(ApplyCache);
        }
        finally {
            _fetching = false;

            // 请求还没回来位置就变了：立刻按新位置重拉一次
            if (!_disposed && WidgetDataStore.Weather is { IsEmpty: false } next &&
                !string.Equals(key, next.Key, StringComparison.Ordinal)) {
                _ = RefreshAsync();
            }
        }
    }

    private static WeatherSnapshot Parse(string json) {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var snapshot = new WeatherSnapshot { Valid = true };

        if (root.TryGetProperty("current_condition", out var conditions) &&
            conditions.GetArrayLength() > 0) {
            var current = conditions[0];

            snapshot.Temp = Number(current, "temp_C");
            snapshot.Humidity = Number(current, "humidity");
            snapshot.Wind = Number(current, "windspeedKmph");

            if (current.TryGetProperty("weatherDesc", out var descs) && descs.GetArrayLength() > 0)
                snapshot.Desc = WidgetFactory.LocalizeWeather(
                    descs[0].GetProperty("value").GetString() ?? "");

            if (current.TryGetProperty("isdaytime", out var day))
                snapshot.IsDay = day.GetString() != "0";
        }

        return snapshot;
    }

    private static double Number(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var value))
            return 0;

        return double.TryParse(value.GetString(), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double result) ? result : 0;
    }
}

// ===== 电量（1×2） =====

internal sealed class BatteryView : WidgetView {
    private readonly TextBlock _percent = W.Text("—", 13, W.TextBrush, FontWeights.SemiBold);
    private readonly TextBlock _sub = W.Text("", 8.5, W.FaintBrush);
    private readonly MiniBar _bar = new(5);

    public BatteryView(WidgetBox box) {
        if (box.Narrow && !box.Tall) {
            // 1×1：只留百分比和进度条
            _percent.FontSize = 15;
            _percent.HorizontalAlignment = HorizontalAlignment.Center;
            _percent.TextAlignment = TextAlignment.Center;
            _bar.Margin = new Thickness(0, 3, 0, 0);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(_percent);

            Grid.SetRow(_bar, 1);
            grid.Children.Add(_bar);

            Root = W.Pad(grid, 6, 5, 6, 5);
            Update();
            return;
        }

        double s = box.Scale;
        _percent.FontSize = 13 * s;
        _sub.FontSize = 8.5 * s;

        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(W.Icon(WidgetCatalog.Find("battery")!.IconPath, 10 * Math.Min(s, 1.4),
            W.FaintBrush));

        var caption = W.Text("电量", 8.5 * s, W.DimBrush);
        caption.Margin = new Thickness(4, 0, 0, 0);
        label.Children.Add(caption);

        if (box.Wide) {
            // 1×6 横条：标签、进度条、百分比、状态一行排开
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(label);

            _bar.Margin = new Thickness(12, 0, 12, 0);
            _bar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_bar, 1);
            row.Children.Add(_bar);

            _percent.Margin = new Thickness(0, 0, 10, 0);
            _percent.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_percent, 2);
            row.Children.Add(_percent);

            _sub.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_sub, 3);
            row.Children.Add(_sub);

            Root = W.Pad(row, 14, 5, 14, 5);
            Update();
            return;
        }

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        header.Children.Add(label);

        _percent.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_percent, 1);
        header.Children.Add(_percent);

        var grid2 = new Grid();
        grid2.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid2.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid2.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid2.Children.Add(header);

        Grid.SetRow(_bar, 1);
        _bar.Margin = new Thickness(0, 4, 0, 0);
        grid2.Children.Add(_bar);

        Grid.SetRow(_sub, 2);
        _sub.VerticalAlignment = VerticalAlignment.Bottom;
        _sub.Margin = new Thickness(0, 4, 0, 0);
        grid2.Children.Add(_sub);

        Root = W.Pad(grid2, 9, 6, 9, 6);
        Update();
    }

    public override int IntervalMs => 15_000;

    public override void Update() {
        var battery = SystemMetrics.Battery();

        if (battery == null) {
            _percent.Text = "—";
            _percent.Foreground = W.FaintBrush;
            _sub.Text = "无电池";
            _bar.SetValue(0);
            return;
        }

        int percent = battery.Value.Percent;
        _percent.Text = $"{percent}%";
        _percent.Foreground = percent <= 20 ? W.DangerBrush : W.TextBrush;

        _bar.SetValue(percent, percent <= 20 ? W.DangerBrush : W.AccentBrush);
        _sub.Text = battery.Value.Charging ? "正在充电" : "使用电池中";
    }
}

// ===== 状态类（WiFi / 蓝牙，1×1） =====

internal sealed class StatusView : WidgetView {
    private readonly Func<(bool On, string Title, string Sub)> _read;
    private readonly TextBlock _title = W.Text("—", 10.5, W.TextBrush, FontWeights.SemiBold);
    private readonly TextBlock _sub = W.Text("", 8, W.FaintBrush);
    private readonly ShapePath _icon;

    public StatusView(string iconPath, string caption, Func<(bool, string, string)> read, WidgetBox box,
        int interval = 5000) {
        _read = read;
        Interval = interval;

        double s = box.Scale;
        _icon = W.Icon(iconPath, 10 * Math.Min(s, 1.5), W.FaintBrush);
        _title.FontSize = 10.5 * s;
        _sub.FontSize = 8 * s;

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(_icon);
        var label = W.Text(caption, 8.5 * s, W.DimBrush);
        label.Margin = new Thickness(4, 0, 0, 0);
        header.Children.Add(label);

        if (box.Cols > 1 && !box.Tall) {
            // 1×2 横向：左边图标 + 名称，右边状态
            var right = new StackPanel {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _title.HorizontalAlignment = HorizontalAlignment.Right;
            _sub.HorizontalAlignment = HorizontalAlignment.Right;
            _sub.Margin = new Thickness(0, 1, 0, 0);

            right.Children.Add(_title);
            right.Children.Add(_sub);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(header);

            Grid.SetColumn(right, 1);
            row.Children.Add(right);

            Root = W.Pad(row, 8, 5, 8, 5);
            Update();
            return;
        }

        if (box.Tall) {
            // 2×2：三行整体居中，别把状态行甩到卡片底边
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            _title.Margin = new Thickness(0, 4 * s, 0, 0);
            _sub.Margin = new Thickness(0, 2, 0, 0);

            stack.Children.Add(header);
            stack.Children.Add(_title);
            stack.Children.Add(_sub);

            Root = W.Pad(stack, 8 * Math.Min(s, 1.2), 6, 8 * Math.Min(s, 1.2), 6);
            Update();
            return;
        }

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(header);

        Grid.SetRow(_title, 1);
        _title.Margin = new Thickness(0, 3 * s, 0, 0);
        grid.Children.Add(_title);

        Grid.SetRow(_sub, 2);
        _sub.VerticalAlignment = VerticalAlignment.Bottom;
        grid.Children.Add(_sub);

        Root = W.Pad(grid, 6 * Math.Min(s, 1.4), 5 * Math.Min(s, 1.4),
            6 * Math.Min(s, 1.4), 5 * Math.Min(s, 1.4));

        Update();
    }

    private int Interval { get; }

    public override int IntervalMs => Interval;

    public override void Update() {
        var (on, title, sub) = _read();

        _title.Text = title;
        _title.Foreground = on ? W.TextBrush : W.FaintBrush;
        _sub.Text = sub;
        _icon.Fill = on ? W.AccentBrush : W.FaintBrush;
    }
}

// ===== 动作类（1×1 按钮） =====

internal sealed class ActionView : WidgetView {
    private readonly string _id;
    private readonly string _label;
    private readonly ShapePath _icon;
    private readonly TextBlock _text;
    private readonly Border _hover;

    public ActionView(string id, string label, string iconPath, Action<ActionView> click, WidgetBox box) {
        _id = id;
        _label = label;

        double s = box.Scale;
        bool horizontal = box.Cols > 1 && !box.Tall;

        _text = W.Text(label, horizontal ? 10 : 8.5 * s, W.DimBrush);
        _icon = W.Icon(iconPath, horizontal ? 15 : 17 * s, W.Frozen(W.Ink(0xD8)));

        StackPanel stack;

        if (horizontal) {
            // 1×2 横向：图标在左、文字在右
            stack = new StackPanel {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            stack.Children.Add(_icon);
            _text.Margin = new Thickness(7, 0, 0, 0);
            stack.Children.Add(_text);
        }
        else {
            _text.HorizontalAlignment = HorizontalAlignment.Center;
            _text.TextAlignment = TextAlignment.Center;
            _icon.HorizontalAlignment = HorizontalAlignment.Center;

            stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_icon);
            _text.Margin = new Thickness(0, 4 * s, 0, 0);
            stack.Children.Add(_text);
        }

        _hover = new Border {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(7),
            Child = W.Pad(stack, 4, 4, 4, 4),
            Cursor = Cursors.Hand
        };

        _hover.MouseEnter += (_, _) => _hover.Background = W.Frozen(W.Ink(0x1F));
        _hover.MouseLeave += (_, _) => _hover.Background = Brushes.Transparent;
        _hover.MouseLeftButtonUp += (_, e) => {
            if (WidgetRuntime.EditMode)
                return;

            click(this);
            e.Handled = true;
        };

        W.Interactive(_hover);

        var root = new Grid();
        root.Children.Add(_hover);
        Root = root;

        Update();
    }

    public override int IntervalMs => 3000;

    public override void Update() {
        if (_id != "mute")
            return;

        bool muted = SystemMetrics.IsMuted();
        _icon.Data = Geometry.Parse(muted
            ? WidgetCatalog.Find("mute")!.IconPath
            : WidgetCatalog.Find("volume")!.IconPath);
        _icon.Fill = muted
            ? W.Frozen(Color.FromArgb(0xE6, 0xFF, 0x8B, 0x7B))
            : W.Frozen(W.Ink(0xD8));
        _text.Text = muted ? "已静音" : "静音";
    }
}

// ===== 滑块类（1×6） =====

internal sealed class SliderView : WidgetView {
    private readonly string _id;
    private readonly SlideBar _bar = new();
    private readonly TextBlock _value = W.Text("0%", 10.5, W.TextBrush, FontWeights.SemiBold);

    private long _lastApply;

    public SliderView(string id, string label, string iconPath, WidgetBox box) {
        _id = id;

        bool narrow = box.Cols <= 3;
        double s = box.Scale;

        _value.FontSize = (narrow ? 9.5 : 10.5) * s;
        _value.HorizontalAlignment = HorizontalAlignment.Right;
        _value.Width = (narrow ? 30 : 34) * s;
        _value.TextAlignment = TextAlignment.Right;

        var head = new StackPanel {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Width = narrow ? 15 : 58
        };

        head.Children.Add(W.Icon(iconPath, (narrow ? 12 : 13) * Math.Min(s, 1.4),
            W.Frozen(W.Ink(0xCC))));

        if (!narrow) {
            var caption = W.Text(label, 11 * s, W.DimBrush);
            caption.Margin = new Thickness(6, 0, 0, 0);
            head.Children.Add(caption);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(head);

        _bar.Margin = narrow ? new Thickness(7, 0, 7, 0) : new Thickness(4, 0, 10, 0);
        _bar.Height = 20 * Math.Min(s, 1.2);
        Grid.SetColumn(_bar, 1);
        grid.Children.Add(_bar);

        Grid.SetColumn(_value, 2);
        grid.Children.Add(_value);

        _bar.ValueChanged += OnBarChanged;

        Root = W.Pad(grid, narrow ? 8 : 10, 6 * Math.Min(s, 1.2), narrow ? 8 : 10,
            6 * Math.Min(s, 1.2));

        Update(force: true);
    }

    public override int IntervalMs => 1000;

    private void OnBarChanged(double percent) {
        _value.Text = $"{percent:0}%";

        // WMI 写亮度偏慢，拖动时按 100ms 节流
        long now = Environment.TickCount64;
        if (now - _lastApply < 100 && percent is > 0 and < 100)
            return;

        _lastApply = now;

        if (_id == "volume") {
            SystemMetrics.SetVolume(percent);
        }
        else {
            // WMI 写亮度一次要几十毫秒，放后台线程，拖动才不卡
            int target = (int)Math.Round(percent);
            _ = System.Threading.Tasks.Task.Run(() => SystemMetrics.SetBrightness(target));
        }
    }

    public override void Update() => Update(force: false);

    private void Update(bool force) {
        // 用户正在拖动时别抢方向盘
        if (!force && Environment.TickCount64 - _bar.LastUserInput < 1500)
            return;

        double current = _id == "volume"
            ? SystemMetrics.Volume()
            : SystemMetrics.Brightness() ?? 0;

        if (_id == "brightness" && SystemMetrics.Brightness() == null) {
            _value.Text = "—";
            _bar.IsEnabled = false;
            Root.ToolTip = "此显示器不支持亮度调节";
            return;
        }

        _bar.SetValue(current);
        _value.Text = $"{current:0}%";
    }
}

// ===== 日程条（1×6） =====

internal sealed class ScheduleBarView : WidgetView {
    private readonly WrapPanel _chips = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly int _rows;

    public ScheduleBarView(WidgetBox box) {
        _rows = box.Rows;
        double s = Math.Min(box.Scale, 1.3);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 多行时内容靠上排，单行条保持垂直居中
        var head = new StackPanel {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = _rows > 1 ? VerticalAlignment.Top : VerticalAlignment.Center,
            Width = 52
        };

        _chips.VerticalAlignment = _rows > 1 ? VerticalAlignment.Top : VerticalAlignment.Center;

        head.Children.Add(W.Icon(WidgetCatalog.Find("schedule")!.IconPath, 13, W.FaintBrush));
        var caption = W.Text("日程", 10.5 * s, W.DimBrush);
        caption.Margin = new Thickness(6, 0, 0, 0);
        head.Children.Add(caption);

        grid.Children.Add(head);
        Grid.SetColumn(_chips, 1);
        grid.Children.Add(_chips);

        Root = W.Pad(grid, 10, 6 * s, 10, 6 * s);
        Update();
    }

    public override int IntervalMs => 15_000;

    public override void Update() {
        _chips.Children.Clear();

        var events = WidgetDataStore.Upcoming(3 * _rows);

        if (events.Count == 0) {
            _chips.Children.Add(W.Chip(W.Text("暂无日程", 9.5, W.FaintBrush)));
            return;
        }

        foreach (var item in events) {
            string date = item.Date.Length >= 10 ? item.Date[5..] : item.Date;
            string time = string.IsNullOrWhiteSpace(item.Time) ? "" : " " + item.Time;
            string text = $"{date}{time} · {item.Title}";

            var label = W.Text(text, 9.5, W.DimBrush);
            label.MaxWidth = _rows > 1 ? 130 : 92;

            _chips.Children.Add(W.Chip(label));
        }
    }
}

// ===== 待办条（1×6） =====

internal sealed class TodoBarView : WidgetView {
    private readonly WrapPanel _chips = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly int _rows;

    public TodoBarView(WidgetBox box) {
        _rows = box.Rows;
        double s = Math.Min(box.Scale, 1.3);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 多行时内容靠上排，单行条保持垂直居中
        var head = new StackPanel {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = _rows > 1 ? VerticalAlignment.Top : VerticalAlignment.Center,
            Width = 52
        };

        _chips.VerticalAlignment = _rows > 1 ? VerticalAlignment.Top : VerticalAlignment.Center;

        head.Children.Add(W.Icon(WidgetCatalog.Find("todo-bar")!.IconPath, 13, W.FaintBrush));
        var caption = W.Text("待办", 10.5 * s, W.DimBrush);
        caption.Margin = new Thickness(6, 0, 0, 0);
        head.Children.Add(caption);

        grid.Children.Add(head);
        Grid.SetColumn(_chips, 1);
        grid.Children.Add(_chips);

        Root = W.Pad(grid, 10, 6, 10, 6);
        Update();
    }

    public override int IntervalMs => 2000;

    public override void Update() {
        _chips.Children.Clear();

        var todos = WidgetDataStore.UndoneTodos(3 * _rows);

        if (todos.Count == 0) {
            _chips.Children.Add(W.Chip(W.Text("没有未完成的待办", 9.5, W.FaintBrush)));
            return;
        }

        foreach (var todo in todos) {
            var circle = new Border {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1.2),
                BorderBrush = W.FaintBrush,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(circle);

            var label = W.Text(todo.Text, 9.5, W.DimBrush);
            label.MaxWidth = _rows > 1 ? 110 : 84;
            row.Children.Add(label);

            var chip = W.Chip(row);
            W.Interactive(chip);
            chip.Cursor = Cursors.Hand;
            chip.ToolTip = "点击标记完成";
            chip.MouseLeftButtonUp += (_, e) => {
                if (WidgetRuntime.EditMode)
                    return;

                WidgetDataStore.ToggleTodo(todo.Id);
                Update();
                e.Handled = true;
            };

            _chips.Children.Add(chip);
        }
    }
}

// ===== 最近文件（1×6） =====

internal sealed class RecentFilesView : WidgetView {
    private readonly WrapPanel _chips = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly int _rows;

    public RecentFilesView(WidgetBox box) {
        _rows = box.Rows;
        double s = Math.Min(box.Scale, 1.3);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 多行时内容靠上排，单行条保持垂直居中
        var head = new StackPanel {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = _rows > 1 ? VerticalAlignment.Top : VerticalAlignment.Center,
            Width = 52
        };

        _chips.VerticalAlignment = _rows > 1 ? VerticalAlignment.Top : VerticalAlignment.Center;

        head.Children.Add(W.Icon(WidgetCatalog.Find("recent")!.IconPath, 13, W.FaintBrush));
        var caption = W.Text("最近", 10.5 * s, W.DimBrush);
        caption.Margin = new Thickness(6, 0, 0, 0);
        head.Children.Add(caption);

        grid.Children.Add(head);
        Grid.SetColumn(_chips, 1);
        grid.Children.Add(_chips);

        Root = W.Pad(grid, 10, 6 * s, 10, 6 * s);
        Update();
    }

    public override int IntervalMs => 8000;

    public override void Update() {
        _chips.Children.Clear();

        var files = SystemMetrics.RecentFiles().Take(4 * _rows).ToList();

        if (files.Count == 0) {
            _chips.Children.Add(W.Chip(W.Text("最近没有打开文件", 9.5, W.FaintBrush)));
            return;
        }

        foreach (var file in files) {
            var label = W.Text(file.Name, 9.5, W.DimBrush);
            label.MaxWidth = _rows > 1 ? 96 : 70;

            var chip = W.Chip(label);
            W.Interactive(chip);
            chip.Cursor = Cursors.Hand;
            chip.ToolTip = file.Name;

            string path = file.Path;
            chip.MouseLeftButtonUp += (_, e) => {
                if (WidgetRuntime.EditMode)
                    return;

                try {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
                catch {
                    // 文件被移动/删除时忽略
                }

                e.Handled = true;
            };

            _chips.Children.Add(chip);
        }
    }
}

// ===== 工厂 =====

internal static class WidgetFactory {
    /// <summary>
    /// 按组件家族 + 实际占格造视图：同一个组件在不同尺寸下走不同版式。
    /// </summary>
    public static WidgetView? Create(WidgetDef def, WidgetBox box) {
        // 用户自己开发的组件：按清单里的布局现画，不走下面的内置分支
        if (def.IsUserWidget) {
            var userWidget = WidgetExtensions.FindByFamily(def.FamilyKey);

            return userWidget == null ? null : new UserWidgetView(userWidget, box);
        }

        switch (def.FamilyKey) {
            case "clock":
                return new ClockView(box);

            case "date":
                return new DateView(box);

            case "weather":
                return new WeatherView(box);

            case "cpu-usage":
                return new MetricView("CPU", def.IconPath,
                    () => new MetricSample(SystemMetrics.CpuUsage(), "%", SystemMetrics.CpuUsage(), ""), true, box);

            case "mem-usage":
                return new MetricView("内存", def.IconPath, () => {
                    var (used, total, percent) = SystemMetrics.Memory();
                    return new MetricSample(percent, "%", percent, $"{used:0.0} / {total:0.0} GB");
                }, true, box);

            case "disk-usage":
                return new MetricView("硬盘", def.IconPath, () => {
                    var (used, total, percent) = SystemMetrics.Disk();
                    return new MetricSample(percent, "%", percent, $"{used:0} / {total:0} GB");
                }, true, box);

            case "gpu-usage":
                return new MetricView("GPU", def.IconPath, () => {
                    double? usage = SystemMetrics.GpuUsage();
                    return new MetricSample(usage, "%", usage,
                        usage == null ? "未检测到 NVIDIA 显卡" : SystemMetrics.GpuName());
                }, true, box);

            case "cpu-temp":
                return new MetricView("CPU", def.IconPath, () => {
                    double? temp = SystemMetrics.CpuTemperature(out string note);
                    return new MetricSample(temp, "°", null, note);
                }, false, box);

            case "gpu-temp":
                return new MetricView("GPU", def.IconPath, () => {
                    double? temp = SystemMetrics.GpuTemperature(out string note);
                    return new MetricSample(temp, "°", null, note);
                }, false, box);

            case "disk-temp":
                return new MetricView("硬盘", def.IconPath, () => {
                    double? temp = SystemMetrics.DiskTemperature(out string note);
                    return new MetricSample(temp, "°", null, note);
                }, false, box);

            case "mem-temp":
                return new MetricView("内存", def.IconPath,
                    () => new MetricSample(null, "°", null, "内存条没有温度传感器"), false, box);

            case "battery":
                return new BatteryView(box);

            case "wifi":
                return new StatusView(def.IconPath, "WiFi", () => {
                    var (connected, name) = SystemMetrics.Wifi();
                    return (connected, connected ? "已连接" : "未连接", name);
                }, box);

            case "bluetooth":
                return new StatusView(def.IconPath, "蓝牙", () => {
                    var (available, paired) = SystemMetrics.Bluetooth();
                    return available
                        ? (true, "已开启", $"已配对 {paired} 台")
                        : (false, "未检测到", "无蓝牙适配器");
                }, box, 10_000);

            case "volume":
                return new SliderView("volume", "音量", def.IconPath, box);

            case "brightness":
                return new SliderView("brightness", "亮度", def.IconPath, box);

            case "mute":
                return new ActionView("mute", "静音", WidgetCatalog.Find("volume")!.IconPath,
                    _ => SystemMetrics.ToggleMute(), box);

            case "lock":
                return new ActionView("lock", "锁定", def.IconPath, _ => NativeActions.LockWorkstation(), box);

            case "keyboard":
                return new ActionView("keyboard", "键盘", def.IconPath, _ => NativeActions.OpenTouchKeyboard(), box);

            case "screenshot":
                return new ActionView("screenshot", "截图", def.IconPath, _ => NativeActions.StartScreenshot(), box);

            // 整页 / 半页 / 条状：同一家族按行数换视图
            case "schedule":
                return box.Rows >= 3 ? new CalendarBoardView(box) : new ScheduleBarView(box);

            case "todo":
                return box.Rows >= 3 ? new TodoBoardView(box) : new TodoBarView(box);

            case "recent":
                return new RecentFilesView(box);
        }

        return null;
    }

    // ===== 天气图标 =====

    private const string SunPath =
        "M12,7 C9.24,7 7,9.24 7,12 C7,14.76 9.24,17 12,17 C14.76,17 17,14.76 17,12 C17,9.24 14.76,7 12,7 Z M12,0 L12,3 M12,21 L12,24 M0,12 L3,12 M21,12 L24,12 M3.5,3.5 L5.6,5.6 M18.4,18.4 L20.5,20.5 M20.5,3.5 L18.4,5.6 M5.6,18.4 L3.5,20.5";

    private const string CloudPath =
        "M19.35,10.04 C18.67,6.59 15.64,4 12,4 C9.11,4 6.6,5.64 5.35,8.04 C2.34,8.36 0,10.9 0,14 C0,17.31 2.69,20 6,20 L19,20 C21.76,20 24,17.76 24,15 C24,12.36 21.95,10.22 19.35,10.04 Z";

    private const string RainPath =
        "M19.35,10.04 C18.67,6.59 15.64,4 12,4 C9.11,4 6.6,5.64 5.35,8.04 C2.34,8.36 0,10.9 0,14 C0,17.31 2.69,20 6,20 L19,20 C21.76,20 24,17.76 24,15 C24,12.36 21.95,10.22 19.35,10.04 Z M8,22 L6,22 L7,18 L9,18 Z M13,22 L11,22 L12,18 L14,18 Z M18,22 L16,22 L17,18 L19,18 Z";

    private const string SnowPath =
        "M19.35,10.04 C18.67,6.59 15.64,4 12,4 C9.11,4 6.6,5.64 5.35,8.04 C2.34,8.36 0,10.9 0,14 C0,17.31 2.69,20 6,20 L19,20 C21.76,20 24,17.76 24,15 C24,12.36 21.95,10.22 19.35,10.04 Z";

    private const string MoonPath =
        "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M12,20 C7.59,20 4,16.41 4,12 C4,7.59 7.59,4 12,4 C16.41,4 20,7.59 20,12 C20,16.41 16.41,20 12,20 Z M13,7 L11,7 L11,11 L9,11 L12,15 L15,11 L13,11 Z";

    public static string WeatherIconPath(string kind) => kind switch {
        "rain" => RainPath,
        "snow" => SnowPath,
        "sun" => SunPath,
        "moon" => MoonPath,
        _ => CloudPath
    };

    public static string WeatherIconKind(string description, bool isDay) {
        string text = description ?? "";

        if (text.Contains("雨") || text.Contains("rain", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("shower", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("drizzle", StringComparison.OrdinalIgnoreCase))
            return "rain";

        if (text.Contains("雪") || text.Contains("snow", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("sleet", StringComparison.OrdinalIgnoreCase))
            return "snow";

        if (text.Contains("晴") || text.Contains("sun", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("clear", StringComparison.OrdinalIgnoreCase))
            return isDay ? "sun" : "moon";

        return "cloud";
    }

    /// <summary>wttr.in 返回英文描述，转成中文短句。</summary>
    public static string LocalizeWeather(string description) {
        string text = (description ?? "").Trim();
        string lower = text.ToLowerInvariant();

        if (lower.Contains("thunder")) return "雷阵雨";
        if (lower.Contains("heavy rain")) return "大雨";
        if (lower.Contains("light rain")) return "小雨";
        if (lower.Contains("rain") || lower.Contains("drizzle")) return "雨";
        if (lower.Contains("heavy snow")) return "大雪";
        if (lower.Contains("snow") || lower.Contains("sleet")) return "雪";
        if (lower.Contains("haze")) return "霾";
        if (lower.Contains("smoke")) return "烟";
        if (lower.Contains("mist") || lower.Contains("fog")) return "雾";
        if (lower.Contains("overcast")) return "阴";
        if (lower.Contains("partly cloudy")) return "多云";
        if (lower.Contains("cloud")) return "多云";
        if (lower.Contains("clear") || lower.Contains("sunny")) return "晴";

        return text.Length == 0 ? "未知" : text;
    }
}

/// <summary>系统级动作（锁屏 / 键盘 / 截图）。</summary>
internal static class NativeActions {
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    public static void LockWorkstation() {
        try {
            LockWorkStation();
        }
        catch {
            // 忽略
        }
    }

    public static void OpenTouchKeyboard() {
        try {
            string tabTip = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                @"microsoft shared\ink\TabTip.exe");

            if (File.Exists(tabTip)) {
                Process.Start(new ProcessStartInfo { FileName = tabTip, UseShellExecute = true });
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = "osk.exe", UseShellExecute = true });
        }
        catch {
            MessageBox.Show("无法打开触摸键盘。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static void StartScreenshot() {
        try {
            // Win10 1809+ 的系统截图浮层
            Process.Start(new ProcessStartInfo { FileName = "ms-screenclip:", UseShellExecute = true });
        }
        catch {
            try {
                Process.Start(new ProcessStartInfo { FileName = "SnippingTool.exe", UseShellExecute = true });
            }
            catch {
                MessageBox.Show("无法调用系统截图工具。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
