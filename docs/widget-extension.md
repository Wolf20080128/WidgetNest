# 自定义组件接口文档

一份 `widget.json` 就是一个小组件：**不编译、不写 C#**，写完把目录（或单个 `.json` 文件）拖进「添加 → 自定义组件」的虚线框即可。

- **schema 版本**：`widgetnest.widget/1`
- **安装位置**：`%LOCALAPPDATA%\WidgetNest\widgets\<id>\`
- **示例**：「添加 → 自定义组件」页底部「组件开发说明与示例」会打开 `%LOCALAPPDATA%\WidgetNest\widget-samples\`，里面有两个可直接拖入的组件

> 想用 C# 写一个内置组件（自己的取数与绘制逻辑），看 [内置组件开发接口](widget-api.md)。

---

## 一、目录结构

```
我的网速/
  ├─ widget.json      ← 必需，清单
  └─ update.ps1       ← 可选，清单里的命令可以引用（工作目录就是组件目录）
```

---

## 二、最小可用清单

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

## 三、顶层字段

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `schema` | string | **是** | — | 固定 `widgetnest.widget/1`，大小写不敏感 |
| `id` | string | **是** | 目录名 | 唯一标识，至少 2 字符。会转小写并丢弃 `a-z 0-9 - _ .` 以外的字符，空格与 `·` 转成 `-`。导入后作为安装目录名，**重复导入会覆盖同 id 组件** |
| `name` | string | **是** | — | 市场里显示的名字，超过 24 字截断 |
| `description` | string | 否 | `""` | 二级菜单里的说明文字 |
| `pinyin` | string | 否 | `id` | 市场 A→Z 排序键，建议写全拼小写 |
| `author` | string | 否 | `""` | 导入确认窗显示来源 |
| `version` | string | 否 | `""` | 同上 |
| `icon` | string | 否 | 内置方块图标 | SVG path 的 `d` 属性，24×24 视口，如 `"M3,3 L10,3 ..."`；解析失败直接报错 |
| `iconPreset` | string | 否 | — | 复用内置图标，见 §八；与 `icon` 同时存在时 **`icon` 优先** |
| `refresh` | number | 否 | `2000` | 整体重绘间隔（毫秒），取整后夹在 `500`–`600000` |
| `sizes` | array | 否 | `[{rows:1, cols:2, label:"标准"}]` | 支持的尺寸，见 §四 |
| `data` | object | 否 | `{}` | 数据源，键名即布局里的占位符名，见 §五 |
| `layout` | object | **是** | — | 版式，键为 `1x2` 这类尺寸串或 `default`，见 §六 |
| `action` | object | 否 | — | `{ "run": "命令" }`，点击整块时执行，见 §十 |

JSON 里允许**尾随逗号**和 `//` 注释。

---

## 四、尺寸 `sizes`

面板每页是 **3 行 × 6 列**，`sizes` 决定这个组件在市场上能选哪几档。

| 字段 | 类型 | 默认 | 取值 |
|---|---|---|---|
| `rows` | number | `1` | 夹在 `1`–`3` |
| `cols` | number | `2` | 夹在 `1`–`6` |
| `label` | string | `"行×列"` | 尺寸胶囊上的叫法（迷你 / 标准 / 大号 …） |
| `default` | bool | `false` | 标了就是**主档**：市场默认选中，并排到尺寸列表最前 |

规则：

- 同一组 `rows`×`cols` 只保留**第一次出现**的那条，重复的静默忽略
- 标了 `default: true` 的那档排到最前，成为主档；没有标记时第一项就是主档
- 不写 `sizes` 等价于 `[{ "rows": 1, "cols": 2, "label": "标准" }]`

```json
"sizes": [
  { "rows": 1, "cols": 1, "label": "迷你" },
  { "rows": 1, "cols": 2, "label": "标准", "default": true },
  { "rows": 2, "cols": 2, "label": "大号" }
]
```

---

## 五、数据源 `data`

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

键名就是布局里 `{键名}` 的名字。

### 5.1 `static`（别名 `text`）

固定文本。

| 字段 | 默认 | 说明 |
|---|---|---|
| `value` | `""` | 固定内容 |

### 5.2 `clock`（别名 `time`）

当前时间。

| 字段 | 默认 | 说明 |
|---|---|---|
| `format` | `"HH:mm"` | .NET 时间格式串，如 `HH:mm:ss`、`M月d日` |

### 5.3 `command`（别名 `cmd`、`shell`）

执行一条命令，取标准输出。

| 字段 | 默认 | 取值 | 说明 |
|---|---|---|---|
| `run` | — | **必填** | 命令本体，经 `cmd.exe /c` 执行 |
| `interval` | `3000` | `500`–`600000` | 拉取间隔（毫秒） |
| `timeout` | `4000` | `200`–`30000` | 超时（毫秒），超时会**连子进程一起杀掉** |
| `firstLine` | `true` | — | 只取第一行；`false` 取全部输出 |

执行细节：

- 工作目录是**组件目录**，所以 `"run": "powershell -File update.ps1"` 这类相对路径可以直接用
- 窗口隐藏、后台线程执行，**不会卡住面板**
- 输出会 `Trim()`；空输出、超时、异常时该占位符显示 `—`，其他组件不受影响

### 5.4 `file`

读一个文本文件。

| 字段 | 默认 | 取值 | 说明 |
|---|---|---|---|
| `path` | `""` | — | 文件路径，建议写绝对路径 |
| `firstLine` | `true` | — | 只取第一行 |
| `interval` | `5000` | `500`–`600000` | 重读间隔（毫秒） |

`static` / `clock` 每帧现算，不占后台线程，`refresh` 可以给到 500ms；`command` / `file` 按各自的 `interval` 节流。

---

## 六、版式 `layout`

```json
"layout": {
  "1x1": { "rows": [ ... ] },
  "1x2": { "rows": [ ... ] },
  "default": { "rows": [ ... ] }
}
```

**键名规范化**：`1X2`、`1×2`、`1*2` 都归一成 `1x2`；空键和 `default` 都表示兜底。

**选档顺序**：

1. 精确匹配当前占格，如 `1x2`
2. 退到 `default`，并按行高自动放大字号（2 行 ×1.35，3 行 ×1.7）
3. 都没有时，退到字典序最小的那一档（同样按行高放大字号）

### 6.1 一档版式的公共字段

| 字段 | 默认 | 说明 |
|---|---|---|
| `pad` | `[9, 7, 9, 7]` | 内边距，`[左, 上, 右, 下]`；写单个数字表示四边相同（缺省 `8`） |
| `align` | `"center"` | 内容整体竖向对齐：`center` / `top` / `bottom` / `stretch` |
| `rows` | — | 行数组，见 §6.2 |

### 6.2 节点类型

`rows` 里每一项是一行，按 `type` 区分（不写默认 `text`）。

| type | 属性 | 说明 |
|---|---|---|
| `text` | `text`、`size`、`weight`、`color`、`align`、`wrap`、`flex` | 单行文本，`text` 可含占位符 |
| `bar` | `value`、`height`、`color`、`flex` | 进度条，`value` 取 0–100，可含占位符 |
| `icon` | `icon` 或 `iconPreset`、`size`、`color`、`flex` | 矢量图标 |
| `row` | `cells`、`gap` | 横向分栏，`cells` 里放 text / icon / bar / space |
| `space` | — | 撑开的空白，把后面的内容推到另一头 |

**属性明细**

| 属性 | 作用于 | 默认 | 取值 / 说明 |
|---|---|---|---|
| `text` | text | `""` | 文本内容，支持占位符 |
| `size` | text、icon | text `10`、icon `12` | 字号 / 图标边长；text 写 `≤0` 按 10 处理。实际尺寸再乘尺寸倍率 |
| `weight` | text | `normal` | `normal` / `semibold` / `bold`（不认识的值按 normal） |
| `color` | 全部 | text `primary`、bar `accent`、icon `dim` | 见 §七 |
| `align` | text | `left` | `left` / `center` / `right` |
| `wrap` | text | `false` | 是否允许换行（默认单行省略号截断） |
| `height` | bar | `4` | 夹在 `2`–`12` |
| `value` | bar | — | 0–100，非数字按 0，超过 100 画满 |
| `flex` | text、icon、bar | text/icon `0`、bar `1` | `>0` 时在同一行里按权重分宽度；`0` 表示跟内容走 |
| `gap` | row | `6` | 格子间距 |
| `cells` | row | `[]` | **不能嵌套 `row`**，会直接报错 |

---

## 七、颜色

| 取值 | 效果 | 别名 |
|---|---|---|
| `primary` | 正文色（默认） | — |
| `dim` | 次要文字 | `secondary` |
| `faint` | 更淡的辅助字 | `tertiary` |
| `accent` | 主题强调色（蓝） | — |
| `warn` | 警示（黄） | — |
| `danger` | 危险（红） | `error` |
| `severity` | 按数值自动变色：≥65% 黄，≥85% 红 | — |

所有颜色都随浅色 / 深色主题自动切换，不用自己判断主题。

---

## 八、内置图标 `iconPreset`

24×24 视口的 Material 图标，写 key 就能用。`icon` 与 `iconPreset` 二选一，`icon` 优先。

| key | 别名 | key | 别名 |
|---|---|---|---|
| `clock` | `time` | `volume` | `audio` |
| `date` | `calendar-day` | `brightness` | — |
| `weather` | — | `mute` | — |
| `thermo` | `temperature`、`temp` | `lock` | — |
| `cpu` | `processor` | `todo` | `check` |
| `gpu` | — | `recent` | `files` |
| `disk` | `storage` | `keyboard` | — |
| `memory` | `ram` | `screenshot` | `camera` |
| `battery` | `power` | `schedule` | — |
| `bluetooth` | — | `calendar` | — |
| `wifi` | `network` | | |

都不写时用内置的「四方块」占位图标。

---

## 九、占位符

| 占位符 | 输出 | 示例 |
|---|---|---|
| `{time}` | `HH:mm` | `14:07` |
| `{date}` | `yyyy-MM-dd` | `2026-10-31` |
| `{weekday}` | 周几 | `周六` |
| `{day}` | 两位日 | `31` |
| `{month}` | 两位月 | `10` |
| `{year}` | 四位年 | `2026` |

- 想在文本里输出字面的大括号，写 `{{` `}}`
- 需要别的日期格式，就在 `data` 里定义 `clock` 数据源
- **导入时会校验**：布局里出现的每个 `{key}` 必须能在内置占位符或 `data` 里找到，拼错直接报错，不会静默显示空白

---

## 十、点击动作 `action`

```json
"action": { "run": "start https://example.com" }
```

- 整块变成可点区域；**编辑模式下不触发**
- 命令同样在后台跑、隐藏窗口，超时 8 秒
- 适合「打开某网站」「切到某个应用」「跑个脚本」这类一键组件

---

## 十一、导入时的校验

清单不合法会在导入时直接报错，不会装进去：

| 检查项 | 不通过时的提示 |
|---|---|
| `schema` 必须是 `widgetnest.widget/1` | `缺少 "schema": "widgetnest.widget/1"（当前是 "..."）` |
| `id` 至少 2 字符 | `id 至少 2 个字符，只能用字母、数字、- 和 _` |
| `name` 非空 | `缺少 "name"（组件显示名）` |
| 数据源 `type` 必须是四选一 | `数据源 "x" 的 type 不认识：…（可用 static / clock / command / file）` |
| `command` 必须有 `run` | `command 数据源缺少 "run"` |
| 至少要有一档版式 | `缺少 "layout"（至少要有一档版式）` |
| 布局里的 `{key}` 都必须有定义 | `布局里用了未定义的占位符：{foo}、{bar}` |
| 行类型必须是五选一 | `布局里不认识的行类型：…（可用 text / row / bar / icon / space）` |
| `row` 不能嵌套 | `row 里不能再套 row` |
| `icon` / `iconPreset` 必须合法 | `图标不是合法的矢量路径` / `图标 preset 不认识：…` |
| JSON 本身要能解析 | `JSON 格式有误：…` |

---

## 十二、尺寸与像素

实际格子约 **61 × 63 像素**，1×1 去掉默认内边距后只剩 37px 左右宽。

- 1×1 版式建议字号 ≤ 13、文字控制在 4–5 个字符，或者把 `pad` 调小（如 `[3, 5, 3, 5]`）
- 2 行以上的版式会自动放大字号，同样的 `size` 在 2×2 里看起来更大
- 放不下的文本按省略号截断（`wrap: true` 时改为换行）

---

## 十三、示例

`%LOCALAPPDATA%\WidgetNest\widget-samples\` 里有两个可直接拖入的组件：

- **`cpu-load-demo`** —— 命令数据源 + 进度条，三档尺寸三套版式
- **`hello-demo`** —— 纯占位符排版，不跑任何命令

一个带图标、进度条和点击动作的完整例子：

```json
{
  "schema": "widgetnest.widget/1",
  "id": "disk-free",
  "name": "磁盘剩余",
  "pinyin": "cipanshengyu",
  "description": "磁盘可用空间",
  "author": "you",
  "version": "1.0",
  "iconPreset": "disk",
  "refresh": 5000,
  "sizes": [{ "rows": 1, "cols": 2, "label": "标准", "default": true }],
  "data": {
    "free": {
      "type": "command",
      "run": "powershell -NoProfile -Command \"[math]::Round((Get-PSDrive C).Free/1GB,1)\"",
      "interval": 10000,
      "timeout": 5000
    },
    "unit": { "type": "static", "value": "GB 可用" }
  },
  "layout": {
    "default": {
      "pad": [9, 7, 9, 7],
      "rows": [
        {
          "type": "row",
          "gap": 6,
          "cells": [
            { "type": "icon", "iconPreset": "disk", "size": 12 },
            { "type": "text", "text": "磁盘", "size": 9, "color": "dim", "flex": 1 },
            { "type": "text", "text": "{free}", "size": 14, "weight": "semibold", "align": "right" }
          ]
        },
        { "type": "text", "text": "{unit}", "size": 8, "color": "faint" }
      ]
    }
  },
  "action": { "run": "explorer C:\\" }
}
```

---

## 十四、调试与安全

- 改完 `widget.json` 后：市场里右键「打开组件文件夹」改文件 → 在「添加 → 自定义组件」里重新拖入同一个目录即可覆盖安装
- 命令没输出、超时或出错时组件显示 `—`，不会影响其他组件
- **自定义组件可以执行任意命令**。导入前请确认清单里的 `run` 是你能接受的——导入确认窗会把要执行的命令逐条列出来
- 只导入你自己写的，或者来源可信的组件

---

## 十五、版本与兼容

`schema` 目前只有 `widgetnest.widget/1`。后续若引入不兼容的字段变更，会启用新的 schema 值；旧清单继续按 `/1` 的规则解析，不会因为升级突然失效。
