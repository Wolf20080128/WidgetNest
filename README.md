# WidgetNest

> 右下角随手唤出的桌面小组件面板 —— 一个键，把常用信息和开关全叫到眼前。

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4.svg)](#环境要求)

WidgetNest 是一个常驻托盘的 Windows 桌面小组件面板。按 `Ctrl + Shift + A`，面板从屏幕右下角滑出：一块 **3 行 × 6 列** 的网格，放时间、天气、CPU 温度、音量、待办……想放什么自己拖。

它不做「全屏仪表盘」那种重活，只解决一件事：**不打断当前工作，一秒钟看到想看的那几个数字。**

---

## 功能

**面板**

- 全局热键唤起 / 收起，默认 `Ctrl + Shift + A`，可在偏好设置里重录
- 多页网格，底部页码提示，触控板拖拽时上下边缘有停留区自动翻页
- 自由拖拽排布、自由缩放尺寸（1×1 到整页 3×6）
- 相邻两块可以「组合」成视觉上连在一起的一整块，随时拆开
- 编辑模式、空态引导、深浅色圆角描边等细节按系统主题走

**内置组件（27 个）**

| 类别 | 组件 |
| --- | --- |
| 时间 | 时间、时间 · 迷你、日期、日程、日历、待办事项、待办显示 |
| 环境 | 天气（按省市自动定位，数据来自 [wttr.in](https://wttr.in/)） |
| 硬件 | CPU 温度、GPU 温度、硬盘温度、内存温度、CPU 占用率、GPU 占用率、硬盘占用率、内存占用率 |
| 状态 | 电量显示、蓝牙连接状态、WiFi 连接状态 |
| 系统开关 | 音量滑块、亮度滑块、静音切换、一键锁定、触控键盘、系统截图 |
| 其他 | 最近文件 |

温度、占用率走 WMI（`root\WMI`）与性能计数器；音量和亮度直接调系统接口，不走模拟按键。

**扩展**

- **声明式自定义组件**：一个目录 + 一个 `widget.json`，不编译、不写 C#，拖进面板即可用
- 数据源支持 `static` / `clock` / `command` / `file`，命令在后台线程跑，有超时保护，取不到值显示 `—`
- 版式支持文本、进度条、图标、横向分栏、占位空白；文本可插占位符，`{time}` `{date}` `{weekday}` 等内置
- 组件市场按拼音搜索，每种组件支持多档尺寸
- 详见 [自定义组件开发说明](docs/widget-extension.md)

**程序本身**

- 单实例：重复启动只会把已有面板叫出来
- 开机自启（启动项路径失效时自动修复）
- 托盘常驻，可「以管理员身份重启」（读写部分硬件信息需要）
- 本机账号系统：多账号、头像、签到、等级，密码只存 PBKDF2 派生值与随机盐，**不存明文、不上传**
- 所有数据都在 `%LOCALAPPDATA%\WidgetNest\`，没有云端账号、没有遥测

---

## 环境要求

- Windows 10 及以上（x64）
- 开发需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)
- 打包安装程序需要 [Inno Setup 6](https://jrsoftware.org/isinfo.php)（可选）

## 构建与运行

```bash
# 调试运行
dotnet run

# 编译
dotnet build -c Release

# 发布为单文件、自包含 exe（产物在 publish/win-x64/WidgetNest.exe）
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

也可以在 Visual Studio / Rider 里直接打开 `WidgetNest.slnx`。

### 打安装包

```bash
ISCC.exe installer\WidgetNest.iss
```

产物为 `publish/WidgetNest-Setup-1.0.0.exe`，默认按当前用户安装到 `%LocalAppData%\Programs`，**不需要管理员权限**。

---

## 目录结构

```
WidgetNest/
├─ App.xaml(.cs)            应用入口：单实例、托盘、热键、主题
├─ MainWindow.xaml(.cs)     面板外壳：导航、页面切换、拖动与贴边
├─ HomePage.xaml(.cs)       主页网格：拖拽排布、组合、多页
├─ WidgetMarketView         组件市场
├─ AddAppPage               添加应用 / 导入自定义组件
├─ PreferencesPage          偏好设置
├─ ProfilePage              本机账号与签到
├─ LocationWindow           省市区三级定位选择
├─ WidgetCatalog.cs         内置组件清单与默认版式
├─ WidgetViews.cs           各类组件的渲染与实时取值
├─ WidgetRuntime.cs         自定义组件的 JSON 解析、数据源调度
├─ WidgetExtensions.cs      组件导入 / 导出 / 内置示例
├─ WidgetBoards.cs          待办、日程等看板型组件
├─ WidgetDataStore.cs       组件数据落盘
├─ SystemMetrics.cs         WMI / 性能计数器取硬件数据
├─ ChinaRegions.json        省市区数据（内嵌资源）
├─ Theme.xaml, ThemeManager.cs
├─ installer/               Inno Setup 脚本
├─ tools/IconGen            图标生成小工具（独立项目）
└─ docs/widget-extension.md 自定义组件开发说明
```

---

## 安全提示

自定义组件**可以执行任意命令**（`data.*.run` 与 `action.run` 都通过 `cmd.exe` 执行）。导入陌生组件前，请确认清单里的命令是你能接受的——导入确认窗会把要执行的命令逐条列出来。

组件市场里的官方内置组件不执行任何用户提供的命令；只有你手动导入的自定义组件才会。

## 参与贡献

欢迎提 Issue 和 PR。

- Bug / 功能建议请开 Issue，附上系统版本和复现步骤
- 提交 PR 前请确认 `dotnet build -c Release` 能过
- 新增自定义组件的字段请同步更新 `docs/widget-extension.md`

## 许可证

[MIT](LICENSE)
