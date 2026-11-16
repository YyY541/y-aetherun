using System.Globalization;

namespace Aetherun.Core.Models;

/// <summary>WGS84 坐标点（模拟器侧坐标系，非高德 GCJ-02）。</summary>
public readonly record struct GeoPoint(double Lat, double Lon)
{
    // 必须固定用 InvariantCulture：德语/法语等区域的数字小数点是逗号，
    // 拼出来会变成 "113,86,22,90"，--LLI 与 --value 都会被拆错参数。
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6}", Lat, Lon);

    /// <summary>ldconsole 的 --LLI 参数顺序是经度在前。</summary>
    public string ToLli() =>
        string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6}", Lon, Lat);
}
