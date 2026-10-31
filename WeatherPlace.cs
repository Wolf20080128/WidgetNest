using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace WidgetNest;

/// <summary>
/// 天气组件选中的位置：省 / 市 / 区县三级里的最末一级。
/// 坐标才是真正的身份（同名区县靠它区分），名字只负责显示；
/// 早先版本存下的位置可能没有坐标，这时 <see cref="HasCoords"/> 为 false，仍然按名字查天气。
/// </summary>
public sealed class WeatherPlace {
    public string Name { get; set; } = "";

    /// <summary>上级行政区，例如「杭州市 · 浙江省」；直辖市的区只写「北京市」。</summary>
    public string Region { get; set; } = "";

    public string Country { get; set; } = "中国";

    /// <summary>行政区划代码，重新打开选择窗时用它还原选中路径。</summary>
    public string Code { get; set; } = "";

    public double Lat { get; set; }

    public double Lon { get; set; }

    public bool HasCoords { get; set; }

    [JsonIgnore]
    public bool IsEmpty => (Name ?? "").Trim().Length == 0;

    /// <summary>天气缓存的键：换了位置，旧数据立刻作废。</summary>
    [JsonIgnore]
    public string Key => HasCoords
        ? string.Create(CultureInfo.InvariantCulture, $"{Lat:0.###},{Lon:0.###}")
        : "name:" + (Name ?? "").Trim().ToLowerInvariant();

    [JsonIgnore]
    public string Subtitle {
        get {
            var parts = new List<string>(2);

            string region = (Region ?? "").Trim();
            string country = (Country ?? "").Trim();

            if (region.Length > 0)
                parts.Add(region);

            if (country.Length > 0)
                parts.Add(country);

            return string.Join(" · ", parts);
        }
    }

    [JsonIgnore]
    public string FullLabel => Subtitle.Length == 0 ? Name : $"{Name} · {Subtitle}";

    /// <summary>交给天气接口的查询目标：有坐标就用坐标，没有才退回名字。</summary>
    [JsonIgnore]
    public string QueryTarget => HasCoords
        ? string.Create(CultureInfo.InvariantCulture, $"{Lat:0.####},{Lon:0.####}")
        : (Name ?? "").Trim();

    public WeatherPlace Clone() => new() {
        Name = Name,
        Region = Region,
        Country = Country,
        Code = Code,
        Lat = Lat,
        Lon = Lon,
        HasCoords = HasCoords
    };

    /// <summary>两个位置是否是同一个：有坐标比坐标，没坐标比名字。</summary>
    public static bool Same(WeatherPlace? a, WeatherPlace? b) {
        bool aEmpty = a is null || a.IsEmpty;
        bool bEmpty = b is null || b.IsEmpty;

        if (aEmpty || bEmpty)
            return aEmpty && bEmpty;

        return string.Equals(a!.Key, b!.Key, StringComparison.Ordinal);
    }
}
