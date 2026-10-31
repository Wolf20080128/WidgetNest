using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;

namespace WidgetNest;

/// <summary>
/// 小组件实际占用的格数（每页 3 行 × 6 列）。视图据此换版式：
/// 单列走竖排、单行走横排、两行以上放字号、整宽走横条。
/// </summary>
internal readonly record struct WidgetBox(int Rows, int Cols) {
    /// <summary>只占一列：标题在上、数值在下的竖排。</summary>
    public bool Narrow => Cols == 1;

    /// <summary>占三列以上：一行里横着排。</summary>
    public bool Wide => Cols >= 3;

    /// <summary>两行以上：高度够，字号可以放大。</summary>
    public bool Tall => Rows >= 2;

    /// <summary>占格数，整页/半页的判断用它。</summary>
    public int Area => Rows * Cols;

    /// <summary>字号倍率：1 行为基准，2 行 1.35，3 行 1.7。</summary>
    public double Scale => Rows switch {
        >= 3 => 1.7,
        2 => 1.35,
        _ => 1
    };
}

/// <summary>
/// 小组件视图基类：负责自己的刷新节奏，刷新异常不影响整个面板。
/// </summary>
internal abstract class WidgetView {
    public FrameworkElement Root { get; protected set; } = null!;

    /// <summary>刷新间隔（毫秒），重传感器类组件可以拉长。</summary>
    public virtual int IntervalMs => 1000;

    public bool IsActive { get; set; }

    private long _lastTick;

    public void Tick(long now) {
        if (!IsActive)
            return;

        if (now - _lastTick < IntervalMs)
            return;

        _lastTick = now;

        try {
            Update();
        }
        catch {
            // 单个组件出错不该拖垮面板
        }
    }

    public abstract void Update();

    public virtual void Dispose() { }
}

/// <summary>
/// 组件刷新调度：一个 400ms 的心跳，按各组件自己的间隔分发。
/// </summary>
internal static class WidgetRuntime {
    private static readonly DispatcherTimer Heartbeat = new() {
        Interval = TimeSpan.FromMilliseconds(400)
    };

    private static readonly System.Collections.Generic.List<WidgetView> Views = new();

    /// <summary>编辑模式下动作类组件不响应点击，避免误锁屏。</summary>
    public static bool EditMode { get; set; }

    static WidgetRuntime() {
        Heartbeat.Tick += (_, _) => {
            long now = Environment.TickCount64;

            foreach (var view in Views.ToArray())
                view.Tick(now);
        };

        Heartbeat.Start();
    }

    public static void Track(WidgetView view) {
        if (!Views.Contains(view))
            Views.Add(view);
    }

    public static void Untrack(WidgetView view) {
        Views.Remove(view);
        view.Dispose();
    }

    public static void DeactivateAll() {
        foreach (var view in Views)
            view.IsActive = false;
    }
}

/// <summary>小组件通用视觉工具。</summary>
internal static class W {
    // 这几支笔不冻结：主题切换时直接改 Color，已经引用它们的控件会立刻跟着变。
    // 初始值只是占位，真正的配色由 Repaint() 按当前主题刷一遍。
    public static readonly SolidColorBrush TextBrush = new(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush DimBrush = new(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush FaintBrush = new(Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF));
    public static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0x4C, 0x8D, 0xFF));
    public static readonly SolidColorBrush WarnBrush = new(Color.FromRgb(0xFF, 0xC4, 0x6B));
    public static readonly SolidColorBrush DangerBrush = new(Color.FromRgb(0xFF, 0x7A, 0x6B));
    public static readonly SolidColorBrush ChipBrush = new(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    /// <summary>
    /// 按当前主题重新给通用画刷上色。由 <see cref="ThemeManager"/> 在主题变化时调用，
    /// 时钟、传感器这些常驻小组件不用重建就能变色。
    /// </summary>
    internal static void Repaint() {
        TextBrush.Color = ThemeManager.Ink(0xF2);
        DimBrush.Color = ThemeManager.Ink(0x99);
        FaintBrush.Color = ThemeManager.Ink(0x5C);
        ChipBrush.Color = ThemeManager.Ink(0x14);
        AccentBrush.Color = ThemeManager.ThemeColor(ThemeManager.Accent);

        WarnBrush.Color = ThemeManager.IsLight
            ? Color.FromRgb(0xC7, 0x7A, 0x16)
            : Color.FromRgb(0xFF, 0xC4, 0x6B);

        DangerBrush.Color = ThemeManager.IsLight
            ? Color.FromRgb(0xD9, 0x45, 0x3A)
            : Color.FromRgb(0xFF, 0x7A, 0x6B);
    }

    /// <summary>「白色叠加」换算成当前主题下的等价色（深色=半透明白，浅色=半透明黑）。</summary>
    public static Color Ink(byte alpha) => ThemeManager.Ink(alpha);

    /// <summary>
    /// 自绘控件（滑块、勾选圈、行内按钮）在 Tag 上打这个标记，
    /// 主页据此判断「按下点落在控件上」，不进入拖拽。
    /// </summary>
    public const string InteractiveTag = "wn-interactive";

    public static T Interactive<T>(T element) where T : FrameworkElement {
        element.Tag = InteractiveTag;
        return element;
    }

    public static Brush Frozen(Color color) {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static TextBlock Text(string text, double size, Brush? brush = null,
        FontWeight? weight = null) =>
        new() {
            Text = text,
            FontSize = size,
            Foreground = brush ?? TextBrush,
            FontWeight = weight ?? FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

    public static ShapePath Icon(string data, double size, Brush? brush = null) =>
        new() {
            Data = Geometry.Parse(data),
            Fill = brush ?? DimBrush,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

    /// <summary>内容卡片的内边距容器。</summary>
    public static Grid Pad(UIElement content, double left = 9, double top = 7, double right = 9,
        double bottom = 7) {
        var grid = new Grid { Margin = new Thickness(left, top, right, bottom) };
        grid.Children.Add(content);
        return grid;
    }

    public static Border Chip(UIElement content) =>
        new() {
            Background = ChipBrush,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = content
        };

    /// <summary>数值越高越醒目的配色。</summary>
    public static Brush Severity(double? percent) =>
        percent switch {
            null => TextBrush,
            >= 85 => DangerBrush,
            >= 65 => WarnBrush,
            _ => TextBrush
        };
}

/// <summary>细长的进度条：两列星号宽度按百分比分配。</summary>
internal sealed class MiniBar : Border {
    private readonly ColumnDefinition _fillColumn = new();
    private readonly ColumnDefinition _restColumn = new();
    private readonly Border _fill;

    public MiniBar(double height, Brush? fillBrush = null) {
        Height = height;
        CornerRadius = new CornerRadius(height / 2);
        Background = W.Frozen(W.Ink(0x2E));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(_fillColumn);
        grid.ColumnDefinitions.Add(_restColumn);

        _fill = new Border {
            CornerRadius = new CornerRadius(height / 2),
            Background = fillBrush ?? W.AccentBrush
        };

        grid.Children.Add(_fill);
        Child = grid;

        SetValue(0);
    }

    public void SetValue(double percent, Brush? fillBrush = null) {
        percent = Math.Clamp(percent, 0, 100);

        _fillColumn.Width = new GridLength(Math.Max(percent, 0.01), GridUnitType.Star);
        _restColumn.Width = new GridLength(Math.Max(100 - percent, 0.01), GridUnitType.Star);

        if (fillBrush != null)
            _fill.Background = fillBrush;
    }
}

/// <summary>可横向拖动的小滑块，视觉比系统 Slider 更好塞进小格子。</summary>
internal sealed class SlideBar : Grid {
    private const double ThumbSize = 12;

    private readonly Border _fill;
    private readonly Border _thumb;
    private readonly Border _track;

    private bool _dragging;

    public event Action<double>? ValueChanged;

    /// <summary>0–100。</summary>
    public double Value { get; private set; }

    /// <summary>用户刚拖过的时间戳，避免心跳把用户的操作覆盖掉。</summary>
    public long LastUserInput { get; private set; }

    public SlideBar() {
        Height = 20;
        Background = Brushes.Transparent;
        W.Interactive(this);

        _track = new Border {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = W.Frozen(W.Ink(0x33)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        _fill = new Border {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = W.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        _thumb = new Border {
            Width = ThumbSize,
            Height = ThumbSize,
            CornerRadius = new CornerRadius(ThumbSize / 2),
            Background = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        Children.Add(_track);
        Children.Add(_fill);
        Children.Add(_thumb);

        SizeChanged += (_, _) => Layout();

        MouseLeftButtonDown += (_, e) => {
            _dragging = true;
            CaptureMouse();
            ApplyFromPoint(e.GetPosition(this));
            e.Handled = true;
        };

        MouseMove += (_, e) => {
            if (_dragging && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                ApplyFromPoint(e.GetPosition(this));
        };

        MouseLeftButtonUp += (_, e) => {
            if (!_dragging)
                return;

            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        };

        LostMouseCapture += (_, _) => _dragging = false;
    }

    public void SetValue(double percent, bool notify = false) {
        Value = Math.Clamp(percent, 0, 100);
        Layout();

        if (notify)
            ValueChanged?.Invoke(Value);
    }

    private void ApplyFromPoint(Point point) {
        double usable = ActualWidth - ThumbSize;
        if (usable <= 0)
            return;

        double percent = Math.Clamp((point.X - ThumbSize / 2) / usable, 0, 1) * 100;

        LastUserInput = Environment.TickCount64;
        Value = percent;
        Layout();
        ValueChanged?.Invoke(Value);
    }

    private void Layout() {
        double usable = Math.Max(0, ActualWidth - ThumbSize);
        double position = usable * (Value / 100.0);

        _track.Width = Math.Max(0, ActualWidth);
        _fill.Width = position;
        _thumb.Margin = new Thickness(Math.Max(0, position), 0, 0, 0);
    }
}
