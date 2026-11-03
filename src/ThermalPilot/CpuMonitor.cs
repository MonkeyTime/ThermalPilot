using LibreHardwareMonitor.Hardware;

namespace ThermalPilot;

public record CpuReading(double? Temperature, double? Load, string Cpu, string Sensor,
    int DeviceCount = 1, int DevicesWithTemperature = 1);

public sealed class CpuMonitor : IDisposable
{
    private readonly Computer _computer = new() { IsCpuEnabled = true };
    private bool _open;

    public CpuReading Read()
    {
        if (!_open) { _computer.Open(); _open = true; }
        var devices = new List<CpuDeviceSample>();
        var loads = new List<double>();
        var names = new List<string>();
        foreach (var cpu in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
        {
            names.Add(cpu.Name);
            var temperatures = new List<CpuTemperature>();
            int loadStart = loads.Count;
            Collect(cpu, temperatures, loads);
            devices.Add(new(cpu.Identifier.ToString(), cpu.Name, temperatures,
                loads.Count > loadStart ? loads.Skip(loadStart).Average() : null));
        }
        // Hottest CPU temperature, including individual cores; never substitute motherboard ACPI zones.
        var summary = CpuTelemetry.Summarize(devices);
        return new(summary.Temperature, loads.Count > 0 ? loads.Average() : null,
            names.Count > 0 ? string.Join(" / ", names) : "Processeur non détecté",
            summary.Sensor, summary.DeviceCount, summary.DevicesWithTemperature);
    }

    private static void Collect(IHardware hardware, List<CpuTemperature> temperatures, List<double> loads)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Temperature) temperatures.Add(new(sensor.Name, sensor.Value));
            if (sensor.SensorType == SensorType.Load && sensor.Name == "CPU Total" && sensor.Value is float load && float.IsFinite(load)) loads.Add(load);
        }
        foreach (var child in hardware.SubHardware) Collect(child, temperatures, loads);
    }

    public void Dispose() { if (_open) _computer.Close(); }
}
