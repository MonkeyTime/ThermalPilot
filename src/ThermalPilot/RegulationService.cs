namespace ThermalPilot;

public record Snapshot(CpuReading Reading, int Limit, string Status, string Detail, bool Active, bool Error);

public sealed class RegulationService(bool demo) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CpuMonitor _monitor = new();
    private readonly ThermalController _controller = new();
    private Guid? _sessionId;
    private bool _demoActive;
    private int _target = 75;
    private CpuReading? _lastReading;
    private long _lastReadingAt;
    private int _demoTick;
    private int _lastApplied = 100;
    private int _probeTick;
    private PowerSupport _powerSupport = new(false, "Vérification du contrôle Windows…", Array.Empty<int>());
    public bool PowerSettingsAvailable => demo || _powerSupport.Available;
    public string Compatibility { get; private set; } = "Vérification des sondes et du contrôle Windows…";
    public bool Active => _sessionId is not null || _demoActive;
    public int Target { get => Volatile.Read(ref _target); set => Volatile.Write(ref _target, Math.Clamp(value, 45, 95)); }

    public async Task<Snapshot> SampleAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                CpuReading reading;
                try
                {
                    reading = demo ? DemoReading() : _monitor.Read();
                    _lastReading = reading;
                    _lastReadingAt = Stopwatch.GetTimestamp();
                }
                catch (Exception ex)
                {
                    Recovery.Log(ex);
                    reading = new(null, null, _lastReading?.Cpu ?? "Processeur", "Lecture des sondes impossible", _lastReading?.DeviceCount ?? 0, 0);
                    _lastReading = reading;
                }

                if (!demo && !Active && _probeTick++ % 10 == 0) _powerSupport = WindowsPowerPlatform.Probe();
                Compatibility = demo ? "Intel / AMD · portables et PC fixes · aperçu simulé"
                    : _powerSupport.Detail + $" · Sondes CPU : {reading.DevicesWithTemperature}/{reading.DeviceCount}";

                if (!Active) return new Snapshot(reading, 100, "En veille",
                    !PowerSettingsAvailable ? _powerSupport.Detail
                        : ThermalController.IsValid(reading.Temperature) ? "Choisis ta température cible, puis active la régulation."
                        : "Sonde CPU indisponible. Lance en administrateur et vérifie l’installation de PawnIO.", false, false);

                var decision = _controller.Step(reading.Temperature, Target);
                try
                {
                    if (_sessionId is Guid id)
                        Recovery.Locked(session =>
                        {
                            if (_lastApplied != decision.Limit) session.Apply(id, decision.Limit);
                            else session.Heartbeat(id);
                            _lastApplied = decision.Limit;
                            return true;
                        });
                }
                catch (Exception ex)
                {
                    Recovery.Log(ex);
                    StopCore();
                    return new Snapshot(reading, 100, "Régulation interrompue", ex.Message, false, true);
                }
                var (status, detail) = decision.State switch
                {
                    ThermalState.Cooling => ("Bridage automatique", "La cible est dépassée. Le plafond CPU diminue progressivement."),
                    ThermalState.Holding => ("Plafond maintenu", "La température se stabilise dans la zone de régulation."),
                    ThermalState.Recovering => ("Retour progressif", "Le CPU refroidit. Les performances remontent par petits paliers."),
                    ThermalState.SensorLost => ("Sonde indisponible", "Plafond de secours à 15 % jusqu’au retour d’une mesure valide. Tu peux arrêter la régulation."),
                    _ => ("Sous la cible", "Les performances du profil Windows sont disponibles.")
                };
                if (decision.Limit == ThermalController.MinimumLimit && decision.State == ThermalState.Cooling)
                    detail = "Plafond minimum atteint. La cible reste dépassée ; le refroidissement peut être insuffisant.";
                return new Snapshot(reading, decision.Limit, status, detail, true, decision.State == ThermalState.SensorLost);
            });
        }
        finally { _gate.Release(); }
    }

    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (Active) return;
                if (_lastReading is null || !ThermalController.IsValid(_lastReading.Temperature)
                    || Stopwatch.GetElapsedTime(_lastReadingAt) > TimeSpan.FromSeconds(3))
                    throw new InvalidOperationException("Une mesure CPU valide et récente est nécessaire.");
                _controller.Reset();
                if (demo) { _demoActive = true; return; }
                if (!Recovery.IsAdmin) throw new InvalidOperationException("Relance l’application en administrateur pour activer la régulation.");
                _powerSupport = WindowsPowerPlatform.Probe();
                if (!_powerSupport.Available) throw new NotSupportedException(_powerSupport.Detail);
                using var process = Process.GetCurrentProcess();
                SessionJournal journal = Recovery.Locked(session => session.Prepare(process.Id, process.StartTime.ToUniversalTime().Ticks));
                try
                {
                    Recovery.StartWatchdog(journal);
                    int limit = _controller.Step(_lastReading.Temperature, Target).Limit;
                    Recovery.Locked(session => { session.Start(journal.Id, limit); return true; });
                    _lastApplied = limit;
                    _sessionId = journal.Id;
                }
                catch { Recovery.Restore(); throw; }
            });
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { await Task.Run(StopCore); }
        finally { _gate.Release(); }
    }

    private void StopCore()
    {
        if (_sessionId is not null) Recovery.Restore();
        _sessionId = null;
        _demoActive = false;
        _controller.Reset();
    }

    private CpuReading DemoReading()
    {
        _demoTick++;
        double t = 72 + 9 * Math.Sin(_demoTick / 9d) - (100 - _controller.Limit) * .18;
        return new(t, 45 + 20 * Math.Sin(_demoTick / 7d), "CPU de démonstration", "Sonde simulée");
    }

    public void Dispose() { StopCore(); _monitor.Dispose(); _gate.Dispose(); }
}
