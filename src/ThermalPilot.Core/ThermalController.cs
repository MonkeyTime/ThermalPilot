namespace ThermalPilot.Core;

public enum ThermalState { Ready, Cooling, Holding, Recovering, SensorLost }
public record Decision(int Limit, ThermalState State);

/// <summary>Call once per second. Percentages are Windows performance ceilings, not measured CPU load.</summary>
public sealed class ThermalController
{
    public const int MinimumLimit = 15;
    private int _coolSamples;
    public int Limit { get; private set; } = 100;

    public static bool IsValid(double? temperature) =>
        temperature is double t && double.IsFinite(t) && t is > 0 and <= 125;

    public Decision Step(double? temperature, int target)
    {
        if (target is < 45 or > 95) throw new ArgumentOutOfRangeException(nameof(target));
        if (!IsValid(temperature))
        {
            _coolSamples = 0;
            Limit = MinimumLimit;
            return new(Limit, ThermalState.SensorLost);
        }

        double delta = temperature!.Value - target;
        if (delta > 0)
        {
            _coolSamples = 0;
            int reduction = delta >= 10 ? 20 : delta >= 5 ? 10 : 5;
            Limit = Math.Max(MinimumLimit, Limit - reduction);
            return new(Limit, ThermalState.Cooling);
        }
        // A 3°C dead band and 4 consecutive cool samples prevent oscillation.
        if (delta <= -3 && Limit < 100)
        {
            if (++_coolSamples >= 4)
            {
                Limit = Math.Min(100, Limit + 2);
                _coolSamples = 0;
            }
            return new(Limit, ThermalState.Recovering);
        }
        _coolSamples = 0;
        return new(Limit, Limit == 100 ? ThermalState.Ready : ThermalState.Holding);
    }

    public void Reset() { Limit = 100; _coolSamples = 0; }
}
