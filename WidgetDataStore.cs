using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WidgetNest;

public sealed class TodoItem {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text { get; set; } = "";

    public bool Done { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class ScheduleEvent {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>yyyy-MM-dd。</summary>
    public string Date { get; set; } = "";

    /// <summary>HH:mm，可空。</summary>
    public string Time { get; set; } = "";

    public string Title { get; set; } = "";
}

public sealed class WidgetData {
    public List<TodoItem> Todos { get; set; } = new();

    public List<ScheduleEvent> Events { get; set; } = new();

    /// <summary>天气组件选择的位置（全球任意城市）；null = 还没选过位置。</summary>
    public WeatherPlace? Weather { get; set; }

    /// <summary>
    /// 旧版存档字段：只存城市名，没有坐标。读取时迁移到 <see cref="Weather"/>，
    /// 写盘时同步写回，降级到旧版本也不会丢位置。
    /// </summary>
    public string WeatherCity { get; set; } = "";
}

/// <summary>
/// 待办 / 日程的本地存档：%LOCALAPPDATA%\WidgetNest\widgets.json
/// </summary>
internal static class WidgetDataStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string FilePath => Path.Combine(AppStorage.RootDir, "widgets.json");

    private static WidgetData? _cache;

    /// <summary>进程内共享的实例：多个小组件改的是同一份数据。</summary>
    public static WidgetData Current {
        get {
            _cache ??= Load();
            return _cache;
        }
    }

    public static WidgetData Load() {
        try {
            AppStorage.EnsureCreated();

            if (!File.Exists(FilePath))
                return new WidgetData();

            string json = File.ReadAllText(FilePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return new WidgetData();

            return Migrate(JsonSerializer.Deserialize<WidgetData>(json, JsonOptions) ?? new WidgetData());
        }
        catch {
            return new WidgetData();
        }
    }

    /// <summary>把旧存档里的纯城市名补成带名字的位置对象。</summary>
    private static WidgetData Migrate(WidgetData data) {
        if (data.Weather is null || data.Weather.IsEmpty) {
            string legacy = (data.WeatherCity ?? "").Trim();
            data.Weather = legacy.Length == 0
                ? null
                : new WeatherPlace { Name = legacy, Country = "" };
        }

        data.WeatherCity = "";
        return data;

    }

    public static void Save(WidgetData data) {
        try {
            AppStorage.EnsureCreated();

            // 旧字段跟着一起写，换回老版本也还能认
            data.WeatherCity = data.Weather?.Name ?? "";

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions), Utf8NoBom);

            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, null);
            else
                File.Move(temp, FilePath);

            _cache = data;
        }
        catch {
            // 落盘失败不影响当前会话
        }
    }

    // ===== 天气位置 =====

    /// <summary>
    /// 天气组件当前选择的位置；null 表示未选择（组件显示「位置未知」）。
    /// 传 null 即清除位置。
    /// </summary>
    public static WeatherPlace? Weather {
        get => Current.Weather;
        set {
            var data = Current;

            if (WeatherPlace.Same(data.Weather, value))
                return;

            data.Weather = value is null || value.IsEmpty ? null : value;
            Save(data);
        }
    }

    /// <summary>只用于显示的简短位置名，空字符串表示未选择。</summary>
    public static string WeatherCity => Current.Weather?.Name ?? "";

    // ===== 待办 =====

    public static void AddTodo(string text) {
        text = (text ?? "").Trim();
        if (text.Length == 0)
            return;

        var data = Current;
        data.Todos.Insert(0, new TodoItem { Text = text });
        Save(data);
    }

    public static void ToggleTodo(string id) {
        var data = Current;
        var todo = data.Todos.FirstOrDefault(item => item.Id == id);
        if (todo == null)
            return;

        todo.Done = !todo.Done;
        Save(data);
    }

    public static void RemoveTodo(string id) {
        var data = Current;
        if (data.Todos.RemoveAll(item => item.Id == id) > 0)
            Save(data);
    }

    public static List<TodoItem> UndoneTodos(int max) =>
        Current.Todos.Where(item => !item.Done).Take(max).ToList();

    // ===== 日程 =====

    public static void AddEvent(DateTime date, string title, string time = "") {
        title = (title ?? "").Trim();
        if (title.Length == 0)
            return;

        var data = Current;

        data.Events.Add(new ScheduleEvent {
            Date = date.ToString("yyyy-MM-dd"),
            Time = time ?? "",
            Title = title
        });

        Save(data);
    }

    public static void RemoveEvent(string id) {
        var data = Current;
        if (data.Events.RemoveAll(item => item.Id == id) > 0)
            Save(data);
    }

    public static List<ScheduleEvent> EventsOn(DateTime date) {
        string key = date.ToString("yyyy-MM-dd");

        return Current.Events
            .Where(item => item.Date == key)
            .OrderBy(item => item.Time, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>从今天开始最近的几条日程（含未来）。</summary>
    public static List<ScheduleEvent> Upcoming(int max) {
        string today = DateTime.Today.ToString("yyyy-MM-dd");

        return Current.Events
            .Where(item => string.CompareOrdinal(item.Date, today) >= 0)
            .OrderBy(item => item.Date, StringComparer.Ordinal)
            .ThenBy(item => item.Time, StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }
}
