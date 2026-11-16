namespace Aetherun.Core.Models;

public static class GeoMath
{
    public const double EarthRadiusMeters = 6371000.0;

    public static double Distance(GeoPoint a, GeoPoint b)
    {
        double p1 = a.Lat * Math.PI / 180.0;
        double p2 = b.Lat * Math.PI / 180.0;
        double dp = (b.Lat - a.Lat) * Math.PI / 180.0;
        double dl = (b.Lon - a.Lon) * Math.PI / 180.0;

        double h = Math.Sin(dp / 2) * Math.Sin(dp / 2)
                 + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        h = Math.Clamp(h, 0, 1);
        return 2.0 * EarthRadiusMeters * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1.0 - h));
    }

    /// <summary>把起终点换算成方位角（度，正北 0，顺时针）。</summary>
    public static double Bearing(GeoPoint a, GeoPoint b)
    {
        double p1 = a.Lat * Math.PI / 180.0;
        double p2 = b.Lat * Math.PI / 180.0;
        double dl = (b.Lon - a.Lon) * Math.PI / 180.0;
        double y = Math.Sin(dl) * Math.Cos(p2);
        double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        double deg = Math.Atan2(y, x) * 180.0 / Math.PI;
        return (deg + 360.0) % 360.0;
    }

    /// <summary>朝某个方位移动若干米后的坐标（用于没有轨迹时的直线走位）。</summary>
    public static GeoPoint Move(GeoPoint from, double bearingDeg, double meters)
    {
        double br = bearingDeg * Math.PI / 180.0;
        double d = meters / EarthRadiusMeters;
        double p1 = from.Lat * Math.PI / 180.0;
        double l1 = from.Lon * Math.PI / 180.0;

        double p2 = Math.Asin(Math.Sin(p1) * Math.Cos(d) + Math.Cos(p1) * Math.Sin(d) * Math.Cos(br));
        double l2 = l1 + Math.Atan2(Math.Sin(br) * Math.Sin(d) * Math.Cos(p1),
                                    Math.Cos(d) - Math.Sin(p1) * Math.Sin(p2));

        return new GeoPoint(p2 * 180.0 / Math.PI, l2 * 180.0 / Math.PI);
    }
}
