using ThermalPilot.Core;
using Xunit;

namespace ThermalPilot.Tests;

public sealed class CompatibilityTests
{
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void DiscoversStandardAndHybridWindowsClasses(int count)
    {
        var policy = ProcessorPerformancePolicy.Capture((id, ac) => ClassIndex(id) < count
            ? IsMinimum(id) ? 5u : ac ? 100u : 80u : null);
        Assert.Equal(count, policy.Classes.Count);
        var writes = policy.Limit(60).ToArray();
        Assert.Equal(count * 4, writes.Length);
        Assert.All(writes.Where(w => !IsMinimum(w.Setting)), w => Assert.Equal(60u, w.Value));
    }

    [Fact] public void PreservesDifferentSectorAndBatteryCeilingsDuringRecovery()
    {
        var policy = ProcessorPerformancePolicy.Capture((id, ac) => ClassIndex(id) == 0
            ? IsMinimum(id) ? 70u : ac ? 95u : 75u : null);
        Assert.All(policy.Limit(20), write => Assert.Equal(20u, write.Value));
        var recovered = policy.Limit(100).ToArray();
        Assert.Equal(95u, recovered.Single(w => w.OnAc && !IsMinimum(w.Setting)).Value);
        Assert.Equal(75u, recovered.Single(w => !w.OnAc && !IsMinimum(w.Setting)).Value);
        Assert.All(recovered.Where(w => IsMinimum(w.Setting)), w => Assert.Equal(70u, w.Value));
    }

    [Fact] public void RejectsMissingPrimaryControls()
        => Assert.Throws<NotSupportedException>(() => ProcessorPerformancePolicy.Capture((_, _) => null));

    [Fact] public void RejectsPartiallyExposedHybridClass()
        => Assert.Throws<NotSupportedException>(() => ProcessorPerformancePolicy.Capture((id, ac) =>
            ClassIndex(id) == 0 ? IsMinimum(id) ? 5u : 100u : ClassIndex(id) == 1 && ac ? 100u : null));

    [Fact] public void RejectsInconsistentWindowsPolicy()
        => Assert.Throws<NotSupportedException>(() => ProcessorPerformancePolicy.Capture((id, _) =>
            ClassIndex(id) == 0 ? IsMinimum(id) ? 90u : 80u : null));

    [Theory] [InlineData("Intel Core Ultra 9 275HX")] [InlineData("Intel Core i7 desktop")]
    [InlineData("AMD Ryzen mobile")] [InlineData("AMD Ryzen desktop")]
    public void SensorSelectionIsIndependentOfCpuModel(string model)
    {
        var result = CpuTelemetry.Summarize([new("/cpu/0", model,
            [new("CPU Package", 72), new("Core #1", 78), new("Core #2", null)], 50)]);
        Assert.Equal(78d, result.Temperature);
        Assert.Equal("Core #1", result.Sensor);
    }

    [Fact] public void MultipleSocketsUseHottestCpu()
    {
        var result = CpuTelemetry.Summarize([
            new("/cpu/0", "CPU 0", [new("Package", 65)], 20),
            new("/cpu/1", "CPU 1", [new("Package", 85)], 80)]);
        Assert.Equal(85d, result.Temperature);
        Assert.Equal(2, result.DevicesWithTemperature);
        Assert.Contains("CPU 1", result.Sensor);
    }

    [Fact] public void LossOfOneSocketDoesNotMaskMissingTemperature()
    {
        var result = CpuTelemetry.Summarize([
            new("/cpu/0", "CPU 0", [new("Package", 65)], 20),
            new("/cpu/1", "CPU 1", [new("Package", null)], 80)]);
        Assert.Null(result.Temperature);
        Assert.Equal(1, result.DevicesWithTemperature);
        Assert.Equal(2, result.DeviceCount);
    }

    [Fact] public void NoCpuCannotEnableRegulation()
        => Assert.Null(CpuTelemetry.Summarize([]).Temperature);

    private static bool IsMinimum(Guid id) => id.ToString().StartsWith("893dee8e", StringComparison.Ordinal);
    private static int ClassIndex(Guid id) => id.ToString()[^1] - 'c';
}
