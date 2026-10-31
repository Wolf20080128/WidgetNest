# 内置组件开发接口（C#）

这份文档面向**改代码加内置组件**的场景：需要自己取数、自己画界面，跟着版本一起发布。

如果你只是想拼一个「读命令 / 读文件 / 显示时间」的组件，不用写 C#，用 [自定义组件接口](widget-extension.md) 的 `widget.json` 就够了。

---

## 一、整体结构

```
WidgetCatalog.Definitions        声明式清单：id、名字、拼音、尺寸、图标、说明
        ↓ Expand()             把 sizes 展开成多个尺寸变体
WidgetCatalog.All               运行期总目录（内置 + 自定义组件），市场与面板都从这里取
        ↓ AppItem               面板上一个格子记录：widget id + 行/列跨度
WidgetFactory.Create(def, box)  工厂：按家族 + 实际格数挑一个视图
        ↓
WidgetView                      视图：自己的刷新间隔与 Update 逻辑
        ↓
WidgetRuntime                   每 400ms 心跳，按各视图的 IntervalMs 分发 Tick
```

| 类型 | 可见性 | 职责 |
|---|---|---|
| `WidgetDef` | public | 一个尺寸变体的定义（id、名字、行列、图标、是否动作块） |
| `WidgetFamily` | public | 市场里的一个格子（同家族的多档尺寸归并） |
| `WidgetCatalog` | public static | 注册表、图标库、按 id / 首字母查询 |
| `WidgetFactory` | internal static | `Create(def, box)` 返回视图，返回 null 表示这个 id 没有视图 |
| `WidgetView` | internal abstract | 视图基类，`Update()` 是唯一必须实现的方法 |
| `WidgetRuntime` | internal static | 心跳调度、编辑模式标记 |
| `WidgetBox` | internal | 当前格数，带 `Narrow` / `Wide` / `Tall` / `Scale` 判断 |
| `W` | internal static | 文字、图标、卡片、进度条等通用视觉工具 |

---

## 二、加一个内置组件：三步

### 第 1 步：在 `WidgetCatalog.Definitions` 里注册

`WidgetCatalog.cs` 里的 `Definitions` 列表就是组件清单，按需追加一条：

```csharp
new() {
    Id = "disk-free", Name = "磁盘剩余", Pinyin = "cipanshengyu",
    Rows = 1, Cols = 2,
    Family = "disk-free", SizeLabel = "标准",
    Description = "磁盘可用空间",
    IconPath = IconDisk,
    // 想要的额外尺寸：写在这里，会被自动展开成同内容、不同跨度的变体
    Sizes = new[] { S(1, 1, "迷你"), S(2, 2, "大号") }
},
```

| 字段 | 必填 | 说明 |
|---|---|---|
| `Id` | ✓ | 全局唯一；展开出来的变体会自动加 `-{行}x{列}` 后缀（如 `disk-free-2x2`） |
| `Name` | ✓ | 市场与格子里的显示名 |
| `Pinyin` | ✓ | 全拼小写，市场 A→Z 排序用 |
| `Rows` / `Cols` | ✓ | 主档占格，每页 3 行 × 6 列 |
| `Description` | ✓ | 市场二级菜单里的说明 |
| `IconPath` | ✓ | 24×24 viewBox 的 SVG path（用本文件里的 `Icon*` 常量即可） |
| `Family` |  | 家族键，留空等于用 `Id`。同家族的尺寸在市场里共用一个格子 |
| `SizeLabel` |  | 这一档的叫法，默认「标准」；**叫「标准」的那档是家族主档** |
| `Sizes` |  | 额外尺寸，`S(rows, cols, label)` 是简写 |
| `IsAction` |  | 整块当按钮：按下不进入拖拽，点击交给控件自己 |
| `IsUserWidget` |  | 只有自定义组件才为 true，内置组件不要动 |

### 第 2 步：在 `WidgetFactory.Create` 里加分支

`WidgetViews.cs` 末尾的工厂按 **家族键** 分派：

```csharp
case "disk-free":
    return new DiskFreeView(box);
```

- 只是复用现成视图（例如换个取数函数），一行就够：`new MetricView("磁盘", def.IconPath, Read, true, box)`
- 同一家族想按尺寸换版式，用 `box.Tall` / `box.Wide` 判断，参考 `schedule` / `todo` 的分支

### 第 3 步：写视图

```csharp
internal sealed class DiskFreeView : WidgetView {
    private readonly TextBlock _value;
    private readonly MiniBar _bar;

    public DiskFreeView(WidgetBox box) {
        _value = W.Text("—", 15 * box.Scale, W.TextBrush, FontWeights.SemiBold);
        _bar = new MiniBar(3 * Math.Min(box.Scale, 1.5));

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(_value);
        Grid.SetRow(_bar, 1);
        grid.Children.Add(_bar);

        Root = W.Pad(grid);      // 必须给 Root 赋值，面板取它当内容
    }

    // 磁盘容量变化很慢，5 秒一次就够（默认 1000ms）
    public override int IntervalMs => 5000;

    public override void Update() {
        var (used, total, percent) = SystemMetrics.Disk();

        _value.Text = $"{total - used:0.0} GB";
        _bar.SetValue(percent, W.Severity(percent));
    }
}
```

编译运行即可：市场里会多出「磁盘剩余」，主档 1×2，另有 1×1 与 2×2 两档。

---

## 三、`WidgetView` 契约

```csharp
internal abstract class WidgetView {
    public FrameworkElement Root { get; protected set; }   // 面板显示的内容，必须赋值
    public virtual int IntervalMs => 1000;                 // 刷新间隔（毫秒）
    public bool IsActive { get; set; }                     // 只有当前页的视图是激活的
    public abstract void Update();                         // 刷新内容
    public virtual void Dispose() { }                      // 释放订阅 / 取消任务
}
```

调用方的行为：

- `WidgetRuntime` 每 **400ms** 心跳一次，对 `IsActive` 且距上次刷新超过 `IntervalMs` 的视图调 `Update()`
- `Update()` 在 **UI 线程**执行，异常会被吞掉（单个组件出错不影响面板），但**阻塞就会卡面板**——耗时取数请自己缓存或放后台线程
- 翻页时旧页的视图被 `DeactivateAll()` 置为不激活，**不会销毁**；格子被删除时才 `Untrack()` → `Dispose()`
- 视图是**按面板格子缓存**的，同一个组件多放几块就是多个实例，不要用实例字段存全局状态

---

## 四、`WidgetBox`：按格数换版式

```csharp
internal readonly record struct WidgetBox(int Rows, int Cols) {
    bool Narrow => Cols == 1;    // 单列：标题在上、数值在下
    bool Wide   => Cols >= 3;    // 三列以上：一行横排
    bool Tall   => Rows >= 2;    // 两行以上：字号可以放大
    int  Area   => Rows * Cols;  // 占格总数
    double Scale => Rows >= 3 ? 1.7 : Rows == 2 ? 1.35 : 1;   // 字号倍率
}
```

惯例写法：`字号 = 基准 * box.Scale`，再用 `Math.Min(box.Scale, 1.5)` 给图标封顶，避免大尺寸里图标过大。

---

## 五、`W`：视觉工具

| 成员 | 用途 |
|---|---|
| `TextBrush` / `DimBrush` / `FaintBrush` | 正文 / 次要 / 辅助文字画刷（随主题自动变色，**不要另建**） |
| `AccentBrush` / `WarnBrush` / `DangerBrush` / `ChipBrush` | 强调 / 警示 / 危险 / 胶囊底色 |
| `Severity(percent)` | 按数值取色：≥85% 红、≥65% 黄，其余正文色 |
| `Ink(alpha)` | 「半透明白」在当前主题下的等价色（浅色主题自动换成半透明黑） |
| `Frozen(color)` | 冻结画刷，给不会变色的装饰用 |
| `Text(text, size, brush?, weight?)` | 建一个带省略号的 TextBlock |
| `Icon(svgPath, size, brush?)` | 建一个矢量图标 |
| `Pad(content, l=9, t=7, r=9, b=7)` | 内容卡片的内边距容器 |
| `Chip(content)` | 圆角小胶囊容器 |
| `Interactive(element)` | 标记「按下别拖格子」，自绘控件（滑块、勾选圈）必须调 |
| `InteractiveTag` | 上面那个标记的字符串值，`HomePage` 据此判断 |

画刷是共享实例，由 `ThemeManager` 在主题切换时统一改色——所以**直接在 `Update()` 里改文字内容就行，主题变化会自动跟上**。

其他可复用控件：

| 控件 | 说明 |
|---|---|
| `MiniBar(height, brush?)` | 细进度条，`SetValue(percent, brush?)` 更新 |
| `SlideBar` | 可拖动滑块，`Value` / `ValueChanged` / `LastUserInput` |
| `BoardUi` | 看板类（待办、日历）的行、勾选圈、空态等构件 |

---

## 六、系统数据 `SystemMetrics`

取数统一走这里，别在视图里直接 P/Invoke。

| 方法 | 返回 | 说明 |
|---|---|---|
| `CpuUsage()` | `double` | CPU 占用率 % |
| `Memory()` | `(UsedGb, TotalGb, Percent)` | 物理内存 |
| `Disk()` | `(UsedGb, TotalGb, Percent)` | 系统盘 |
| `Battery()` | `(Percent, Charging)?` | 无电池返回 null |
| `CpuTemperature(out note)` | `double?` | 需要管理员权限 / 传感器支持，`note` 说明取不到的原因 |
| `GpuTemperature(out note)` / `GpuUsage()` / `GpuName()` | `double?` / `string` | 走 NVML，仅 NVIDIA |
| `DiskTemperature(out note)` | `double?` | 走 SMART |
| `Volume()` / `IsMuted()` / `SetVolume(p)` / `ToggleMute()` | — | 系统音量 |
| `Brightness()` / `SetBrightness(p)` | — | 屏幕亮度 |
| `Wifi()` | `(Connected, Name)` | 当前 WiFi |
| `Bluetooth()` | `(Available, Paired)` | 蓝牙状态 |
| `RecentFiles()` | `List<(Name, Path)>` | 最近文件 |

取不到值时**返回 null 并给一句 `note`**，视图照常显示 `—`，不要抛异常。

系统级动作在 `NativeActions`：`LockWorkstation()`、`OpenTouchKeyboard()`、`StartScreenshot()`。

---

## 七、持久化 `WidgetDataStore`

待办、日程这类要存数据的组件走这里，落盘在 `%LOCALAPPDATA%\WidgetNest\widgets.json`。

| 成员 | 说明 |
|---|---|
| `Current` / `Load()` / `Save(data)` | 整个 `WidgetData` 的读写 |
| `Weather` / `WeatherCity` | 天气组件选定的位置 |
| `AddTodo(text)` / `ToggleTodo(id)` / `RemoveTodo(id)` / `UndoneTodos(max)` | 待办 |
| `AddEvent(date, title, time)` / `RemoveEvent(id)` / `EventsOn(date)` / `Upcoming(max)` | 日程 |

数据结构 `TodoItem` / `ScheduleEvent` 都是 public，可以直接绑定。

> 只给自己的组件存少量状态时，更轻的做法是写到 `AppStorage.RootDir` 下的独立 json，别挤进 `WidgetData`。

---

## 八、图标

- 一律 **24×24 viewBox** 的 SVG path，直接塞进 `Geometry.Parse`
- 内置图标常量都在 `WidgetCatalog.cs` 顶部（`IconClock`、`IconDisk` …），自定义组件通过 `WidgetCatalog.PresetIcon("disk")` 复用
- 新增一个通用图标时，建议同时加进 `PresetIcon` 的 switch，这样 `widget.json` 也能用

---

## 九、换行、截断与像素

- 格子实际约 **61 × 63 像素**，`W.Text` 默认单行 + 省略号截断
- 需要多行时自己设 `TextWrapping` 与 `MaxHeight`
- 1×1 里放不下两行文字，别硬塞；用 `box.Narrow` 提前分支

---

## 十、常见坑

| 现象 | 原因 |
|---|---|
| 面板一片空白，市场里有这个组件 | `Create` 没返回视图，或视图没给 `Root` 赋值 |
| 组件不刷新 | `IntervalMs` 写成 0 / 负数，或 `Update()` 里抛异常被吞了 |
| 面板整体卡顿 | `Update()` 里做了同步阻塞调用（WMI、网络、进程等待），改缓存或后台线程 |
| 拖不动格子 | 自绘控件没调 `W.Interactive(...)`，被当成格子本体 |
| 深色正常、浅色发白 | 自己 `new SolidColorBrush(Colors.White)` 了，改用 `W.TextBrush` / `W.Ink()` |
| 编辑模式下误触发动作 | 动作类视图要判断 `WidgetRuntime.EditMode` |
| 主题切换后颜色不更新 | 画刷被 `Freeze()` 了，或用了自带颜色而不是 `W` 的共享画刷 |

---

## 十一、提交前自检

- `dotnet build -c Release` 零警告零错误
- 至少验证三档尺寸：1×1、1×2、2×2（字号、截断、图标比例）
- 浅色 / 深色主题各看一遍
- 数据取不到（拔网线、关 WiFi、无独显）时显示 `—` 而不是崩
- 新增的用户可见文案用中文，和其他组件保持一致
