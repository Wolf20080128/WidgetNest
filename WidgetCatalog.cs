using System;
using System.Collections.Generic;
using System.Linq;

namespace WidgetNest;

/// <summary>
/// 小组件定义。尺寸按网格单元计：Rows 行 × Cols 列（每页 3 行 × 6 列）。
/// </summary>
public sealed class WidgetDef {
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>名称全拼（小写），市场按它排 A→Z。</summary>
    public required string Pinyin { get; init; }

    public required int Rows { get; init; }

    public required int Cols { get; init; }

    public required string Description { get; init; }

    /// <summary>Material 风格的 24×24 矢量图标。</summary>
    public required string IconPath { get; init; }

    /// <summary>整块即按钮的小组件：按下时不进入拖拽，直接把点击交给控件自己。</summary>
    public bool IsAction { get; init; }

    /// <summary>
    /// 家族键：同一小组件的不同尺寸（例如时钟的 1×2 与 1×1）归为一家。
    /// 市场里一个格子 = 一个家族，具体尺寸在单击后的二级菜单里选。留空表示自成一家。
    /// </summary>
    public string Family { get; init; } = "";

    /// <summary>这一档尺寸的叫法（标准 / 迷你 / 条状 / 整页），显示在市场的尺寸选项上。</summary>
    public string SizeLabel { get; init; } = "标准";

    /// <summary>
    /// 额外的可选尺寸：市场二级菜单里和主档一起列出，并按此展开出同内容、不同跨度的变体。
    /// 只有主档需要写，展开出来的变体不再带 Sizes。
    /// </summary>
    public IReadOnlyList<WidgetSizeSpec> Sizes { get; init; } = Array.Empty<WidgetSizeSpec>();

    /// <summary>
    /// 用户自己开发、导入的组件（来自 widgets 目录里的 widget.json）。
    /// 市场里可以移除，工厂按清单里的布局现画，不走内置分支。
    /// </summary>
    public bool IsUserWidget { get; init; }

    public string FamilyKey => string.IsNullOrEmpty(Family) ? Id : Family;

    public string Initial => Pinyin.Length == 0 ? "#" : char.ToUpperInvariant(Pinyin[0]).ToString();

    public string SizeText => $"{Rows}×{Cols}";

    public string SpanText => $"占 {Rows} 行 × {Cols} 列";
}

/// <summary>一个可选尺寸：行列跨度 + 叫法，主档用它展开额外的变体。</summary>
public sealed record WidgetSizeSpec(int Rows, int Cols, string Label) {
    public string Text => $"{Rows}×{Cols}";
}

/// <summary>
/// 市场里的一个格子：一个小组件（家族），带若干可选尺寸。
/// 单尺寸的组件只有一项，尺寸选项仍然显示出来，保证「选大小」这一步位置固定。
/// </summary>
public sealed class WidgetFamily {
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Pinyin { get; init; }

    /// <summary>默认选中的那一档（通常是「标准」）。</summary>
    public required WidgetDef Default { get; init; }

    /// <summary>可选尺寸，按占格从小到大排。</summary>
    public required IReadOnlyList<WidgetDef> Variants { get; init; }

    public string IconPath => Default.IconPath;

    public string Initial => Pinyin.Length == 0 ? "#" : char.ToUpperInvariant(Pinyin[0]).ToString();

    /// <summary>格子提示里的尺寸串，例如 "1×1 / 1×2"。</summary>
    public string SizeHint => string.Join(" / ", Variants.Select(variant => variant.SizeText));
}

public static class WidgetCatalog {
    // 图标路径（Material Icons 24dp 轮廓，直接拉伸使用）
    private const string IconClock =
        "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M12,20 C7.59,20 4,16.41 4,12 C4,7.59 7.59,4 12,4 C16.41,4 20,7.59 20,12 C20,16.41 16.41,20 12,20 Z M12.5,7 L11,7 L11,13 L16,13 L16,11.5 L12.5,11.5 Z";

    private const string IconDate =
        "M19,3 L18,3 L18,1 L16,1 L16,3 L8,3 L8,1 L6,1 L6,3 L5,3 C3.89,3 3.01,3.9 3.01,5 L3,19 C3,20.1 3.89,21 5,21 L19,21 C20.1,21 21,20.1 21,19 L21,5 C21,3.9 20.1,3 19,3 Z M19,19 L5,19 L5,8 L19,8 L19,19 Z";

    private const string IconWeather =
        "M6.76,4.84 L4.96,3.05 L3.55,4.46 L5.34,6.25 Z M1,10.5 L4,10.5 L4,12.5 L1,12.5 Z M11,0.55 L13,0.55 L13,3.5 L11,3.5 Z M19.04,3.05 L20.45,4.46 L18.66,6.25 L17.25,4.84 Z M17.24,18.16 L19.03,19.96 L20.44,18.55 L18.64,16.76 Z M20,10.5 L23,10.5 L23,12.5 L20,12.5 Z M12,5.5 C8.69,5.5 6,8.19 6,11.5 C6,14.81 8.69,17.5 12,17.5 C15.31,17.5 18,14.81 18,11.5 C18,8.19 15.31,5.5 12,5.5 Z M4.46,17.54 L3.05,18.95 L4.84,20.74 L6.25,19.33 Z";

    private const string IconThermo =
        "M15,13 L15,5 C15,3.34 13.66,2 12,2 C10.34,2 9,3.34 9,5 L9,13 C7.79,13.91 7,15.37 7,17 C7,19.76 9.24,22 12,22 C14.76,22 17,19.76 17,17 C17,15.37 16.21,13.91 15,13 Z M11,5 C11,4.45 11.45,4 12,4 C12.55,4 13,4.45 13,5 L13,8 L11,8 Z";

    private const string IconGpu =
        "M15,9 L9,9 L9,15 L15,15 L15,9 Z M13,13 L11,13 L11,11 L13,11 L13,13 Z M21,11 L21,9 L19,9 L19,7 C19,5.89 18.1,5 17,5 L15,5 L15,3 L13,3 L13,5 L11,5 L11,3 L9,3 L9,5 L7,5 C5.89,5 5,5.89 5,7 L5,9 L3,9 L3,11 L5,11 L5,13 L3,13 L3,15 L5,15 L5,17 C5,18.11 5.89,19 7,19 L9,19 L9,21 L11,21 L11,19 L13,19 L13,21 L15,21 L15,19 L17,19 C18.11,19 19,18.11 19,17 L19,15 L21,15 L21,13 L19,13 L19,11 L21,11 Z";

    private const string IconDisk =
        "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z M12,20 C7.59,20 4,16.41 4,12 L8,12 C8,14.21 9.79,16 12,16 C14.21,16 16,14.21 16,12 L20,12 C20,16.41 16.41,20 12,20 Z";

    private const string IconMemory =
        "M21,11 L21,9 L19,9 L19,7 C19,5.9 18.1,5 17,5 L7,5 C5.9,5 5,5.9 5,7 L5,9 L3,9 L3,11 L5,11 L5,13 L3,13 L3,15 L5,15 L5,17 C5,18.1 5.9,19 7,19 L17,19 C18.1,19 19,18.1 19,17 L19,15 L21,15 L21,13 L19,13 L19,11 L21,11 Z";

    private const string IconBattery =
        "M15.67,4 L14,4 L14,2 L10,2 L10,4 L8.33,4 C7.6,4 7,4.6 7,5.33 L7,20.66 C7,21.4 7.6,22 8.33,22 L15.67,22 C16.4,22 17,21.4 17,20.67 L17,5.33 C17,4.6 16.4,4 15.67,4 Z";

    private const string IconBluetooth =
        "M17.71,7.71 L12,2 L11,2 L11,9.59 L6.41,5 L5,6.41 L10.59,12 L5,17.59 L6.41,19 L11,14.41 L11,22 L12,22 L17.71,16.29 L13.41,12 L17.71,7.71 Z M13,5.83 L14.88,7.71 L13,9.59 L13,5.83 Z M14.88,16.29 L13,18.17 L13,14.41 L14.88,16.29 Z";

    private const string IconWifi =
        "M1,9 L3,11 C7.97,6.03 16.03,6.03 21,11 L23,9 C16.93,2.93 7.07,2.93 1,9 Z M9,17 L12,20 L15,17 C13.35,15.34 10.66,15.34 9,17 Z M5,13 L7,15 C9.76,12.24 14.24,12.24 17,15 L19,13 C15.14,9.14 8.86,9.14 5,13 Z";

    private const string IconVolume =
        "M3,9 L3,15 L7,15 L12,20 L12,4 L7,9 L3,9 Z M16.5,12 C16.5,10.23 15.48,8.71 14,7.97 L14,16.02 C15.48,15.29 16.5,13.77 16.5,12 Z M14,3.23 L14,5.29 C16.89,6.15 19,8.83 19,12 C19,15.17 16.89,17.85 14,18.71 L14,20.77 C18.01,19.86 21,16.28 21,12 C21,7.72 18.01,4.14 14,3.23 Z";

    private const string IconBrightness =
        "M12,7 C9.24,7 7,9.24 7,12 C7,14.76 9.24,17 12,17 C14.76,17 17,14.76 17,12 C17,9.24 14.76,7 12,7 Z M2,13 L4,13 C4.55,13 5,12.55 5,12 C5,11.45 4.55,11 4,11 L2,11 C1.45,11 1,11.45 1,12 C1,12.55 1.45,13 2,13 Z M20,13 L22,13 C22.55,13 23,12.55 23,12 C23,11.45 22.55,11 22,11 L20,11 C19.45,11 19,11.45 19,12 C19,12.55 19.45,13 20,13 Z M11,2 L11,4 C11,4.55 11.45,5 12,5 C12.55,5 13,4.55 13,4 L13,2 C13,1.45 12.55,1 12,1 C11.45,1 11,1.45 11,2 Z M11,20 L11,22 C11,22.55 11.45,23 12,23 C12.55,23 13,22.55 13,22 L13,20 C13,19.45 12.55,19 12,19 C11.45,19 11,19.45 11,20 Z M5.99,4.58 C5.6,4.19 4.96,4.19 4.58,4.58 C4.19,4.97 4.19,5.61 4.58,5.99 L5.64,7.05 C6.03,7.44 6.67,7.44 7.05,7.05 C7.44,6.66 7.44,6.02 7.05,5.64 L5.99,4.58 Z M18.36,16.95 C17.97,16.56 17.33,16.56 16.95,16.95 C16.56,17.34 16.56,17.98 16.95,18.36 L18.01,19.42 C18.4,19.81 19.04,19.81 19.42,19.42 C19.81,19.03 19.81,18.39 19.42,18.01 L18.36,16.95 Z M19.42,5.99 C19.81,5.6 19.81,4.96 19.42,4.58 C19.03,4.19 18.39,4.19 18.01,4.58 L16.95,5.64 C16.56,6.03 16.56,6.67 16.95,7.05 C17.34,7.44 17.98,7.44 18.36,7.05 L19.42,5.99 Z M7.05,18.36 C7.44,17.97 7.44,17.33 7.05,16.95 C6.66,16.56 6.02,16.56 5.64,16.95 L4.58,18.01 C4.19,18.4 4.19,19.04 4.58,19.42 C4.97,19.81 5.61,19.81 5.99,19.42 L7.05,18.36 Z";

    private const string IconMute =
        "M16.5,12 C16.5,10.23 15.48,8.71 14,7.97 L14,10.18 L16.45,12.63 C16.48,12.43 16.5,12.22 16.5,12 Z M19,12 C19,12.94 18.8,13.82 18.46,14.64 L19.97,16.15 C20.63,14.91 21,13.5 21,12 C21,7.72 18.01,4.14 14,3.23 L14,5.29 C16.89,6.15 19,8.83 19,12 Z M4.27,3 L3,4.27 L7.73,9 L3,9 L3,15 L7,15 L12,20 L12,13.27 L16.25,17.52 C15.58,18.04 14.83,18.45 14,18.7 L14,20.76 C15.38,20.45 16.63,19.81 17.69,18.95 L19.73,21 L21,19.73 L4.27,3 Z M12,4 L9.91,6.09 L12,8.18 L12,4 Z";

    private const string IconLock =
        "M18,8 L17,8 L17,6 C17,3.24 14.76,1 12,1 C9.24,1 7,3.24 7,6 L7,8 L6,8 C4.9,8 4,8.9 4,10 L4,20 C4,21.1 4.9,22 6,22 L18,22 C19.1,22 20,21.1 20,20 L20,10 C20,8.9 19.1,8 18,8 Z M12,17 C10.9,17 10,16.1 10,15 C10,13.9 10.9,13 12,13 C13.1,13 14,13.9 14,15 C14,16.1 13.1,17 12,17 Z M15.1,8 L8.9,8 L8.9,6 C8.9,4.29 10.29,2.9 12,2.9 C13.71,2.9 15.1,4.29 15.1,6 L15.1,8 Z";

    private const string IconTodo =
        "M22,5.18 L10.59,16.6 L6.35,12.36 L7.76,10.95 L10.59,13.78 L20.59,3.76 L22,5.18 Z M19.85,10.85 L19.85,16.5 C19.85,18.1 18.55,19.4 16.95,19.4 L5.35,19.4 C3.75,19.4 2.45,18.1 2.45,16.5 L2.45,4.9 C2.45,3.3 3.75,2 5.35,2 L14.75,2 L14.75,4 L5.35,4 L5.35,16.5 L16.95,16.5 L16.95,10.85 L19.85,10.85 Z";

    private const string IconRecent =
        "M10,4 L4,4 C2.9,4 2.01,4.9 2.01,6 L2,18 C2,19.1 2.9,20 4,20 L20,20 C21.1,20 22,19.1 22,18 L22,8 C22,6.9 21.1,6 20,6 L12,6 L10,4 Z";

    private const string IconKeyboard =
        "M20,5 L4,5 C2.9,5 2.01,5.9 2.01,7 L2,17 C2,18.1 2.9,19 4,19 L20,19 C21.1,19 22,18.1 22,17 L22,7 C22,5.9 21.1,5 20,5 Z M11,8 L13,8 L13,10 L11,10 L11,8 Z M11,11 L13,11 L13,13 L11,13 L11,11 Z M8,8 L10,8 L10,10 L8,10 L8,8 Z M8,11 L10,11 L10,13 L8,13 L8,11 Z M7,13 L5,13 L5,11 L7,11 L7,13 Z M7,10 L5,10 L5,8 L7,8 L7,10 Z M16,17 L8,17 L8,15 L16,15 L16,17 Z M16,13 L14,13 L14,11 L16,11 L16,13 Z M16,10 L14,10 L14,8 L16,8 L16,10 Z M19,13 L17,13 L17,11 L19,11 L19,13 Z M19,10 L17,10 L17,8 L19,8 L19,10 Z";

    private const string IconScreenshot =
        "M21,19 L21,5 C21,3.9 20.1,3 19,3 L5,3 C3.9,3 3,3.9 3,5 L3,19 C3,20.1 3.9,21 5,21 L19,21 C20.1,21 21,20.1 21,19 Z M8.5,13.5 L11,16.51 L14.5,12 L19,18 L5,18 L8.5,13.5 Z";

    private const string IconSchedule =
        "M17,12 L12,12 L12,17 L17,17 L17,12 Z M16,2 L16,1 L14,1 L14,2 L10,2 L10,1 L8,1 L8,2 L7,2 C5.89,2 5.01,2.9 5.01,4 L5,20 C5,21.1 5.89,22 7,22 L21,22 C22.1,22 23,21.1 23,20 L23,4 C23,2.9 22.1,2 21,2 L16,2 Z M21,20 L7,20 L7,8 L21,8 L21,20 Z M3,17 L1,17 L1,4 C1,2.9 1.9,2 3,2 L18,2 L18,4 L3,4 L3,17 Z";

    private const string IconCalendar =
        "M19,3 L18,3 L18,1 L16,1 L16,3 L8,3 L8,1 L6,1 L6,3 L5,3 C3.89,3 3.01,3.9 3.01,5 L3,19 C3,20.1 3.89,21 5,21 L19,21 C20.1,21 21,20.1 21,19 L21,5 C21,3.9 20.1,3 19,3 Z M19,19 L5,19 L5,8 L19,8 L19,19 Z M16,11 L12,11 L12,15 L16,15 L16,11 Z";

    /// <summary>
    /// 每个组件的主档定义。带 <see cref="WidgetDef.Sizes"/> 的会被展开成多个尺寸变体；
    /// 同一 <see cref="WidgetDef.Family"/> 的条目在市场里共用一个格子。
    /// </summary>
    private static readonly List<WidgetDef> Definitions = new() {
        new() {
            Id = "clock", Name = "时间", Pinyin = "shijian", Rows = 1, Cols = 2,
            Family = "clock", SizeLabel = "标准",
            Sizes = new[] { S(2, 2, "大号"), S(1, 6, "宽条") },
            Description = "时钟，带星期、日期与秒进度",
            IconPath = IconClock
        },
        new() {
            Id = "clock-mini", Name = "时间 · 迷你", Pinyin = "shijianmini", Rows = 1, Cols = 1,
            Family = "clock", SizeLabel = "迷你",
            Description = "迷你时钟，时间与星期",
            IconPath = IconClock
        },
        new() {
            Id = "date", Name = "日期", Pinyin = "riqi", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "今天几号、星期几",
            IconPath = IconDate
        },
        new() {
            Id = "weather", Name = "天气", Pinyin = "tianqi", Rows = 2, Cols = 2,
            Sizes = new[] { S(1, 2, "紧凑"), S(1, 6, "宽条") },
            Description = "当前天气、温度与湿度",
            IconPath = IconWeather
        },
        new() {
            Id = "cpu-temp", Name = "CPU 温度", Pinyin = "cpuwendu", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "处理器温度（ACPI 热区）",
            IconPath = IconThermo
        },
        new() {
            Id = "gpu-temp", Name = "GPU 温度", Pinyin = "gpuwendu", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "显卡温度（NVIDIA NVML）",
            IconPath = IconThermo
        },
        new() {
            Id = "disk-temp", Name = "硬盘温度", Pinyin = "yingpanwendu", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "硬盘温度（NVMe / SATA，需管理员）",
            IconPath = IconThermo
        },
        new() {
            Id = "mem-temp", Name = "内存温度", Pinyin = "neicunwendu", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "内存温度（多数主板无此传感器）",
            IconPath = IconThermo
        },
        new() {
            Id = "cpu-usage", Name = "CPU 占用率", Pinyin = "cpuzhanyonglv", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "实时处理器占用",
            IconPath = IconGpu
        },
        new() {
            Id = "gpu-usage", Name = "GPU 占用率", Pinyin = "gpuzhanyonglv", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "实时显卡占用",
            IconPath = IconGpu
        },
        new() {
            Id = "disk-usage", Name = "硬盘占用率", Pinyin = "yingpanzhanyonglv", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "系统盘已用空间",
            IconPath = IconDisk
        },
        new() {
            Id = "mem-usage", Name = "内存占用率", Pinyin = "neicunzhanyonglv", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "物理内存使用情况",
            IconPath = IconMemory
        },
        new() {
            Id = "battery", Name = "电量显示", Pinyin = "dianliangxianshi", Rows = 1, Cols = 2,
            Sizes = new[] { S(1, 1, "迷你"), S(1, 6, "宽条") },
            Description = "电池余量、充电状态",
            IconPath = IconBattery
        },
        new() {
            Id = "bluetooth", Name = "蓝牙连接状态", Pinyin = "lanyalianjiezhuangtai", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "蓝牙开关与已配对设备",
            IconPath = IconBluetooth
        },
        new() {
            Id = "wifi", Name = "WiFi 连接状态", Pinyin = "wifi", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "无线网络连接状态",
            IconPath = IconWifi
        },
        new() {
            Id = "volume", Name = "音量滑块", Pinyin = "yinlianghuakuai", Rows = 1, Cols = 6,
            Sizes = new[] { S(1, 2, "紧凑"), S(1, 3, "短条"), S(2, 6, "大号") },
            Description = "横向拖动调节系统音量",
            IconPath = IconVolume
        },
        new() {
            Id = "brightness", Name = "亮度滑块", Pinyin = "liangduhuakuai", Rows = 1, Cols = 6,
            Sizes = new[] { S(1, 2, "紧凑"), S(1, 3, "短条"), S(2, 6, "大号") },
            Description = "横向拖动调节屏幕亮度",
            IconPath = IconBrightness
        },
        new() {
            Id = "mute", Name = "静音切换", Pinyin = "jingyinqiehuan", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "一键静音 / 取消静音", IconPath = IconMute, IsAction = true
        },
        new() {
            Id = "lock", Name = "一键锁定", Pinyin = "yijiansuoding", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "立即锁定此电脑", IconPath = IconLock, IsAction = true
        },
        new() {
            Id = "keyboard", Name = "触控键盘", Pinyin = "chukongjianpan", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "打开系统触摸键盘", IconPath = IconKeyboard, IsAction = true
        },
        new() {
            Id = "screenshot", Name = "系统截图", Pinyin = "xitongjietu", Rows = 1, Cols = 1,
            Sizes = new[] { S(1, 2, "横向"), S(2, 2, "大号") },
            Description = "调用系统截图工具", IconPath = IconScreenshot, IsAction = true
        },
        new() {
            Id = "schedule", Name = "日程显示", Pinyin = "richengxianshi", Rows = 1, Cols = 6,
            Family = "schedule", SizeLabel = "条状",
            Sizes = new[] { S(2, 6, "半页") },
            Description = "接下来要做的日程", IconPath = IconSchedule
        },
        new() {
            Id = "todo-bar", Name = "待办显示", Pinyin = "daibanxianshi", Rows = 1, Cols = 6,
            Family = "todo", SizeLabel = "条状",
            Sizes = new[] { S(2, 6, "半页") },
            Description = "未完成的待办，可勾选", IconPath = IconTodo
        },
        new() {
            Id = "recent", Name = "最近文件", Pinyin = "zuijinwenjian", Rows = 1, Cols = 6,
            Sizes = new[] { S(2, 6, "半页"), S(3, 6, "整页") },
            Description = "最近打开过的文件，点击即开",
            IconPath = IconRecent
        },
        new() {
            Id = "todo-board", Name = "待办事项", Pinyin = "daibanshixiang", Rows = 3, Cols = 6,
            Family = "todo", SizeLabel = "整页",
            Description = "待办清单，可增删勾选", IconPath = IconTodo
        },
        new() {
            Id = "calendar", Name = "日程", Pinyin = "richeng", Rows = 3, Cols = 6,
            Family = "schedule", SizeLabel = "整页",
            Description = "月历，可为选中日期添加日程", IconPath = IconCalendar
        }
    };

    // 内置定义 + 用户导入的组件，合在一起才是市场看到的全部内容。
    // 用户装/卸组件后调 Refresh() 重建这三份数据。
    private static List<WidgetDef> _all = new();
    private static Dictionary<string, WidgetDef> _byId = new();
    private static List<WidgetFamily> _families = new();

    static WidgetCatalog() {
        // 目录第一次被用到时就把 widgets 目录里的自定义组件读进来，
        // 这样调用方不用关心初始化顺序。
        Refresh();
    }

    /// <summary>全部尺寸变体（主档 + 展开出来的尺寸 + 自定义组件），工厂与市场都按它取定义。</summary>
    public static IReadOnlyList<WidgetDef> All => _all;

    /// <summary>重新扫描自定义组件并重建索引。装完 / 卸掉组件后调用。</summary>
    public static void Refresh() {
        WidgetExtensions.Load();

        _all = Expand(Definitions)
            .Concat(WidgetExtensions.Defs)
            .OrderBy(def => def.Pinyin, StringComparer.Ordinal)
            .ThenBy(def => def.Rows * def.Cols)
            .ThenBy(def => def.Cols)
            .ToList();

        _byId = _all.GroupBy(def => def.Id)
            .ToDictionary(group => group.Key, group => group.First());

        _families = BuildFamilies();
    }

    /// <summary>
    /// 内置图标的路径。自定义组件的清单里用 "iconPreset": "wifi" 就能复用这些图标，
    /// 不用自己画矢量路径。
    /// </summary>
    public static string? PresetIcon(string key) => key.ToLowerInvariant() switch {
        "clock" or "time" => IconClock,
        "date" or "calendar-day" => IconDate,
        "weather" => IconWeather,
        "thermo" or "temperature" or "temp" => IconThermo,
        "gpu" => IconGpu,
        "cpu" or "processor" => IconGpu,
        "disk" or "storage" => IconDisk,
        "memory" or "ram" => IconMemory,
        "battery" or "power" => IconBattery,
        "bluetooth" => IconBluetooth,
        "wifi" or "network" => IconWifi,
        "volume" or "audio" => IconVolume,
        "brightness" => IconBrightness,
        "mute" => IconMute,
        "lock" => IconLock,
        "todo" or "check" => IconTodo,
        "recent" or "files" => IconRecent,
        "keyboard" => IconKeyboard,
        "screenshot" or "camera" => IconScreenshot,
        "schedule" => IconSchedule,
        "calendar" => IconCalendar,
        _ => null
    };

    /// <summary>把每个主档的额外尺寸展开成同内容、不同跨度的变体。</summary>
    private static List<WidgetDef> Expand(List<WidgetDef> definitions) {
        var all = new List<WidgetDef>();

        foreach (var def in definitions) {
            all.Add(def);

            foreach (var size in def.Sizes)
                all.Add(Clone(def, size));
        }

        return all
            .OrderBy(def => def.Pinyin, StringComparer.Ordinal)
            .ThenBy(def => def.Rows * def.Cols)
            .ThenBy(def => def.Cols)
            .ToList();
    }

    private static WidgetDef Clone(WidgetDef source, WidgetSizeSpec size) =>
        new() {
            Id = $"{source.Id}-{size.Rows}x{size.Cols}",
            Name = source.Name,
            Pinyin = source.Pinyin,
            Rows = size.Rows,
            Cols = size.Cols,
            Family = source.FamilyKey,
            SizeLabel = size.Label,
            Description = source.Description,
            IconPath = source.IconPath,
            IsAction = source.IsAction
        };

    /// <summary>注册一个额外尺寸，写法短一点。</summary>
    private static WidgetSizeSpec S(int rows, int cols, string label) => new(rows, cols, label);

    /// <summary>
    /// 市场网格里的格子：按家族归并后的小组件，顺序沿用拼音 A→Z。
    /// </summary>
    public static IReadOnlyList<WidgetFamily> Families => _families;

    private static List<WidgetFamily> BuildFamilies() {
        var families = new List<WidgetFamily>();

        foreach (var group in _all.GroupBy(def => def.FamilyKey)) {
            // 尺寸按占格从小到大排，二级菜单里读起来是「小 → 大」
            var variants = group
                .OrderBy(def => def.Rows * def.Cols)
                .ThenBy(def => def.Cols)
                .ToList();

            var primary = variants.FirstOrDefault(def => def.SizeLabel == "标准")
                          ?? variants.OrderByDescending(def => def.Rows * def.Cols).First();

            families.Add(new WidgetFamily {
                Key = group.Key,
                // 格子标题用主档的名字（时间 / 待办事项 / 日程 …）
                Name = primary.Name,
                Pinyin = primary.Pinyin,
                Default = primary,
                Variants = variants
            });
        }

        return families
            .OrderBy(family => family.Pinyin, StringComparer.Ordinal)
            .ToList();
    }

    public static WidgetDef? Find(string id) =>
        _byId.TryGetValue(id, out var def) ? def : null;

    public static bool IsAction(string widgetId) => Find(widgetId)?.IsAction == true;

    /// <summary>按首字母分组，字母 A→Z。</summary>
    public static List<IGrouping<string, WidgetDef>> GroupedByInitial() =>
        _all.GroupBy(def => def.Initial)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
}
