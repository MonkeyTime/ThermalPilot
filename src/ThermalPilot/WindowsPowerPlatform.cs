using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ThermalPilot;

public record PowerSupport(bool Available, string Detail, IReadOnlyList<int> Classes);

public sealed class WindowsPowerPlatform : IPowerPlatform
{
    private static readonly Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00");
    private readonly Dictionary<Guid, ProcessorPerformancePolicy> _policies = new();
    private readonly Dictionary<Guid, int> _applied = new();

    public static PowerSupport Probe()
    {
        try
        {
            if (RuntimeInformation.OSArchitecture != Architecture.X64)
                return new(false, "Cette version nécessite Windows sur processeur Intel ou AMD x64.", Array.Empty<int>());
            var platform = new WindowsPowerPlatform();
            var policy = platform.Inspect(platform.ActiveScheme);
            var classes = policy.Classes.Select(c => c.Index).ToArray();
            return new(true, $"Réglages Windows accessibles · {classes.Length} classe(s) de performances", classes);
        }
        catch (Exception ex) when (ex is Win32Exception or NotSupportedException)
        {
            return new(false, "Contrôle Windows indisponible : " + ex.Message, Array.Empty<int>());
        }
    }

    public Guid ActiveScheme
    {
        get
        {
            Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer));
            try { return Marshal.PtrToStructure<Guid>(pointer); }
            finally { LocalFree(pointer); }
        }
    }

    public void Duplicate(Guid source, Guid destination)
    {
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        var pointer = buffer;
        try
        {
            Marshal.StructureToPtr(destination, buffer, false);
            Check(PowerDuplicateScheme(IntPtr.Zero, ref source, ref pointer));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Activate(Guid scheme) => Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme));

    public void Delete(Guid scheme)
    {
        uint result = PowerDeleteScheme(IntPtr.Zero, ref scheme);
        // ERROR_FILE_NOT_FOUND: startup may have stopped before the duplicate was created.
        if (result is not 0 and not 2) Check(result);
        _applied.Remove(scheme);
        _policies.Remove(scheme);
    }

    public void SetLimit(Guid scheme, int percent)
    {
        if (percent is < 15 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        if (_applied.TryGetValue(scheme, out var last) && last == percent) return;
        if (!_policies.TryGetValue(scheme, out var policy))
            _policies[scheme] = policy = Inspect(scheme);
        foreach (var write in policy.Limit(percent)) Write(scheme, write.Setting, write.OnAc, write.Value);
        _applied[scheme] = percent;
    }

    public ProcessorPerformancePolicy Inspect(Guid scheme) => ProcessorPerformancePolicy.Capture((setting, ac) => Read(scheme, setting, ac));

    private static uint? Read(Guid scheme, Guid setting, bool ac)
    {
        Guid subgroup = Processor;
        uint value;
        uint error = ac ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value)
            : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);
        if (error == 2) return null;
        Check(error);
        return value;
    }

    private static void Write(Guid scheme, Guid setting, bool ac, uint value)
    {
        Guid subgroup = Processor;
        Check(ac ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value)
            : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value));
    }

    private static void Check(uint error)
    {
        if (error != 0) throw new Win32Exception((int)error);
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerDuplicateScheme(IntPtr root, ref Guid source, ref IntPtr destination);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
