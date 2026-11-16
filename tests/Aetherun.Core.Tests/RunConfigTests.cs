using Aetherun.Core.Models;
using Xunit;

namespace Aetherun.Core.Tests;

public class RunConfigTests
{
    [Fact]
    public void ClampsOutOfRangeValues()
    {
        var c = new RunConfig
        {
            TargetKm = 999, SpeedMps = 99, CadencePerMin = 5, LeadSeconds = -3,
            GravityLow = 20, GravityHigh = 1, EmulatorIndex = -2
        }.Sanitized();

        Assert.Equal(30, c.TargetKm);
        Assert.Equal(8.0, c.SpeedMps);
        Assert.Equal(60, c.CadencePerMin);
        Assert.Equal(0, c.LeadSeconds);
        Assert.True(c.GravityLow <= 8.5);
        Assert.True(c.GravityHigh >= 11.5);
        Assert.Equal(0, c.EmulatorIndex);
    }

    [Fact]
    public void KeepsZeroLeadAndIndexZero()
    {
        var c = new RunConfig { LeadSeconds = 0, EmulatorIndex = 0 }.Sanitized();
        Assert.Equal(0, c.LeadSeconds);
        Assert.Equal(0, c.EmulatorIndex);
    }

    [Fact]
    public void PresetFillsExpectedDefaults()
    {
        Assert.Equal(110, RunConfig.Preset("standard").CadencePerMin);
        Assert.Equal(90, RunConfig.Preset("slow").CadencePerMin);
        Assert.Equal(150, RunConfig.Preset("fast").CadencePerMin);
    }

    [Fact]
    public void CadenceWithinServerAcceptedRange()
    {
        var c = new RunConfig().Sanitized();
        Assert.InRange(c.CadencePerMin, 60, 300);
    }
}
