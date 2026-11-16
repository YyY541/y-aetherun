using System.Globalization;
using Aetherun.Core.Models;

namespace Aetherun.Desktop.Services;

public static class TrackLoopFactory
{
    /// <summary>默认跑道：与真机实测点位一致（WGS84，模拟器侧坐标系）。</summary>
    public static readonly string[] DefaultPoints =
    {
        "22.908578,113.869650",
        "22.909064,113.869674",
        "22.909544,113.869646",
        "22.909693,113.869513",
        "22.909758,113.869301",
        "22.909686,113.869094",
        "22.909534,113.868971",
        "22.909051,113.868957",
        "22.908558,113.868992",
        "22.908425,113.869132",
        "22.908367,113.869319",
        "22.908450,113.869555",
    };

    public static TrackLoop Default() => new(DefaultPoints.Select(ParseOne));

    public static string DefaultText() => string.Join(Environment.NewLine, DefaultPoints);

    /// <summary>把一组点位还原成编辑框里的文本（每行「纬度,经度」，6 位小数）。</summary>
    public static string ToText(TrackLoop loop) => string.Join(Environment.NewLine,
        loop.Points.Select(p => $"{p.Lat.ToString("F6", CultureInfo.InvariantCulture)},{p.Lon.ToString("F6", CultureInfo.InvariantCulture)}"));

    public static GeoPoint ParseOne(string line)
    {
        var parts = line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new FormatException("每行需要「纬度,经度」");
        double lat = double.Parse(parts[0], CultureInfo.InvariantCulture);
        double lon = double.Parse(parts[1], CultureInfo.InvariantCulture);
        return new GeoPoint(lat, lon);
    }

    public static TrackLoop? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var pts = new List<GeoPoint>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            try { pts.Add(ParseOne(line)); }
            catch { return null; }
        }
        if (pts.Count < 3) return null;
        try { return new TrackLoop(pts); }
        catch { return null; }
    }
}
