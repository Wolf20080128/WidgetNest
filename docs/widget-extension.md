# 自定义小组件开发说明

WidgetNest 的小组件是**声明式**的：一个目录 + 一个 `widget.json`，不需要编译，也不用写 C#。
写好后把目录（或单个 `.json` 文件）拖进「添加 → 自定义组件」的虚线框，确认后就会出现在小组件市场。

```
我的网速/
  ├─ widget.json      ← 必需
  └─ update.ps1       ← 可选，命令里可以引用（工作目录就是组件目录）
```

安装后的位置：`%LOCALAPPDATA%\WidgetNest\widgets\<id>\`。
市场里右键自定义组件的格子可以「打开组件文件夹」或「移除组件」。

---

## 一、最小可用清单

```json
{
  "schema": "widgetnest.widget/1",
  "id": "my-clock",
  "name": "我的时间",
  "iconPreset": "clock",
  "sizes": [{ "rows": 1, "cols": 2, "label": "标准" }],
  "layout": {
    "default": {
      "rows": [
        { "type": "text", "text": "{time}", "size": 16, "weight": "semibold", "align": "center" }
      ]
    }
  }
}
```

放进面板就是一格 1×2 的时间。`{time}` 是内置占位符，不用声明数据源。

---

## 二、字段说明

| 字段 | 必填 | 说明 |
|---|---|---|
| `schema` | 是 | 固定 `widgetnest.widget/1` |
| `id` | 是 | 唯一标识，字母/数字/`-`/`_`，导入后作为目录名；重复导入会覆盖同名组件 |
| `name` | 是 | 市场里显示的名字，超过 24 字截断 |
| `description` | 否 | 二级菜单里的说明文字 |
| `pinyin` | 否 | 市场排序用，不写就用 `id` |
| `author` / `version` | 否 | 导入确认窗里显示来源 |
| `icon` | 否 | SVG path 的 `d` 属性，24×24 视口，例如 `"M3,3 L10,3 ..."` |
| `iconPreset` | 否 | 复用内置图标，见下方列表；与 `icon` 二选一，`icon` 优先 |
| `refresh` | 否 | 整体刷新间隔（毫秒），默认 2000，范围 500–600000 |
| `sizes` | 否 | 支持的尺寸，不写默认一档 `1×2` |
| `data` | 否 | 数据源，键名即布局里的占位符名 |
| `layout` | 是 | 版式，键为 `1x2` 这类尺寸串，或用 `default` 兜底 |
| `action` | 否 | `{ "run": "命令" }`，点击整块时执行（编辑模式下不触发） |

内置图标：`clock` `date` `weather` `thermo` `cpu` `gpu` `disk` `memory` `battery` `bluetooth` `wifi`
`volume` `brightness` `mute` `lock` `todo` `recent` `keyboard` `screenshot` `schedule` `calendar`。

---

## 三、尺寸

每页是 **3 行 × 6 列**，所以：

| 写法 | 效果 |
|---|---|
| `{"rows":1,"cols":1}` | 一格 |
| `{"rows":1,"cols":2}` | 一格宽 |
| `{"rows":2,"cols":2}` | 2×2 大方块 |
| `{"rows":1,"cols":6}` | 整行横条 |
| `{"rows":3,"cols":6}` | 整页 |

```json
"sizes": [
  { "rows": 1, "cols": 1, "label": "迷你" },
  { "rows": 1, "cols": 2, "label": "标准", "default": true },
  { "rows": 2, "cols": 2, "label": "大号" }
]
```

`default: true` 的那档是主档（市场里默认选中）。`label` 显示在尺寸胶囊上。

实际格子约 **61 × 63 像素**：1×1 去掉默认内边距后只有 37px 左右宽，一行放不下就会被省略号截断。
写 1×1 版式时建议字号 ≤ 13、文字控制在 4–5 个字符，或者把 `pad` 调小（例如 `[3, 5, 3, 5]`）。
`layout` 里给了对应尺寸键就用那份版式；没给就用 `default`，并按行高自动放大字号（2 行 ×1.35，3 行 ×1.7）。

---

## 四、数据源

```json
"data": {
  "unit":  { "type": "static", "value": "%" },
  "clock": { "type": "clock", "format": "HH:mm:ss" },
  "cpu": {
    "type": "command",
    "run": "powershell -NoProfile -Command \"(Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average\"",
    "interval": 2000,
    "timeout": 4000,
    "firstLine": true
  },
  "note": { "type": "file", "path": "C:\\note.txt", "firstLine": true, "interval": 5000 }
}
```

| type | 说明 |
|---|---|
| `static` | 固定文本，`value` |
| `clock` | 当前时间，按 `format`（.NET 时间格式串） |
| `command` | 通过 `cmd.exe /c` 执行 `run`，取标准输出；`interval` 拉取间隔、`timeout` 超时（超时会被杀掉）、`firstLine` 只取第一行 |
| `file` | 读 `path` 指向的文本文件，同样支持 `firstLine` / `interval` |

要点：

- 命令在后台线程跑，**不会卡住面板**；单次超时最长 30 秒，取不到内容显示 `—`。
- 工作目录是组件目录，所以 `"run": "powershell -File update.ps1"` 这种相对路径可用。
- 静态 / 时钟类数据源在每帧现算，不占后台线程，`refresh` 可以给到 500ms。

**内置占位符**（不需要声明）：`{time}` `{date}` `{weekday}` `{day}` `{month}` `{year}`。
`{time}` 是 `HH:mm`，`{date}` 是 `yyyy-MM-dd`，`{weekday}` 形如「周六」，`{day}`/`{month}` 补零。
想要别的格式就在 `data` 里定义 `clock` 数据源。

模板里写 `{{` `}}` 可以输出字面的大括号。

---

## 五、版式

`layout` 的一档版式里，`rows` 是一行一行的列表：

```json
{
  "pad": [9, 7, 9, 7],
  "align": "center",
  "rows": [
    {
      "type": "row",
      "gap": 6,
      "cells": [
        { "type": "icon", "iconPreset": "cpu", "size": 12 },
        { "type": "text", "text": "CPU 负载", "size": 9, "color": "dim", "flex": 1 },
        { "type": "text", "text": "{load}%", "size": 13, "weight": "semibold", "align": "right" }
      ]
    },
    { "type": "bar", "value": "{load}", "height": 4 }
  ]
}
```

- `pad`：内边距 `[左, 上, 右, 下]`，也可以写一个数字表示四边相同。
- `align`：内容整体竖向对齐 `center`（默认）/ `top` / `bottom` / `stretch`。

| 行类型 | 可用属性 |
|---|---|
| `text` | `text`（可含占位符）、`size`（默认 10）、`weight`（normal / semibold / bold）、`color`、`align`（left / center / right）、`wrap` |
| `bar` | `value`（0–100）、`height`（2–12）、`color` |
| `icon` | `icon` 或 `iconPreset`、`size`（默认 12）、`color`、`align` |
| `row` | `cells`（text / icon / bar / space）、`gap`；格子的 `flex` > 0 时按比例分宽度 |
| `space` | 撑开的空白，把后面的内容推到另一头 |

`color` 取值：`primary`（默认正文）、`dim`、`faint`、`accent`、`warn`、`danger`、`severity`
（`severity` 按数值自动变黄变红，阈值 65% / 85%）。

---

## 六、点击动作

```json
"action": { "run": "start https://example.com" }
```

整块变成可点区域，编辑模式下不触发。命令同样在后台跑、隐藏窗口，超时 8 秒。
适合做「打开某网站」「切到某个应用」「跑个脚本」这类一键组件。

---

## 七、安全与调试

- 自定义组件**可以执行任意命令**。导入前请确认清单里的 `run` 是你能接受的；导入确认窗会把要执行的命令逐条列出来。
- 导入时就会校验：`schema`、`id`、`name`、图标路径、布局节点类型、以及**占位符是否都有定义**（拼错会直接报错，不会静默显示空白）。
- 命令没输出、超时或出错时，组件显示 `—`；其他组件不受影响。
- 改完 `widget.json` 后：市场里右键「打开组件文件夹」改文件 → 在「添加 → 自定义组件」里重新拖入同一个目录即可覆盖安装。

---

## 八、示例

「添加 → 自定义组件」页底部的「组件开发说明与示例」会打开 `%LOCALAPPDATA%\WidgetNest\widget-samples\`，里面有两个可直接拖入的示例：

- `cpu-load-demo` — 命令 + 进度条，三档尺寸三种版式。
- `hello-demo` — 纯占位符排版，不跑命令。

改一改再拖回虚线框就能看到效果。
