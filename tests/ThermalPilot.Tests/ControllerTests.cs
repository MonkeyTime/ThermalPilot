using ThermalPilot.Core;
using Xunit;

namespace ThermalPilot.Tests;

public sealed class ControllerTests
{
    [Fact] public void AtOrBelowTargetDoesNotThrottle()
    {
        var control = new ThermalController();
        Assert.Equal(new Decision(100, ThermalState.Ready), control.Step(75, 75));
        Assert.Equal(100, control.Step(60, 75).Limit);
    }

    [Theory]
    [InlineData(76, 95)] [InlineData(80, 90)] [InlineData(90, 80)]
    public void OvershootReducesCeilingImmediately(double temperature, int limit)
        => Assert.Equal(limit, new ThermalController().Step(temperature, 75).Limit);

    [Fact] public void ContinuousHeatSaturatesAtFloor()
    {
        var control = new ThermalController();
        for (int i = 0; i < 100; i++) control.Step(100, 75);
        Assert.Equal(15, control.Limit);
    }

    [Fact] public void RecoveryRequiresFourConsecutiveCoolSamples()
    {
        var control = new ThermalController();
        control.Step(85, 75);
        for (int i = 0; i < 3; i++) Assert.Equal(80, control.Step(72, 75).Limit);
        Assert.Equal(80, control.Step(74, 75).Limit); // breaks the dwell
        for (int i = 0; i < 3; i++) Assert.Equal(80, control.Step(72, 75).Limit);
        Assert.Equal(82, control.Step(72, 75).Limit);
        for (int i = 0; i < 100; i++) control.Step(60, 75);
        Assert.Equal(100, control.Limit);
    }

    [Theory]
    [InlineData(null)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)] [InlineData(0d)] [InlineData(126d)]
    public void InvalidSensorsUseFailSafeAndRecoverSlowly(double? temperature)
    {
        var control = new ThermalController();
        Assert.Equal(new Decision(15, ThermalState.SensorLost), control.Step(temperature, 75));
        Assert.Equal(15, control.Step(70, 75).Limit);
    }

    [Fact] public void ChangedTargetTakesEffectOnNextSample()
    {
        var control = new ThermalController();
        Assert.Equal(100, control.Step(70, 75).Limit);
        Assert.Equal(80, control.Step(70, 60).Limit);
        control.Reset();
        Assert.Equal(100, control.Limit);
    }

    [Theory] [InlineData(44)] [InlineData(96)]
    public void RejectsOutOfRangeTarget(int target)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalController().Step(65, target));
}
