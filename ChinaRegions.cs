using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetNest;

/// <summary>
/// 一个行政区节点：省 / 市 / 区县通用。
/// 三级用同一类型，靠 <see cref="Children"/> 是否为空区分层级——
/// 直辖市的下一级直接就是区，台湾省没有下级，东莞这类市也没有下级。
/// </summary>
internal sealed class RegionNode {
    /// <summary>行政区划代码，例如 330106。</summary>
    [JsonPropertyName("c")]
    public string Code { get; set; } = "";

    [JsonPropertyName("n")]
    public string Name { get; set; } = "";

    /// <summary>[纬度, 经度]，取行政区中心点。</summary>
    [JsonPropertyName("p")]
    public double[] Center { get; set; } = Array.Empty<double>();

    [JsonPropertyName("k")]
    public List<RegionNode> Children { get; set; } = new();

    [JsonIgnore]
    public double Lat => Center.Length > 1 ? Center[0] : 0;

    [JsonIgnore]
    public double Lon => Center.Length > 1 ? Center[1] : 0;

    [JsonIgnore]
    public bool HasChildren => Children.Count > 0;
}

/// <summary>
/// 省 / 市 / 区县三级行政区划。数据打包在程序集里（ChinaRegions.json，约 180KB），
/// 来源阿里 DataV 的行政区划边界（properties.center 即中心点），一次读入内存后常驻。
/// </summary>
internal static class ChinaRegions {
    private const string ResourceName = "WidgetNest.ChinaRegions.json";

    private static List<RegionNode>? _provinces;

    public static IReadOnlyList<RegionNode> Provinces => _provinces ??= Load();

    /// <summary>按区划代码找回选中路径：省 / 市（可能为空）/ 末级。</summary>
    public static (RegionNode Province, RegionNode? City, RegionNode Leaf)? PathOf(string code) {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        foreach (RegionNode province in Provinces) {
            if (province.Code == code)
                return (province, null, province);

            foreach (RegionNode city in province.Children) {
                if (city.Code == code)
                    return (province, null, city);

                foreach (RegionNode county in city.Children) {
                    if (county.Code == code)
                        return (province, city, county);
                }
            }
        }

        return null;
    }

    private static List<RegionNode> Load() {
        try {
            var assembly = typeof(ChinaRegions).Assembly;

            using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
                return new List<RegionNode>();

            return JsonSerializer.Deserialize<List<RegionNode>>(stream) ?? new List<RegionNode>();
        }
        catch {
            // 数据坏了就当没有：窗口会提示「数据缺失」，不至于让面板起不来
            return new List<RegionNode>();
        }
    }
}
