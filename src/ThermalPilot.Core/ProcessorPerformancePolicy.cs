namespace ThermalPilot.Core;

public record PerformanceClass(int Index, Guid Minimum, Guid Maximum, uint AcMinimum,
    uint AcMaximum, uint DcMinimum, uint DcMaximum);
public record PerformanceWrite(Guid Setting, bool OnAc, uint Value);

/// <summary>Model-independent policy for standard and hybrid Windows processor classes.</summary>
public sealed class ProcessorPerformancePolicy(IReadOnlyList<PerformanceClass> classes)
{
    public IReadOnlyList<PerformanceClass> Classes { get; } = classes;

    public static ProcessorPerformancePolicy Capture(Func<Guid, bool, uint?> read)
    {
        var classes = new List<PerformanceClass>();
        for (int index = 0; index < 3; index++)
        {
            char suffix = (char)('c' + index);
            var min = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964" + suffix);
            var max = new Guid("bc5038f7-23e0-4960-96da-33abaf5935e" + suffix);
            uint? acMax = read(max, true), dcMax = read(max, false);
            uint? acMin = read(min, true), dcMin = read(min, false);
            if (index != 0 && acMax is null && dcMax is null && acMin is null && dcMin is null) continue;
            if (acMax is null || dcMax is null || acMin is null || dcMin is null)
                throw new NotSupportedException($"Réglages Windows incomplets pour la classe CPU {index}.");
            if (acMax > 100 || dcMax > 100 || acMin > acMax || dcMin > dcMax)
                throw new NotSupportedException($"Réglages Windows incohérents pour la classe CPU {index}.");
            classes.Add(new(index, min, max, acMin.Value, acMax.Value, dcMin.Value, dcMax.Value));
        }
        return new(classes.AsReadOnly());
    }

    public IEnumerable<PerformanceWrite> Limit(int percent)
    {
        if (percent is < 15 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        foreach (var group in Classes)
        {
            uint acCap = Math.Min(group.AcMaximum, (uint)percent);
            uint dcCap = Math.Min(group.DcMaximum, (uint)percent);
            yield return new(group.Minimum, true, Math.Min(group.AcMinimum, acCap));
            yield return new(group.Maximum, true, acCap);
            yield return new(group.Minimum, false, Math.Min(group.DcMinimum, dcCap));
            yield return new(group.Maximum, false, dcCap);
        }
    }
}
