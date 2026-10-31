using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;

namespace WidgetNest;

/// <summary>
/// 自定义组件的渲染器：按清单里的布局现画界面，按数据源取内容。
///
/// 约定：
/// - 静态 / 时钟类数据源每帧现算，不占后台线程；
/// - 命令 / 文件类数据源在后台跑，按各自的 interval 节流，结果回到 UI 线程再刷；
/// - 命令超时会被杀掉，任何异常都只影响这一个组件。
/// </summary>
internal sealed class UserWidgetView : WidgetView {
    private static readonly string[] Weekdays = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

    private readonly UserWidget _widget;
    private readonly WidgetLayout _layout;
    private readonly double _scale;
    private readonly Dispatcher _dispatcher;

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TextSlot> _texts = new();
    private readonly List<BarSlot> _bars = new();

    private readonly Dictionary<string, long> _lastFetch = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cancel = new();

    private Border? _clickSurface;

    private sealed record TextSlot(TextBlock Block, string Template, string Color);

    private sealed record BarSlot(MiniBar Bar, string Template, string Color);

    public UserWidgetView(UserWidget widget, WidgetBox box) {
        _widget = widget;
        _dispatcher = Dispatcher.CurrentDispatcher;

        var layout = widget.FindLayout(box, out double scale);
        _layout = layout ?? new WidgetLayout();
        _scale = scale;

        foreach (var spec in widget.Data) {
            // 命令 / 文件类先占个位，避免第一帧只剩单位符号孤零零挂着
            _values[spec.Key] = spec.Kind switch {
                WidgetDataKind.Static => spec.Value,
                _ => "—"
            };
        }

        var body = BuildBody();

        var padded = new Border {
            Padding = new Thickness(_layout.PadLeft, _layout.PadTop, _layout.PadRight, _layout.PadBottom),
            Child = body
        };

        if (widget.ActionRun.Length > 0) {
            _clickSurface = new Border {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(7),
                Child = padded,
                Cursor = Cursors.Hand,
                ToolTip = string.IsNullOrWhiteSpace(widget.Description) ? null : widget.Description
            };

            _clickSurface.MouseEnter += (_, _) => _clickSurface.Background = W.Frozen(W.Ink(0x1F));
            _clickSurface.MouseLeave += (_, _) => _clickSurface.Background = Brushes.Transparent;
            _clickSurface.MouseLeftButtonUp += (_, e) => {
                if (WidgetRuntime.EditMode)
                    return;

                RunAction();
                e.Handled = true;
            };

            W.Interactive(_clickSurface);
            Root = _clickSurface;
        }
        else {
            Root = padded;
        }

        Render();
        KickFetches(force: true);
    }

    public override int IntervalMs => _widget.RefreshMs;

    public override void Update() {
        Render();
        KickFetches(force: false);
    }

    public override void Dispose() {
        try {
            _cancel.Cancel();
        }
        catch {
            // 已经取消了
        }
    }

    // ===== 绘制 =====

    private FrameworkElement BuildBody() {
        var grid = new Grid { VerticalAlignment = VerticalOf(_layout.Align) };

        foreach (var row in _layout.Rows) {
            grid.RowDefinitions.Add(new RowDefinition {
                Height = row.Kind == WidgetNodeKind.Space
                    ? new GridLength(1, GridUnitType.Star)
                    : GridLength.Auto
            });

            var element = BuildNode(row);
            Grid.SetRow(element, grid.RowDefinitions.Count - 1);
            grid.Children.Add(element);
        }

        return grid;
    }

    private FrameworkElement BuildNode(WidgetNode node) => node.Kind switch {
        WidgetNodeKind.Text => BuildText(node),
        WidgetNodeKind.Bar => BuildBar(node),
        WidgetNodeKind.Icon => BuildIcon(node),
        WidgetNodeKind.Row => BuildRow(node),
        _ => new Grid()
    };

    private FrameworkElement BuildText(WidgetNode node) {
        double size = node.Size > 0 ? node.Size * _scale : 10 * _scale;

        var block = W.Text(node.Text, size, BrushFor(node.Color, null), WeightOf(node.Weight));
        block.HorizontalAlignment = HorizontalOf(node.Align);
        block.TextAlignment = TextOf(node.Align);

        if (node.Wrap) {
            block.TextWrapping = TextWrapping.Wrap;
            block.TextTrimming = TextTrimming.None;
        }

        _texts.Add(new TextSlot(block, node.Text, node.Color));

        return block;
    }

    private FrameworkElement BuildBar(WidgetNode node) {
        var bar = new MiniBar(node.Height);

        _bars.Add(new BarSlot(bar, node.Text, node.Color));

        return new Border {
            Child = bar,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
    }

    private FrameworkElement BuildIcon(WidgetNode node) {
        ShapePath icon = W.Icon(node.IconPath, node.Size * _scale, BrushFor(node.Color, null));
        icon.HorizontalAlignment = HorizontalOf(node.Align);

        return icon;
    }

    private FrameworkElement BuildRow(WidgetNode node) {
        var grid = new Grid();

        for (int i = 0; i < node.Cells.Count; i++) {
            var cell = node.Cells[i];

            grid.ColumnDefinitions.Add(new ColumnDefinition {
                Width = cell.Flex > 0
                    ? new GridLength(cell.Flex, GridUnitType.Star)
                    : GridLength.Auto
            });

            var element = BuildNode(cell);

            if (i > 0 && node.Gap > 0)
                element.Margin = new Thickness(node.Gap, 0, 0, 0);

            Grid.SetColumn(element, i);
            grid.Children.Add(element);
        }

        return grid;
    }

    // ===== 刷新 =====

    private void Render() {
        var now = DateTime.Now;

        foreach (var slot in _texts) {
            string text = WidgetTemplate.Fill(slot.Template, key => Lookup(key, now));

            if (slot.Block.Text != text)
                slot.Block.Text = text;

            if (slot.Color == "severity")
                slot.Block.Foreground = BrushFor(slot.Color, text);
        }

        foreach (var slot in _bars) {
            string raw = WidgetTemplate.Fill(slot.Template, key => Lookup(key, now));
            double value = ParseNumber(raw, 0);

            Brush? fill = slot.Color switch {
                "severity" => W.Severity(value),
                "accent" or "" => null,
                _ => BrushFor(slot.Color, null)
            };

            slot.Bar.SetValue(value, fill);
        }
    }

    private string Lookup(string key, DateTime now) {
        switch (key.ToLowerInvariant()) {
            case "time":
                return now.ToString("HH:mm");

            case "date":
                return now.ToString("yyyy-MM-dd");

            case "weekday":
                return Weekdays[(int)now.DayOfWeek];

            case "day":
                return now.Day.ToString("00", CultureInfo.InvariantCulture);

            case "month":
                return now.Month.ToString("00", CultureInfo.InvariantCulture);

            case "year":
                return now.Year.ToString(CultureInfo.InvariantCulture);
        }

        foreach (var spec in _widget.Data) {
            if (!string.Equals(spec.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;

            if (spec.Kind == WidgetDataKind.Clock)
                return now.ToString(spec.Format);

            return _values.TryGetValue(spec.Key, out string? value) ? value : "";
        }

        return "";
    }

    // ===== 命令 / 文件数据源 =====

    private void KickFetches(bool force) {
        if (_cancel.IsCancellationRequested)
            return;

        long now = Environment.TickCount64;

        foreach (var spec in _widget.Data) {
            if (spec.Kind is not (WidgetDataKind.Command or WidgetDataKind.File))
                continue;

            if (_running.Contains(spec.Key))
                continue;

            if (!force && _lastFetch.TryGetValue(spec.Key, out long last) &&
                now - last < spec.IntervalMs)
                continue;

            _lastFetch[spec.Key] = now;
            _running.Add(spec.Key);

            var captured = spec;

            Task.Run(() => {
                if (_cancel.IsCancellationRequested)
                    return;

                string value;

                try {
                    value = captured.Kind == WidgetDataKind.Command
                        ? WidgetShell.Run(captured.Run, _widget.Folder, captured.TimeoutMs, captured.FirstLine)
                        : ReadFile(captured);
                }
                catch {
                    value = "—";
                }

                Post(captured.Key, value);
            });
        }
    }

    private static string ReadFile(WidgetDataSpec spec) {
        try {
            if (spec.FilePath.Length == 0 || !File.Exists(spec.FilePath))
                return "—";

            string text = File.ReadAllText(spec.FilePath).Trim();

            if (!spec.FirstLine)
                return Clip(text);

            foreach (string line in text.Split('\n')) {
                string trimmed = line.Trim();

                if (trimmed.Length > 0)
                    return Clip(trimmed);
            }

            return "—";
        }
        catch {
            return "—";
        }
    }

    private void Post(string key, string value) {
        if (_cancel.IsCancellationRequested)
            return;

        _dispatcher.BeginInvoke(new Action(() => {
            _running.Remove(key);

            if (_cancel.IsCancellationRequested)
                return;

            _values[key] = value;
            Render();
        }));
    }

    private void RunAction() {
        if (_cancel.IsCancellationRequested)
            return;

        _clickSurface!.Background = W.Frozen(W.Ink(0x33));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };

        timer.Tick += (_, _) => {
            timer.Stop();

            if (_clickSurface != null)
                _clickSurface.Background = Brushes.Transparent;
        };

        timer.Start();

        string run = _widget.ActionRun;
        string folder = _widget.Folder;

        Task.Run(() => WidgetShell.Run(run, folder, 8000, firstLine: true));
    }

    private static string Clip(string text) {
        text = text.Replace("\r", "").Trim();

        if (text.Length > 240)
            text = text[..240] + "…";

        return text.Length == 0 ? "—" : text;
    }

    // ===== 小工具 =====

    private static double ParseNumber(string text, double fallback) {
        var builder = new StringBuilder();
        bool started = false;

        foreach (char c in text) {
            if (char.IsDigit(c) || c == '.' || (c == '-' && !started)) {
                builder.Append(c);
                started = true;
            }
            else if (started) {
                break;
            }
        }

        return builder.Length > 0 &&
               double.TryParse(builder.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;
    }

    private static Brush BrushFor(string color, string? text) => color switch {
        "dim" or "secondary" => W.DimBrush,
        "faint" or "tertiary" => W.FaintBrush,
        "accent" => W.AccentBrush,
        "warn" => W.WarnBrush,
        "danger" or "error" => W.DangerBrush,
        "severity" => W.Severity(text == null ? null : ParseNumber(text, 0)),
        _ => W.TextBrush
    };

    private static FontWeight WeightOf(string weight) => weight switch {
        "semibold" or "medium" or "500" or "600" => FontWeights.SemiBold,
        "bold" or "700" => FontWeights.Bold,
        _ => FontWeights.Normal
    };

    private static HorizontalAlignment HorizontalOf(string align) => align switch {
        "center" => HorizontalAlignment.Center,
        "right" => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left
    };

    private static TextAlignment TextOf(string align) => align switch {
        "center" => TextAlignment.Center,
        "right" => TextAlignment.Right,
        _ => TextAlignment.Left
    };

    private static VerticalAlignment VerticalOf(string align) => align switch {
        "top" => VerticalAlignment.Top,
        "bottom" => VerticalAlignment.Bottom,
        "stretch" => VerticalAlignment.Stretch,
        _ => VerticalAlignment.Center
    };
}

/// <summary>跑自定义组件里的命令：后台执行、隐藏窗口、超时即杀。</summary>
internal static class WidgetShell {
    public static string Run(string command, string workingFolder, int timeoutMs, bool firstLine) {
        if (string.IsNullOrWhiteSpace(command))
            return "—";

        try {
            var info = new ProcessStartInfo("cmd.exe", "/c " + command) {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Directory.Exists(workingFolder) ? workingFolder : Environment.CurrentDirectory
            };

            using var process = Process.Start(info);

            if (process == null)
                return "—";

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(Math.Clamp(timeoutMs, 200, 30_000))) {
                try {
                    process.Kill(entireProcessTree: true);
                }
                catch {
                    // 已经退了
                }

                return "—";
            }

            string text = output.Wait(600) ? output.Result : "";
            text = text.Replace("\r", "").Trim();

            if (text.Length == 0)
                return "—";

            if (firstLine) {
                foreach (string line in text.Split('\n')) {
                    string trimmed = line.Trim();

                    if (trimmed.Length > 0) {
                        text = trimmed;
                        break;
                    }
                }
            }

            return text.Length > 240 ? text[..240] + "…" : text;
        }
        catch {
            return "—";
        }
    }
}
