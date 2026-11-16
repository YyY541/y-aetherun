using Aetherun.Core.Models;
using Xunit;

namespace Aetherun.Core.Tests;

public class TrackLoopTests
{
    private static readonly (double Lat, double Lon)[] Real =
    {
        (22.908578, 113.869650), (22.909064, 113.869674), (22.909544, 113.869646),
        (22.909693, 113.869513), (22.909758, 113.869301), (22.909686, 113.869094),
        (22.909534, 113.868971), (22.909051, 113.868957), (22.908558, 113.868992),
        (22.908425, 113.869132), (22.908367, 113.869319), (22.908450, 113.869555),
    };

    private static TrackLoop Make() => new(Real.Select(p => new GeoPoint(p.Lat, p.Lon)));

    [Fact]
    public void PerimeterMatchesMeasuredTrack()
    {
        var t = Make();
        Assert.InRange(t.Perimeter, 384, 392);
    }

    [Fact]
    public void AtZeroReturnsFirstPoint()
    {
        var t = Make();
        var p = t.At(0);
        Assert.Equal(Real[0].Lat, p.Lat, 6);
        Assert.Equal(Real[0].Lon, p.Lon, 6);
    }

    [Fact]
    public void AtPerimeterWrapsToStart()
    {
        var t = Make();
        var p = t.At(t.Perimeter);
        Assert.Equal(Real[0].Lat, p.Lat, 6);
    }

    [Fact]
    public void ConsecutiveFixesAreCloselySpaced()
    {
        var t = Make();
        // 每秒 2.44 米，插值点之间的实际距离应当与设定速度一致
        for (double d = 0; d < t.Perimeter; d += 2.44)
        {
            var a = t.At(d);
            var b = t.At(d + 2.44);
            Assert.InRange(GeoMath.Distance(a, b), 2.0, 2.9);
        }
    }

    [Fact]
    public void NegativeDistanceIsNormalized()
    {
        var t = Make();
        var neg = t.At(-5);
        var pos = t.At(t.Perimeter - 5);
        Assert.Equal(pos.Lat, neg.Lat, 6);
        Assert.Equal(pos.Lon, neg.Lon, 6);
    }

    [Fact]
    public void TooFewPointsRejected()
    {
        Assert.Throws<ArgumentException>(() => new TrackLoop(new[]
        {
            new GeoPoint(1, 1), new GeoPoint(2, 2)
        }));
    }

    [Fact]
    public void LliUsesLongitudeFirst()
    {
        var p = new GeoPoint(22.905691, 113.874572);
        Assert.Equal("113.874572,22.905691", p.ToLli());
    }
}
