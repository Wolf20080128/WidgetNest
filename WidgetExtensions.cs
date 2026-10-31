using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace WidgetNest;

// ============================================================================
//  自定义组件扩展协议（widget.json v1）
//
//  一个组件 = 一个目录，目录里有 widget.json（可选带脚本等附属文件）：
//    %LOCALAPPDATA%\WidgetNest\widgets\<id>\widget.json
//
//  清单声明四件事：长什么样（layout）、取什么数据（data）、支持哪些尺寸（sizes）、
//  以及点击时做什么（action）。运行期由 UserWidgetView 按实际占格现画。
// ============================================================================

internal enum WidgetDataKind {
    /// <summary>固定文本。</summary>
    Static,

    /// <summary>当前时间，按 format 格式化。</summary>
    Clock,

    /// <summary>跑一条命令，取标准输出的第一行（或全部）。</summary>
    Command,

    /// <summary>读一个文本文件。</summary>
    File
}

/// <summary>一个数据源，对应布局里 {key} 占位符的取值方式。</summary>
internal sealed class WidgetDataSpec {
    public required string Key { get; init; }

    public WidgetDataKind Kind { get; init; }

    public string Value { get; init; } = "";

    public string Format { get; init; } = "";

    public string Run { get; init; } = "";

    public string FilePath { get; init; } = "";

    public bool FirstLine { get; init; } = true;

    public int IntervalMs { get; init; }

    public int TimeoutMs { get; init; } = 4000;
}

internal enum WidgetNodeKind { Text, Row, Bar, Icon, Space }

/// <summary>布局里的一行内容 / 行里的一个格子。</summary>
internal sealed class WidgetNode {
    public WidgetNodeKind Kind { get; init; }

    public string Text { get; init; } = "";

    public double Size { get; init; }

    public string Weight { get; init; } = "normal";

    public string Color { get; init; } = "primary";

    public string Align { get; init; } = "left";

    public bool Wrap { get; init; }

    public double Height { get; init; } = 4;

    /// <summary>行内单元格的横向权重：&gt;0 表示按星号分配宽度，0 表示跟内容走。</summary>
    public double Flex { get; init; }

    public double Gap { get; init; } = 6;

    public string IconPath { get; init; } = "";

    public IReadOnlyList<WidgetNode> Cells { get; init; } = Array.Empty<WidgetNode>();
}

/// <summary>一档尺寸对应的版式。</summary>
internal sealed class WidgetLayout {
    public double PadLeft { get; init; } = 9;
    public double PadTop { get; init; } = 7;
    public double PadRight { get; init; } = 9;
    public double PadBottom { get; init; } = 7;

    /// <summary>整体竖向对齐：center（默认）/ top / bottom / stretch。</summary>
    public string Align { get; init; } = "center";

    public IReadOnlyList<WidgetNode> Rows { get; init; } = Array.Empty<WidgetNode>();
}

/// <summary>一个导入的自定义组件（一个 widget.json）。</summary>
internal sealed class UserWidget {
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Pinyin { get; init; } = "";

    public string Description { get; init; } = "";

    public string Author { get; init; } = "";

    public string Version { get; init; } = "";

    public string IconPath { get; init; } = "";

    /// <summary>整体刷新节奏（毫秒）。命令类数据源还有自己的间隔。</summary>
    public int RefreshMs { get; init; } = 2000;

    public IReadOnlyList<WidgetSizeSpec> Sizes { get; init; } = Array.Empty<WidgetSizeSpec>();

    public IReadOnlyList<WidgetDataSpec> Data { get; init; } = Array.Empty<WidgetDataSpec>();

    /// <summary>版式：键是 "1x2" 这种尺寸串，或 "default"。</summary>
    public IReadOnlyDictionary<string, WidgetLayout> Layouts { get; init; } =
        new Dictionary<string, WidgetLayout>();

    /// <summary>点击整块时执行的命令，空表示不可点。</summary>
    public string ActionRun { get; init; } = "";

    /// <summary>清单所在目录（导入时要连同附属文件一起复制）。</summary>
    public string Folder { get; init; } = "";

    public string Json { get; init; } = "";

    /// <summary>来源是目录（整包导入）还是单个 .json 文件。</summary>
    public bool FromFolder { get; init; }

    public string FamilyKey => "u:" + Id;

    /// <summary>市场排序用的拼音，没写就用 id。</summary>
    public string SortKey => string.IsNullOrWhiteSpace(Pinyin) ? Id : Pinyin;

    /// <summary>展开成目录里的尺寸变体，主档沿用 "u:{id}"。</summary>
    public IReadOnlyList<WidgetDef> ToDefs() {
        var primary = Sizes.FirstOrDefault();
        var list = new List<WidgetDef>(Sizes.Count);

        foreach (var size in Sizes) {
            bool isPrimary = primary is not null &&
                             size.Rows == primary.Rows && size.Cols == primary.Cols;

            list.Add(new WidgetDef {
                Id = isPrimary ? FamilyKey : $"{FamilyKey}-{size.Rows}x{size.Cols}",
                Name = Name,
                Pinyin = SortKey,
                Rows = size.Rows,
                Cols = size.Cols,
                Family = FamilyKey,
                SizeLabel = size.Label,
                Description = Description.Length == 0 ? "自定义组件" : Description,
                IconPath = IconPath,
                IsUserWidget = true,
                IsAction = ActionRun.Length > 0
            });
        }

        return list;
    }

    /// <summary>按实际占格挑一档版式。找不到同尺寸就用 default，并按尺寸放大字号。</summary>
    public WidgetLayout? FindLayout(WidgetBox box, out double scale) {
        scale = 1;

        if (Layouts.TryGetValue($"{box.Rows}x{box.Cols}", out var exact))
            return exact;

        if (Layouts.TryGetValue("default", out var fallback)) {
            scale = box.Scale;
            return fallback;
        }

        if (Layouts.Count == 0)
            return null;

        scale = box.Scale;
        return Layouts.OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
    }
}

/// <summary>占位符渲染：{key} 换值，{{ }} 输出原来的大括号。</summary>
internal static class WidgetTemplate {
    public static string Fill(string template, Func<string, string?> lookup) {
        if (template.Length == 0 || template.IndexOf('{') < 0)
            return template;

        var builder = new StringBuilder(template.Length + 16);

        for (int i = 0; i < template.Length; i++) {
            char current = template[i];

            if (current == '{' && i + 1 < template.Length && template[i + 1] == '{') {
                builder.Append('{');
                i++;
                continue;
            }

            if (current == '}' && i + 1 < template.Length && template[i + 1] == '}') {
                builder.Append('}');
                i++;
                continue;
            }

            if (current != '{') {
                builder.Append(current);
                continue;
            }

            int end = template.IndexOf('}', i + 1);

            if (end < 0) {
                builder.Append(current);
                continue;
            }

            string key = template[(i + 1)..end].Trim();

            if (key.Length == 0) {
                builder.Append(current);
                continue;
            }

            builder.Append(lookup(key) ?? "");
            i = end;
        }

        return builder.ToString();
    }

    /// <summary>列出模板里用到的占位符名，导入时用来校验拼写。</summary>
    public static IEnumerable<string> Keys(string template) {
        for (int i = 0; i < template.Length; i++) {
            char current = template[i];

            if (current == '{' && i + 1 < template.Length && template[i + 1] == '{') {
                i++;
                continue;
            }

            if (current != '{')
                continue;

            int end = template.IndexOf('}', i + 1);

            if (end < 0)
                break;

            string key = template[(i + 1)..end].Trim();

            if (key.Length > 0)
                yield return key;

            i = end;
        }
    }
}

/// <summary>
/// 自定义组件的解析 / 安装 / 扫描。清单格式详见 docs/widget-extension.md。
/// </summary>
internal static class WidgetExtensions {
    /// <summary>内置占位符：不用在 data 里声明。</summary>
    public static readonly string[] BuiltinKeys = { "time", "date", "weekday", "day", "month", "year" };

    public const string SchemaId = "widgetnest.widget/1";

    private static List<UserWidget> _widgets = new();

    // 解析清单时会去取内置图标，而内置图标在 WidgetCatalog 里；
    // 目录的静态构造又会回调 Load，这里挡一下重入。
    private static bool _loading;

    public static IReadOnlyList<UserWidget> All => _widgets;

    /// <summary>所有自定义组件展开后的尺寸变体，由 WidgetCatalog 合并进目录。</summary>
    public static IReadOnlyList<WidgetDef> Defs { get; private set; } = Array.Empty<WidgetDef>();

    public static string RootDir => AppStorage.WidgetDir;

    public static UserWidget? Find(string id) =>
        _widgets.FirstOrDefault(widget => string.Equals(widget.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>按 FamilyKey（"u:xxx"）找组件。</summary>
    public static UserWidget? FindByFamily(string familyKey) =>
        familyKey.StartsWith("u:", StringComparison.Ordinal)
            ? Find(familyKey[2..])
            : null;

    // ===== 扫描 =====

    public static void Load() {
        if (_loading)
            return;

        _loading = true;

        try {
            LoadCore();
        }
        finally {
            _loading = false;
        }
    }

    private static void LoadCore() {
        var widgets = new List<UserWidget>();

        try {
            if (Directory.Exists(RootDir)) {
                foreach (string folder in Directory.GetDirectories(RootDir)) {
                    try {
                        if (ReadFolder(folder, out var widget, out _) && widget != null)
                            widgets.Add(widget);
                    }
                    catch {
                        // 单个组件坏了不该影响其它组件
                    }
                }
            }
        }
        catch {
            // 目录读不到就当作没有自定义组件
        }

        _widgets = widgets;
        Defs = widgets.SelectMany(widget => widget.ToDefs()).ToList();

        CleanEmptyFolders();
    }

    /// <summary>
    /// 卸载时若命令进程还占着目录，会留下一个空壳。启动时顺手扫掉这些空目录，
    /// 只删完全空的，不动用户目录里还有文件的东西。
    /// </summary>
    private static void CleanEmptyFolders() {
        try {
            foreach (string folder in Directory.GetDirectories(RootDir)) {
                if (Directory.EnumerateFileSystemEntries(folder).Any())
                    continue;

                try {
                    Directory.Delete(folder);
                }
                catch {
                    // 还占着就下次再说
                }
            }
        }
        catch {
            // 目录读不到就算了
        }
    }

    // ===== 解析 =====

    /// <summary>
    /// 判断拖进来的东西是不是组件包（.json 清单或含清单的目录）。
    /// 只做轻量探测，真正的校验在 <see cref="ReadSource"/>。
    /// </summary>
    public static bool LooksLikeWidget(string path) {
        try {
            if (Directory.Exists(path))
                return FindManifest(path) != null;

            if (!File.Exists(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return false;

            // 只读前 4KB 找标记，避免用户拖进来一个很大的数据文件
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(4096, (int)Math.Min(stream.Length, 4096))];
            int read = stream.Read(buffer, 0, buffer.Length);

            return Encoding.UTF8.GetString(buffer, 0, read)
                .Contains("widgetnest.widget", StringComparison.OrdinalIgnoreCase);
        }
        catch {
            return false;
        }
    }

    /// <summary>读一个被拖进来的组件包（目录或 .json），失败时给出中文原因。</summary>
    public static UserWidget? ReadSource(string path, out string error) {
        error = "";

        if (Directory.Exists(path))
            return ReadFolder(path, out var widget, out error) ? widget : null;

        if (!File.Exists(path)) {
            error = "找不到这个文件";
            return null;
        }

        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
            error = "不是 .json 组件包";
            return null;
        }

        try {
            string json = File.ReadAllText(path, Encoding.UTF8);
            string fallback = Path.GetFileNameWithoutExtension(path);
            return Parse(json, Path.GetDirectoryName(path) ?? "", fallback, fromFolder: false, out error);
        }
        catch (Exception ex) {
            error = ex.Message;
            return null;
        }
    }

    private static bool ReadFolder(string folder, out UserWidget? widget, out string error) {
        widget = null;
        error = "";

        string? manifest = FindManifest(folder);

        if (manifest == null) {
            error = "目录里没有 widget.json";
            return false;
        }

        try {
            string json = File.ReadAllText(manifest, Encoding.UTF8);
            widget = Parse(json, folder, Path.GetFileName(folder) ?? "widget", fromFolder: true, out error);

            return widget != null;
        }
        catch (Exception ex) {
            error = ex.Message;
            return false;
        }
    }

    private static string? FindManifest(string folder) {
        string direct = Path.Combine(folder, "widget.json");

        if (File.Exists(direct))
            return direct;

        foreach (string file in Directory.GetFiles(folder, "*.json")) {
            try {
                if (LooksLikeWidget(file))
                    return file;
            }
            catch {
                // 忽略读不了的文件
            }
        }

        return null;
    }

    /// <summary>解析清单文本。校验失败抛 <see cref="FormatException"/>。</summary>
    public static UserWidget Parse(string json, string folder, string fallbackId,
        bool fromFolder, out string error) {
        error = "";

        try {
            return ParseCore(json, folder, fallbackId, fromFolder);
        }
        catch (FormatException ex) {
            error = ex.Message;
            return null!;
        }
        catch (JsonException ex) {
            error = "JSON 格式有误：" + ex.Message;
            return null!;
        }
    }

    private static UserWidget ParseCore(string json, string folder, string fallbackId, bool fromFolder) {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = new JObj(document.RootElement);

        string schema = root.Str("schema");

        if (!string.Equals(schema, SchemaId, StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"缺少 \"schema\": \"{SchemaId}\"（当前是 \"{schema}\"）");

        string id = NormalizeId(root.Str("id", fallbackId));

        if (id.Length < 2)
            throw new FormatException("id 至少 2 个字符，只能用字母、数字、- 和 _");

        string name = root.Str("name").Trim();

        if (name.Length == 0)
            throw new FormatException("缺少 \"name\"（组件显示名）");

        if (name.Length > 24)
            name = name[..24];

        string icon = ResolveIcon(root);

        // ---- 数据源 ----
        var data = new List<WidgetDataSpec>();

        foreach (var entry in root.Entries("data")) {
            string key = entry.Key.Trim();

            if (key.Length == 0)
                continue;

            var spec = entry.Value;

            string kind = spec.Str("type", "static").ToLowerInvariant();

            data.Add(kind switch {
                "static" or "text" => new WidgetDataSpec {
                    Key = key, Kind = WidgetDataKind.Static, Value = spec.Str("value")
                },
                "clock" or "time" => new WidgetDataSpec {
                    Key = key, Kind = WidgetDataKind.Clock, Format = spec.Str("format", "HH:mm")
                },
                "command" or "cmd" or "shell" => new WidgetDataSpec {
                    Key = key,
                    Kind = WidgetDataKind.Command,
                    Run = RequireCommand(spec),
                    IntervalMs = (int)Math.Clamp(spec.Num("interval", 3000), 500, 600_000),
                    TimeoutMs = (int)Math.Clamp(spec.Num("timeout", 4000), 200, 30_000),
                    FirstLine = spec.Bool("firstLine", true)
                },
                "file" => new WidgetDataSpec {
                    Key = key,
                    Kind = WidgetDataKind.File,
                    FilePath = spec.Str("path"),
                    FirstLine = spec.Bool("firstLine", true),
                    IntervalMs = (int)Math.Clamp(spec.Num("interval", 5000), 500, 600_000)
                },
                _ => throw new FormatException(
                    $"数据源 \"{key}\" 的 type 不认识：{kind}（可用 static / clock / command / file）")
            });
        }

        // ---- 尺寸 ----
        var sizes = new List<WidgetSizeSpec>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int defaultIndex = -1;

        foreach (var item in root.Arr("sizes")) {
            int rows = (int)Math.Clamp(item.Num("rows", 1), 1, 3);
            int cols = (int)Math.Clamp(item.Num("cols", 2), 1, 6);

            if (!used.Add($"{rows}x{cols}"))
                continue;

            if (defaultIndex < 0 && item.Bool("default"))
                defaultIndex = sizes.Count;

            string label = item.Str("label");

            sizes.Add(new WidgetSizeSpec(rows, cols, label.Length == 0 ? $"{rows}×{cols}" : label));
        }

        if (sizes.Count == 0)
            sizes.Add(new WidgetSizeSpec(1, 2, "标准"));

        // 清单里标了 default:true 的那档排到最前面，它就是「主档」
        if (defaultIndex > 0) {
            var primary = sizes[defaultIndex];
            sizes.RemoveAt(defaultIndex);
            sizes.Insert(0, primary);
        }

        // ---- 布局 ----
        var layouts = new Dictionary<string, WidgetLayout>(StringComparer.OrdinalIgnoreCase);
        var placeholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in root.Entries("layout"))
            layouts[NormalizeSizeKey(entry.Key)] = ParseLayout(entry.Value, placeholders);

        if (layouts.Count == 0)
            throw new FormatException("缺少 \"layout\"（至少要有一档版式）");

        // 占位符拼错在导入时就要报出来，否则运行期只会显示空白，很难查
        var allowed = new HashSet<string>(BuiltinKeys, StringComparer.OrdinalIgnoreCase);

        foreach (var spec in data)
            allowed.Add(spec.Key);

        var unknown = placeholders.Where(key => !allowed.Contains(key)).ToList();

        if (unknown.Count > 0)
            throw new FormatException(
                $"布局里用了未定义的占位符：{string.Join("、", unknown.Select(key => "{" + key + "}"))}");

        // ---- 点击动作 ----
        string actionRun = "";
        var action = root.Obj("action");

        if (action != null)
            actionRun = action.Str("run");

        return new UserWidget {
            Id = id,
            Name = name,
            Pinyin = root.Str("pinyin"),
            Description = root.Str("description"),
            Author = root.Str("author"),
            Version = root.Str("version"),
            IconPath = icon,
            RefreshMs = (int)Math.Clamp(root.Num("refresh", 2000), 500, 600_000),
            Sizes = sizes,
            Data = data,
            Layouts = layouts,
            ActionRun = actionRun,
            Folder = folder,
            Json = json,
            FromFolder = fromFolder
        };
    }

    private static string RequireCommand(JObj spec) {
        string run = spec.Str("run").Trim();

        if (run.Length == 0)
            throw new FormatException("command 数据源缺少 \"run\"");

        return run;
    }

    private static string ResolveIcon(JObj root) {
        string svg = root.Str("icon");

        if (svg.Length == 0) {
            string preset = root.Str("iconPreset");

            if (preset.Length > 0) {
                svg = WidgetCatalog.PresetIcon(preset)
                      ?? throw new FormatException(
                          $"iconPreset 不认识：{preset}（可用 clock / date / weather / thermo / gpu / disk / memory / battery / bluetooth / wifi / volume / brightness / mute / lock / todo / recent / keyboard / screenshot / schedule / calendar）");
            }
            else {
                // 没写图标就给个通用的「组件」方块
                return DefaultIcon;
            }
        }

        try {
            System.Windows.Media.Geometry.Parse(svg);
        }
        catch {
            throw new FormatException("icon 不是合法的矢量路径（需要 SVG path 的 d 属性，24×24 视口）");
        }

        return svg;
    }

    private const string DefaultIcon =
        "M3,3 L10,3 L10,10 L3,10 Z M14,3 L21,3 L21,10 L14,10 Z " +
        "M3,14 L10,14 L10,21 L3,21 Z M14,14 L21,14 L21,21 L14,21 Z";

    private static WidgetLayout ParseLayout(JObj obj, HashSet<string> placeholders) {
        var pad = obj.Nums("pad");
        double left = 9, top = 7, right = 9, bottom = 7;

        if (pad.Count == 4) {
            left = pad[0];
            top = pad[1];
            right = pad[2];
            bottom = pad[3];
        }
        else if (obj.Num("pad", -1) >= 0) {
            left = top = right = bottom = obj.Num("pad", 8);
        }

        var rows = new List<WidgetNode>();

        foreach (var row in obj.Arr("rows"))
            rows.Add(ParseNode(row, placeholders, allowRow: true));

        return new WidgetLayout {
            PadLeft = left,
            PadTop = top,
            PadRight = right,
            PadBottom = bottom,
            Align = obj.Str("align", "center").ToLowerInvariant(),
            Rows = rows
        };
    }

    private static WidgetNode ParseNode(JObj obj, HashSet<string> placeholders, bool allowRow) {
        string type = obj.Str("type", "text").ToLowerInvariant();

        switch (type) {
            case "space":
                return new WidgetNode { Kind = WidgetNodeKind.Space };

            case "text": {
                string text = obj.Str("text");

                foreach (string key in WidgetTemplate.Keys(text))
                    placeholders.Add(key);

                return new WidgetNode {
                    Kind = WidgetNodeKind.Text,
                    Text = text,
                    Size = obj.Num("size", 0),
                    Weight = obj.Str("weight", "normal").ToLowerInvariant(),
                    Color = obj.Str("color", "primary").ToLowerInvariant(),
                    Align = obj.Str("align", "left").ToLowerInvariant(),
                    Wrap = obj.Bool("wrap"),
                    Flex = obj.Num("flex", 0)
                };
            }

            case "bar": {
                string value = obj.Str("value");

                foreach (string key in WidgetTemplate.Keys(value))
                    placeholders.Add(key);

                return new WidgetNode {
                    Kind = WidgetNodeKind.Bar,
                    Text = value,
                    Height = Math.Clamp(obj.Num("height", 4), 2, 12),
                    Color = obj.Str("color", "accent").ToLowerInvariant(),
                    Flex = obj.Num("flex", 1)
                };
            }

            case "icon": {
                string preset = obj.Str("iconPreset", obj.Str("preset"));
                string svg = obj.Str("icon", obj.Str("svg"));

                if (svg.Length == 0) {
                    svg = preset.Length > 0
                        ? WidgetCatalog.PresetIcon(preset) ??
                          throw new FormatException($"图标 preset 不认识：{preset}")
                        : DefaultIcon;
                }

                try {
                    System.Windows.Media.Geometry.Parse(svg);
                }
                catch {
                    throw new FormatException("图标不是合法的矢量路径");
                }

                return new WidgetNode {
                    Kind = WidgetNodeKind.Icon,
                    IconPath = svg,
                    Size = obj.Num("size", 12),
                    Color = obj.Str("color", "dim").ToLowerInvariant(),
                    Flex = obj.Num("flex", 0)
                };
            }

            case "row": {
                if (!allowRow)
                    throw new FormatException("row 里不能再套 row");

                var cells = new List<WidgetNode>();

                foreach (var cell in obj.Arr("cells"))
                    cells.Add(ParseNode(cell, placeholders, allowRow: false));

                return new WidgetNode {
                    Kind = WidgetNodeKind.Row,
                    Cells = cells,
                    Gap = obj.Num("gap", 6)
                };
            }

            default:
                throw new FormatException(
                    $"布局里不认识的行类型：{type}（可用 text / row / bar / icon / space）");
        }
    }

    private static string NormalizeSizeKey(string key) {
        string normalized = key.Trim().ToLowerInvariant().Replace('×', 'x').Replace('*', 'x');

        if (normalized is "default" or "")
            return "default";

        return normalized;
    }

    public static string NormalizeId(string raw) {
        var builder = new StringBuilder(raw.Length);

        foreach (char c in raw.Trim().ToLowerInvariant()) {
            if (char.IsLetterOrDigit(c) && c < 128)
                builder.Append(c);
            else if (c is '-' or '_' or '.')
                builder.Append(c);
            else if (c is ' ' or '·')
                builder.Append('-');
        }

        string id = builder.ToString().Trim('-', '.');

        return id.Length == 0 ? "widget" : id;
    }

    // ===== 安装 / 卸载 =====

    /// <summary>把读进来的组件落盘。返回 false 时 <paramref name="error"/> 说明原因。</summary>
    public static bool Install(UserWidget widget, out string error) {
        error = "";

        try {
            Directory.CreateDirectory(RootDir);

            string target = Path.Combine(RootDir, widget.Id);
            string source = widget.Folder;

            bool sameFolder = source.Length > 0 &&
                              string.Equals(Path.GetFullPath(target).TrimEnd('\\'),
                                  Path.GetFullPath(source).TrimEnd('\\'),
                                  StringComparison.OrdinalIgnoreCase);

            if (!sameFolder) {
                // 旧版本可能正被它的命令进程占着目录，删不掉就原地覆盖
                if (Directory.Exists(target))
                    TryDeleteFolder(target);

                Directory.CreateDirectory(target);

                if (widget.FromFolder)
                    CopyFolder(source, target);
                else
                    File.WriteAllText(Path.Combine(target, "widget.json"), widget.Json, new UTF8Encoding(false));
            }

            // 目录导入时清单文件名可能不叫 widget.json，统一补一份，扫描才找得到
            string manifest = Path.Combine(target, "widget.json");

            if (!File.Exists(manifest))
                File.WriteAllText(manifest, widget.Json, new UTF8Encoding(false));

            return true;
        }
        catch (Exception ex) {
            error = "写入失败：" + ex.Message;
            return false;
        }
    }

    public static bool Uninstall(UserWidget widget, out string error) {
        error = "";

        string folder = Path.Combine(RootDir, widget.Id);

        if (!Directory.Exists(folder))
            return true;

        try {
            // 先摘掉清单：即使目录被占用（命令进程的工作目录），重启后也不会再被加载
            File.Delete(Path.Combine(folder, "widget.json"));
        }
        catch {
            // 删不掉就靠下面的重试
        }

        if (TryDeleteFolder(folder))
            return true;

        // 残留的通常是正在退出的命令进程，过一会儿再清
        Task.Run(() => {
            for (int i = 0; i < 10; i++) {
                Thread.Sleep(400);

                if (TryDeleteFolder(folder))
                    return;
            }
        });

        return true;
    }

    /// <summary>
    /// 删目录，遇到占用（命令进程的工作目录正好在里面）就小退一下再试。
    /// 5 次共约 300ms，足够等一个短命令退出。
    /// </summary>
    private static bool TryDeleteFolder(string folder) {
        for (int attempt = 0; attempt < 5; attempt++) {
            try {
                if (!Directory.Exists(folder))
                    return true;

                Directory.Delete(folder, true);

                return true;
            }
            catch {
                Thread.Sleep(60);
            }
        }

        return !Directory.Exists(folder);
    }

    private static void CopyFolder(string source, string target) {
        foreach (string file in Directory.GetFiles(source)) {
            try {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
            catch {
                // 单个附属文件复制失败不影响主体
            }
        }

        foreach (string folder in Directory.GetDirectories(source)) {
            string next = Path.Combine(target, Path.GetFileName(folder));
            Directory.CreateDirectory(next);
            CopyFolder(folder, next);
        }
    }

    // ===== 开发示例 =====

    /// <summary>
    /// 把开发说明和两个示例包写到 widget-samples 目录，用户照着改完拖回来就能用。
    /// 已存在的不覆盖，避免盖掉用户自己的改动。
    /// </summary>
    public static void EnsureSamples() {
        try {
            string dir = AppStorage.WidgetSampleDir;
            Directory.CreateDirectory(dir);

            WriteIfMissing(Path.Combine(dir, "README.md"), SampleReadme);
            WriteIfMissing(Path.Combine(dir, "cpu-load-demo", "widget.json"), CpuSample);
            WriteIfMissing(Path.Combine(dir, "hello-demo", "widget.json"), HelloSample);
        }
        catch {
            // 示例写不出来不影响主流程
        }
    }

    private static void WriteIfMissing(string path, string content) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
            File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private const string SampleReadme = """
        # 自己写一个小组件

        一个小组件就是一个目录，里面放 `widget.json`：

        ```
        我的组件/
          └─ widget.json
        ```

        写好之后把「我的组件」文件夹（或者单个 .json 文件）拖到「添加 → 自定义组件」的虚线框里，
        确认信息后就会出现在「小组件市场」，可以选尺寸放到面板上。

        ## 最小示例

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

        ## 字段

        | 字段 | 必填 | 说明 |
        |---|---|---|
        | `schema` | 是 | 固定 `widgetnest.widget/1` |
        | `id` | 是 | 唯一标识，字母数字 `-` `_`，导入后作为目录名 |
        | `name` | 是 | 市场里显示的名字（≤ 24 字） |
        | `description` | 否 | 二级菜单里的说明 |
        | `icon` | 否 | SVG 路径（24×24 视口） |
        | `iconPreset` | 否 | 复用内置图标：clock / date / weather / thermo / gpu / disk / memory / battery / bluetooth / wifi / volume / brightness / mute / lock / todo / recent / keyboard / screenshot / schedule / calendar |
        | `refresh` | 否 | 整体刷新毫秒，默认 2000 |
        | `sizes` | 否 | 支持哪些尺寸，`rows` 1–3、`cols` 1–6，`default: true` 的那档是主档 |
        | `data` | 否 | 数据源，键名就是布局里的占位符名 |
        | `layout` | 是 | 版式，键是 `1x2` 这种尺寸串，或用 `default` 兜底 |
        | `action` | 否 | `{ "run": "命令" }`，点击整块时执行 |

        ## 数据源

        ```json
        "data": {
          "text": "静止文本",
          "fixed": { "type": "static", "value": "固定值" },
          "clock": { "type": "clock", "format": "HH:mm" },
          "cpu":   { "type": "command", "run": "powershell -NoProfile -Command \"...\"", "interval": 2000, "timeout": 4000, "firstLine": true },
          "note":  { "type": "file", "path": "C:\\note.txt", "firstLine": true }
        }
        ```

        - `command` 通过 `cmd.exe /c` 执行，工作目录是组件目录，取标准输出的第一行（`firstLine: false` 取全部）。
        - 命令是异步跑的，超过 `timeout` 会被强制结束，不会卡住面板。
        - 内置占位符不用声明：`{time}` `{date}` `{weekday}` `{day}` `{month}` `{year}`。

        ## 版式

        `rows` 里每一项是一行：

        | type | 说明 |
        |---|---|
        | `text` | `text`（可含占位符）、`size`、`weight`（normal/semibold/bold）、`color`（primary/dim/faint/accent/warn/danger/severity）、`align`（left/center/right）、`wrap` |
        | `bar` | 进度条，`value` 取 0–100，`height` 2–12 |
        | `icon` | 矢量图标，`icon`/`iconPreset` + `size` + `color` |
        | `row` | 横向一行，`cells` 里放 text/icon/bar/space，`gap` 控制间距；格子的 `flex` 大于 0 时按比例分宽度 |
        | `space` | 撑开的空白，把后面的内容推到另一头 |

        尺寸串没写对应版式时，用 `default` 那档，并随行高自动放大字号。

        格子实际约 61 × 63 像素：1×1 去掉默认内边距只剩约 37px 宽，放不下就会省略号截断。
        1×1 版式建议字号 ≤ 13、文字 4–5 个字符，或者把 `pad` 调小（如 `[3, 5, 3, 5]`）。

        ## 安全提示

        自定义组件可以执行命令。只导入你自己写的、或者你信任的来源的组件。
        """;

    private const string CpuSample = """
        {
          "schema": "widgetnest.widget/1",
          "id": "demo-cpu-load",
          "name": "CPU 负载（示例）",
          "pinyin": "demo-cpu",
          "description": "示例：一条 PowerShell 命令取 CPU 占用率，并画成进度条",
          "author": "WidgetNest 示例",
          "version": "1.0",
          "iconPreset": "cpu",
          "refresh": 2000,
          "sizes": [
            { "rows": 1, "cols": 1, "label": "迷你" },
            { "rows": 1, "cols": 2, "label": "标准", "default": true },
            { "rows": 2, "cols": 2, "label": "大号" }
          ],
          "data": {
            "load": {
              "type": "command",
              "run": "powershell -NoProfile -Command \"(Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average\"",
              "interval": 2000,
              "timeout": 4000
            },
            "unit": { "type": "static", "value": "%" }
          },
          "layout": {
            "1x1": {
              "rows": [
                { "type": "text", "text": "{load}", "size": 18, "weight": "semibold", "align": "center" },
                { "type": "text", "text": "{unit}", "size": 8, "color": "faint", "align": "center" },
                { "type": "bar", "value": "{load}", "height": 3 }
              ]
            },
            "default": {
              "rows": [
                {
                  "type": "row",
                  "cells": [
                    { "type": "icon", "iconPreset": "cpu", "size": 12 },
                    { "type": "text", "text": "CPU 负载", "size": 9, "color": "dim", "flex": 1 },
                    { "type": "text", "text": "{load}{unit}", "size": 13, "weight": "semibold", "align": "right" }
                  ]
                },
                { "type": "bar", "value": "{load}", "height": 4 }
              ]
            },
            "2x2": {
              "rows": [
                {
                  "type": "row",
                  "cells": [
                    { "type": "icon", "iconPreset": "cpu", "size": 16 },
                    { "type": "text", "text": "CPU 负载", "size": 11, "color": "dim", "flex": 1 }
                  ]
                },
                { "type": "space" },
                { "type": "text", "text": "{load}{unit}", "size": 30, "weight": "semibold", "align": "center" },
                { "type": "bar", "value": "{load}", "height": 5 }
              ]
            }
          }
        }
        """;

    private const string HelloSample = """
        {
          "schema": "widgetnest.widget/1",
          "id": "demo-hello",
          "name": "问候（示例）",
          "pinyin": "demo-hello",
          "description": "示例：只排版、不跑命令，全部用内置占位符",
          "author": "WidgetNest 示例",
          "version": "1.0",
          "iconPreset": "todo",
          "refresh": 1000,
          "sizes": [
            { "rows": 1, "cols": 1, "label": "迷你" },
            { "rows": 1, "cols": 2, "label": "标准", "default": true },
            { "rows": 1, "cols": 6, "label": "宽条" }
          ],
          "layout": {
            "1x1": {
              "pad": [3, 5, 3, 5],
              "rows": [
                { "type": "text", "text": "{time}", "size": 12, "weight": "semibold", "align": "center" },
                { "type": "text", "text": "{weekday}", "size": 7.5, "color": "dim", "align": "center" }
              ]
            },
            "default": {
              "rows": [
                { "type": "text", "text": "{time}", "size": 17, "weight": "semibold" },
                { "type": "text", "text": "{month} 月 {day} 日 · {weekday}", "size": 8.5, "color": "dim" }
              ]
            },
            "1x6": {
              "rows": [
                {
                  "type": "row",
                  "cells": [
                    { "type": "icon", "iconPreset": "todo", "size": 13 },
                    { "type": "text", "text": "今天是 {month} 月 {day} 日", "size": 11, "flex": 1 },
                    { "type": "text", "text": "{time}", "size": 14, "weight": "semibold", "align": "right" }
                  ]
                }
              ]
            }
          }
        }
        """;

    // ===== 轻量 JSON 读取 =====

    /// <summary>包装 JsonElement，做到大小写不敏感、缺字段给默认值，省掉一堆 TryGetProperty。</summary>
    private sealed class JObj {
        private readonly Dictionary<string, JsonElement> _map;

        public JObj(JsonElement element) {
            _map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            if (element.ValueKind != JsonValueKind.Object)
                return;

            foreach (var property in element.EnumerateObject())
                _map[property.Name] = property.Value;
        }

        public string Str(string name, string fallback = "") =>
            _map.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;

        public double Num(string name, double fallback) {
            if (!_map.TryGetValue(name, out var value))
                return fallback;

            return value.ValueKind switch {
                JsonValueKind.Number when value.TryGetDouble(out double number) => number,
                JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double parsed) => parsed,
                _ => fallback
            };
        }

        public bool Bool(string name, bool fallback = false) {
            if (!_map.TryGetValue(name, out var value))
                return fallback;

            return value.ValueKind switch {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when value.TryGetInt32(out int number) => number != 0,
                JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
                _ => fallback
            };
        }

        public JObj? Obj(string name) =>
            _map.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Object
                ? new JObj(value)
                : null;

        /// <summary>对象数组（sizes 之类）。</summary>
        public List<JObj> Arr(string name) {
            if (!_map.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return new List<JObj>();

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new JObj(item))
                .ToList();
        }

        /// <summary>数字数组（pad 之类）。</summary>
        public List<double> Nums(string name) {
            if (!_map.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return new List<double>();

            var list = new List<double>();

            foreach (var item in value.EnumerateArray()) {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double number))
                    list.Add(number);
                else if (item.ValueKind == JsonValueKind.String &&
                         double.TryParse(item.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                             out double parsed))
                    list.Add(parsed);
            }

            return list;
        }

        /// <summary>子对象里的键值对（data / layout）。</summary>
        public List<KeyValuePair<string, JObj>> Entries(string name) {
            var list = new List<KeyValuePair<string, JObj>>();

            if (!_map.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Object)
                return list;

            foreach (var property in value.EnumerateObject()) {
                if (property.Value.ValueKind == JsonValueKind.Object)
                    list.Add(new KeyValuePair<string, JObj>(property.Name, new JObj(property.Value)));
            }

            return list;
        }
    }
}
