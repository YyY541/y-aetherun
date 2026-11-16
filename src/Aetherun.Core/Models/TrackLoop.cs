namespace Aetherun.Core.Models;

/// <summary>闭合跑道：由有序点位构成，支持按累计里程取插值坐标。</summary>
public sealed class TrackLoop
{
    private readonly GeoPoint[] _pts;
    private readonly double[] _seg;   // 每段长度（米）
    private readonly double[] _cum;   // 每段起点累计里程（米）

    public TrackLoop(IEnumerable<GeoPoint> points)
    {
        _pts = points?.ToArray() ?? throw new ArgumentNullException(nameof(points));
        if (_pts.Length < 3)
            throw new ArgumentException("闭合跑道至少需要 3 个点", nameof(points));

        int n = _pts.Length;
        _seg = new double[n];
        _cum = new double[n];

        double acc = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double d = GeoMath.Distance(_pts[i], _pts[j]);
            _seg[i] = d;
            _cum[i] = acc;
            acc += d;
        }
        Perimeter = acc;
    }

    public int Count => _pts.Length;
    public double Perimeter { get; }
    public IReadOnlyList<GeoPoint> Points => _pts;

    /// <summary>按累计里程取点，超出周长自动回环。</summary>
    public GeoPoint At(double distanceMeters)
    {
        double x = distanceMeters % Perimeter;
        if (x < 0) x += Perimeter;

        for (int i = 0; i < _pts.Length; i++)
        {
            double next = (i == _pts.Length - 1) ? Perimeter : _cum[i + 1];
            if (x >= next) continue;

            double f = _seg[i] > 0.001 ? (x - _cum[i]) / _seg[i] : 0;
            f = Math.Clamp(f, 0, 1);

            int j = (i + 1) % _pts.Length;
            return new GeoPoint(
                _pts[i].Lat + (_pts[j].Lat - _pts[i].Lat) * f,
                _pts[i].Lon + (_pts[j].Lon - _pts[i].Lon) * f);
        }
        return _pts[0];
    }

    /// <summary>第 index 圈的剩余可用里程（用于「跑 N 圈」这种表达）。</summary>
    public double Laps(double meters) => Perimeter <= 0 ? 0 : meters / Perimeter;
}
