using Aetherun.Core.Models;
using Xunit;

namespace Aetherun.Core.Tests;

public class RunSnapshotTests
{
    [Fact]
    public void ProgressClampsToOne()
    {
        var s = new RunSnapshot { DistanceMeters = 3000, TargetMeters = 2000 };
        Assert.Equal(1.0, s.Progress);
    }

    [Fact]
    public void ProgressZeroWhenNoTarget()
    {
        Assert.Equal(0, new RunSnapshot().Progress);
    }

    [Fact]
    public void CadenceNeedsEnoughData()
    {
        Assert.Equal(0, new RunSnapshot { Steps = 100, ElapsedSeconds = 1 }.Cadence);
        var s = new RunSnapshot { Steps = 1653, ElapsedSeconds = 901.6 };
        Assert.InRange(s.Cadence, 109, 111);
    }

    [Fact]
    public void PaceMatchesExpectedRange()
    {
        var s = new RunSnapshot { DistanceMeters = 2200, ElapsedSeconds = 901.6 };
        Assert.InRange(s.PaceSecondsPerKm, 400, 420);
    }

    [Fact]
    public void PaceZeroWithNoDistance()
    {
        Assert.Equal(0, new RunSnapshot { DistanceMeters = 0, ElapsedSeconds = 30 }.PaceSecondsPerKm);
    }
}
