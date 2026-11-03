namespace ThermalPilot.Core;

public record CpuDeviceSample(string Id, string Name, IReadOnlyList<CpuTemperature> Temperatures, double? Load);
public record CpuTemperature(string Name, double? Value);
public record CpuTemperatureSummary(double? Temperature, string Sensor, int DeviceCount, int DevicesWithTemperature);

public static class CpuTelemetry
{
    public static CpuTemperatureSummary Summarize(IReadOnlyList<CpuDeviceSample> devices)
    {
        var valid = devices.Select(device => new
        {
            Device = device,
            Hottest = device.Temperatures.Where(t => ThermalController.IsValid(t.Value))
                .OrderByDescending(t => t.Value).FirstOrDefault()
        }).ToArray();
        int covered = valid.Count(device => device.Hottest is not null);
        if (covered != devices.Count || covered == 0)
            return new(null, "Sondes CPU disponibles : " + covered + "/" + devices.Count, devices.Count, covered);
        var hottest = valid.OrderByDescending(device => device.Hottest!.Value).First();
        return new(hottest.Hottest!.Value,
            devices.Count > 1 ? hottest.Device.Name + " · " + hottest.Hottest.Name : hottest.Hottest.Name,
            devices.Count, covered);
    }
}
