using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WidgetNest;

/// <summary>3×6 整页小组件共用的输入框与按钮。</summary>
internal static class BoardUi {
    public static TextBox Input(string placeholder) {
        var box = new TextBox {
            Height = 24,
            FontSize = 11,
            Foreground = W.TextBrush,
            CaretBrush = W.TextBrush,
            Background = W.Frozen(W.Ink(0x1A)),
            BorderBrush = W.Frozen(W.Ink(0x2E)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 0, 7, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            SelectionBrush = W.AccentBrush,
            Tag = placeholder
        };

        return box;
    }

    /// <summary>给输入框加占位文字（TextBox 没有原生 placeholder）。</summary>
    public static FrameworkElement WithPlaceholder(TextBox box) {
        var hint = new TextBlock {
            Text = box.Tag as string ?? "",
            FontSize = 11,
            Foreground = W.FaintBrush,
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        void Sync() => hint.Visibility = string.IsNullOrEmpty(box.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        box.TextChanged += (_, _) => Sync();
        Sync();

        var host = new Grid();
        host.Children.Add(box);
        host.Children.Add(hint);

        return host;
    }

    public static Border Button(string text, Action click, bool primary = true) {
        var border = new Border {
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 0, 10, 0),
            Background = primary
                ? W.Frozen(Color.FromArgb(0xCC, 0x4C, 0x8D, 0xFF))
                : W.Frozen(W.Ink(0x1A)),
            Child = new TextBlock {
                Text = text,
                FontSize = 11,
                Foreground = primary ? Brushes.White : W.DimBrush,
                VerticalAlignment = VerticalAlignment.Center
            },
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };

        W.Interactive(border);

        border.MouseEnter += (_, _) => border.Opacity = 0.85;
        border.MouseLeave += (_, _) => border.Opacity = 1;
        border.MouseLeftButtonUp += (_, e) => {
            if (WidgetRuntime.EditMode)
                return;

            click();
            e.Handled = true;
        };

        return border;
    }

    public static Border IconButton(string glyph, string tooltip, Action click, double size = 18) {
        var border = new Border {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Child = new TextBlock {
                Text = glyph,
                FontSize = 12,
                Foreground = W.FaintBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center
        };

        W.Interactive(border);

        border.MouseEnter += (_, _) => border.Background = W.Frozen(W.Ink(0x1F));
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, e) => {
            if (WidgetRuntime.EditMode)
                return;

            click();
            e.Handled = true;
        };

        return border;
    }

    public static ScrollViewer Scroll(UIElement content) =>
        new() {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
            Padding = new Thickness(0, 0, 4, 0)
        };
}

// ===== 待办事项（3×6） =====

internal sealed class TodoBoardView : WidgetView {
    private readonly TextBox _input = BoardUi.Input("添加待办，回车确认");
    private readonly TextBlock _summary = W.Text("", 9, W.FaintBrush);
    private readonly StackPanel _list = new();

    private string _signature = "";

    public TodoBoardView(WidgetBox box) {
        // 整页与半页共用这套版式：高度不够时下面的列表自己滚
        double s = box.Rows >= 3 ? 1 : 0.92;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var title = W.Text("待办事项", 12 * s, W.TextBrush, FontWeights.SemiBold);
        header.Children.Add(title);

        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_summary, 1);
        header.Children.Add(_summary);

        var inputRow = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var inputHost = BoardUi.WithPlaceholder(_input);
        inputRow.Children.Add(inputHost);

        var addButton = BoardUi.Button("添加", () => CommitInput());
        addButton.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(addButton, 1);
        inputRow.Children.Add(addButton);

        _input.KeyDown += (_, e) => {
            if (e.Key != Key.Enter)
                return;

            CommitInput();
            e.Handled = true;
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(header);

        Grid.SetRow(inputRow, 1);
        grid.Children.Add(inputRow);

        var scroll = BoardUi.Scroll(_list);
        Grid.SetRow(scroll, 2);
        grid.Children.Add(scroll);

        Root = W.Pad(grid, 11, 8, 9, 7);
        Rebuild(force: true);
    }

    public override int IntervalMs => 1500;

    private void CommitInput() {
        string text = _input.Text;

        if (string.IsNullOrWhiteSpace(text))
            return;

        WidgetDataStore.AddTodo(text);
        _input.Text = "";
        Rebuild(force: true);
    }

    public override void Update() => Rebuild(force: false);

    private void Rebuild(bool force) {
        var todos = WidgetDataStore.Current.Todos;

        string signature = string.Join("|", todos.Select(todo => todo.Id + (todo.Done ? "1" : "0") + todo.Text));

        if (!force && signature == _signature)
            return;

        _signature = signature;

        int done = todos.Count(todo => todo.Done);
        _summary.Text = todos.Count == 0 ? "" : $"{done} / {todos.Count} 已完成";

        _list.Children.Clear();

        if (todos.Count == 0) {
            _list.Children.Add(new TextBlock {
                Text = "还没有待办。在上面输入一行，回车即可添加。",
                FontSize = 10.5,
                Foreground = W.FaintBrush,
                Margin = new Thickness(2, 12, 0, 0)
            });

            return;
        }

        foreach (var todo in todos)
            _list.Children.Add(BuildRow(todo));
    }

    private FrameworkElement BuildRow(TodoItem todo) {
        var check = new Border {
            Width = 15,
            Height = 15,
            CornerRadius = new CornerRadius(7.5),
            BorderThickness = new Thickness(1.3),
            BorderBrush = todo.Done ? W.AccentBrush : W.FaintBrush,
            Background = todo.Done ? W.AccentBrush : Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand
        };

        if (todo.Done) {
            check.Child = new TextBlock {
                Text = "✓",
                FontSize = 9,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        W.Interactive(check);

        check.MouseLeftButtonUp += (_, e) => {
            if (WidgetRuntime.EditMode)
                return;

            WidgetDataStore.ToggleTodo(todo.Id);
            Rebuild(force: true);
            e.Handled = true;
        };

        var text = new TextBlock {
            Text = todo.Text,
            FontSize = 11,
            Foreground = todo.Done ? W.FaintBrush : W.DimBrush,
            TextDecorations = todo.Done ? TextDecorations.Strikethrough : null,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 6, 0)
        };

        var delete = BoardUi.IconButton("✕", "删除该待办", () => {
            WidgetDataStore.RemoveTodo(todo.Id);
            Rebuild(force: true);
        }, 16);

        var row = new Grid { Height = 25 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(check);

        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        Grid.SetColumn(delete, 2);
        row.Children.Add(delete);

        // 行底色：悬停时轻微提亮，提示可点
        var host = new Border {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(4, 0, 4, 0),
            Child = row,
            Background = Brushes.Transparent
        };

        host.MouseEnter += (_, _) => host.Background = W.Frozen(W.Ink(0x0F));
        host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;

        return host;
    }
}

// ===== 日程（3×6） =====

internal sealed class CalendarBoardView : WidgetView {
    private static readonly string[] WeekHeaders = { "日", "一", "二", "三", "四", "五", "六" };

    private readonly TextBlock _monthTitle = W.Text("", 11.5, W.TextBrush, FontWeights.SemiBold);
    private readonly Grid _dayGrid = new();
    private readonly TextBlock _selectedTitle = W.Text("", 11, W.TextBrush, FontWeights.SemiBold);
    private readonly StackPanel _eventList = new();
    private readonly TextBox _input = BoardUi.Input("添加日程，回车确认");

    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selected = DateTime.Today;
    private string _signature = "";

    public CalendarBoardView(WidgetBox box) {
        // 只有整页尺寸会走到这里（半页/条状是 ScheduleBarView），副栏高度按行数微调
        double detailHeight = Math.Max(56, 78 + (box.Rows - 3) * 36);

        var left = BuildCalendar();
        var right = BuildDetail(detailHeight);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(152) });

        grid.Children.Add(left);

        var divider = new Border {
            Width = 1,
            Background = W.Frozen(W.Ink(0x1F)),
            Margin = new Thickness(10, 2, 10, 2)
        };

        Grid.SetColumn(divider, 1);
        grid.Children.Add(divider);

        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        Root = W.Pad(grid, 11, 8, 11, 8);
        RebuildAll();
    }

    public override int IntervalMs => 2000;

    private FrameworkElement BuildCalendar() {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var prev = BoardUi.IconButton("‹", "上个月", () => ShiftMonth(-1), 18);
        header.Children.Add(prev);

        _monthTitle.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(_monthTitle, 1);
        header.Children.Add(_monthTitle);

        var next = BoardUi.IconButton("›", "下个月", () => ShiftMonth(1), 18);
        Grid.SetColumn(next, 2);
        header.Children.Add(next);

        var weekHeader = new UniformGrid { Columns = 7, Margin = new Thickness(0, 4, 0, 2) };

        foreach (string day in WeekHeaders) {
            weekHeader.Children.Add(new TextBlock {
                Text = day,
                FontSize = 8.5,
                Foreground = W.FaintBrush,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        for (int i = 0; i < 7; i++)
            _dayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < 6; i++)
            _dayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(weekHeader);
        stack.Children.Add(_dayGrid);

        return stack;
    }

    private FrameworkElement BuildDetail(double eventListHeight) {
        var stack = new StackPanel();

        stack.Children.Add(_selectedTitle);

        var scroll = BoardUi.Scroll(_eventList);
        scroll.Height = eventListHeight;
        scroll.Margin = new Thickness(0, 5, 0, 5);
        stack.Children.Add(scroll);

        var inputRow = new Grid();
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        inputRow.Children.Add(BoardUi.WithPlaceholder(_input));

        var add = BoardUi.Button("＋", () => CommitInput());
        add.Margin = new Thickness(5, 0, 0, 0);
        add.Padding = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(add, 1);
        inputRow.Children.Add(add);

        _input.KeyDown += (_, e) => {
            if (e.Key != Key.Enter)
                return;

            CommitInput();
            e.Handled = true;
        };

        stack.Children.Add(inputRow);
        return stack;
    }

    private void ShiftMonth(int delta) {
        _month = _month.AddMonths(delta);
        RebuildAll();
    }

    private void CommitInput() {
        string text = _input.Text;

        if (string.IsNullOrWhiteSpace(text))
            return;

        WidgetDataStore.AddEvent(_selected, text);
        _input.Text = "";
        RebuildAll(force: true);
    }

    public override void Update() => RebuildAll(force: false);

    private void RebuildAll(bool force = false) {
        string signature = string.Join("|", WidgetDataStore.Current.Events.Select(item => item.Id + item.Date + item.Title))
                           + "#" + _selected.Ticks + "#" + _month.Ticks;

        if (!force && signature == _signature)
            return;

        _signature = signature;

        _monthTitle.Text = _month.ToString("yyyy 年 M 月");
        _selectedTitle.Text = _selected.ToString("M 月 d 日") + " " + WeekdayName(_selected);

        BuildDays();
        BuildEvents();
    }

    private static string WeekdayName(DateTime date) =>
        "周" + WeekHeaders[(int)date.DayOfWeek];

    private void BuildDays() {
        _dayGrid.Children.Clear();

        var firstVisible = _month.AddDays(-(int)_month.DayOfWeek);

        for (int index = 0; index < 42; index++) {
            var date = firstVisible.AddDays(index);

            var label = new TextBlock {
                Text = date.Day.ToString(),
                FontSize = 9.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = date.Month == _month.Month ? W.DimBrush : W.FaintBrush
            };

            var cell = new Border {
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Child = label,
                Cursor = Cursors.Hand
            };

            bool isToday = date.Date == DateTime.Today;
            bool isSelected = date.Date == _selected.Date;

            if (isSelected) {
                cell.Background = W.AccentBrush;
                label.Foreground = Brushes.White;
                label.FontWeight = FontWeights.SemiBold;
            }
            else if (isToday) {
                cell.BorderBrush = W.AccentBrush;
                cell.BorderThickness = new Thickness(1);
                label.Foreground = W.TextBrush;
            }

            // 有日程的日期在右下角点一个点
            if (WidgetDataStore.EventsOn(date).Count > 0 && !isSelected) {
                var dot = new Border {
                    Width = 4,
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = W.AccentBrush,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 3, 3)
                };

                var host = new Grid();
                host.Children.Add(cell);
                host.Children.Add(dot);
                W.Interactive(host);
                host.MouseLeftButtonUp += (_, e) => Select(date, e);

                Grid.SetRow(host, index / 7);
                Grid.SetColumn(host, index % 7);
                _dayGrid.Children.Add(host);
                continue;
            }

            W.Interactive(cell);
            cell.MouseLeftButtonUp += (_, e) => Select(date, e);

            Grid.SetRow(cell, index / 7);
            Grid.SetColumn(cell, index % 7);
            _dayGrid.Children.Add(cell);
        }
    }

    private void Select(DateTime date, MouseButtonEventArgs e) {
        if (WidgetRuntime.EditMode)
            return;

        if (date.Month != _month.Month)
            _month = new DateTime(date.Year, date.Month, 1);

        _selected = date;
        RebuildAll(force: true);
        e.Handled = true;
    }

    private void BuildEvents() {
        _eventList.Children.Clear();

        var events = WidgetDataStore.EventsOn(_selected);

        if (events.Count == 0) {
            _eventList.Children.Add(new TextBlock {
                Text = "这一天还没有日程",
                FontSize = 10,
                Foreground = W.FaintBrush,
                Margin = new Thickness(2, 4, 0, 0)
            });

            return;
        }

        foreach (var item in events) {
            var text = new TextBlock {
                Text = item.Title,
                FontSize = 10.5,
                Foreground = W.DimBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            var delete = BoardUi.IconButton("✕", "删除日程", () => {
                WidgetDataStore.RemoveEvent(item.Id);
                RebuildAll(force: true);
            }, 16);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(text);

            Grid.SetColumn(delete, 1);
            row.Children.Add(delete);

            _eventList.Children.Add(new Border {
                Padding = new Thickness(2, 3, 0, 3),
                Child = row
            });
        }
    }
}
