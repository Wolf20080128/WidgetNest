namespace WidgetNest;

public class AppItem {
    /// <summary>矢量兜底图标（位图取不到时使用）。</summary>
    public const string DefaultIconPath =
        "M6,2 H18 A2,2 0 0 1 20,4 V20 A2,2 0 0 1 18,22 H6 A2,2 0 0 1 4,20 V4 A2,2 0 0 1 6,2 Z";

    /// <summary>普通应用图标。</summary>
    public const string KindApp = "app";

    /// <summary>小组件（占多个网格单元，内容由 WidgetFactory 生成）。</summary>
    public const string KindWidget = "widget";

    public string Name { get; set; } = "";

    /// <summary>图标来源文件的原始路径（后续用于启动）。</summary>
    public string TargetPath { get; set; } = "";

    /// <summary>FilesDate 目录内的图标文件名，如 "a1b2c3d4e5f60718.png"。</summary>
    public string IconFile { get; set; } = "";

    public string IconPath { get; set; } = DefaultIconPath;

    /// <summary>"app" 或 "widget"；旧存档没有该字段，按应用处理。</summary>
    public string Kind { get; set; } = KindApp;

    /// <summary>小组件类型 Id（见 WidgetCatalog），普通应用为空。</summary>
    public string WidgetId { get; set; } = "";

    /// <summary>占用行数（1–3）。</summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>占用列数（1–6）。</summary>
    public int ColSpan { get; set; } = 1;

    public int Page { get; set; }

    public int Row { get; set; }

    public int Col { get; set; }

    /// <summary>
    /// 组合标记：相邻的两个小组件共用同一个 GroupId 时，渲染上合为一块。
    /// 空字符串表示未组合；旧存档没有该字段，默认也是空。
    /// </summary>
    public string GroupId { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsWidget => Kind == KindWidget;

    [System.Text.Json.Serialization.JsonIgnore]
    public int SafeRowSpan => Math.Clamp(RowSpan <= 0 ? 1 : RowSpan, 1, 3);

    [System.Text.Json.Serialization.JsonIgnore]
    public int SafeColSpan => Math.Clamp(ColSpan <= 0 ? 1 : ColSpan, 1, 6);
}
