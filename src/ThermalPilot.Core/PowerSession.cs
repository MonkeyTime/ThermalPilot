using System.Text.Json;

namespace ThermalPilot.Core;

public interface IPowerPlatform
{
    Guid ActiveScheme { get; }
    void Duplicate(Guid source, Guid destination);
    void Activate(Guid scheme);
    void Delete(Guid scheme);
    void SetLimit(Guid scheme, int percent);
}

public record SessionJournal(Guid Id, Guid Original, Guid Temporary, int OwnerPid,
    long OwnerStartTicks, DateTimeOffset Heartbeat);

/// <summary>Caller serializes access across the UI process and recovery process.</summary>
public sealed class PowerSession(IPowerPlatform platform, string journalPath)
{
    public SessionJournal? Read() => File.Exists(journalPath)
        ? JsonSerializer.Deserialize<SessionJournal>(File.ReadAllText(journalPath))
            ?? throw new IOException("Journal de restauration vide.")
        : null;

    private void Save(SessionJournal journal)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
        string temporary = journalPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal));
        File.Move(temporary, journalPath, true);
    }

    public SessionJournal Prepare(int pid, long startTicks)
    {
        if (Read() is not null) throw new InvalidOperationException("Une session doit d’abord être restaurée.");
        var journal = new SessionJournal(Guid.NewGuid(), platform.ActiveScheme, Guid.NewGuid(),
            pid, startTicks, DateTimeOffset.UtcNow);
        // Journal precedes all mutations, including duplication, so interrupted startup is recoverable.
        Save(journal);
        try { platform.Duplicate(journal.Original, journal.Temporary); }
        catch { Restore(); throw; }
        return journal;
    }

    public void Start(Guid id, int initialLimit)
    {
        var journal = Require(id);
        if (platform.ActiveScheme != journal.Original)
            throw new InvalidOperationException("Le profil Windows a changé pendant le démarrage.");
        platform.SetLimit(journal.Temporary, initialLimit);
        platform.Activate(journal.Temporary);
        Save(journal with { Heartbeat = DateTimeOffset.UtcNow });
    }

    public void Apply(Guid id, int limit)
    {
        var journal = Require(id);
        if (platform.ActiveScheme != journal.Temporary)
            throw new InvalidOperationException("Le profil d’alimentation a été changé hors de Thermal Pilot. La régulation est arrêtée.");
        platform.SetLimit(journal.Temporary, limit);
        platform.Activate(journal.Temporary);
        Save(journal with { Heartbeat = DateTimeOffset.UtcNow });
    }

    public void Heartbeat(Guid id)
    {
        var journal = Require(id);
        if (platform.ActiveScheme != journal.Temporary)
            throw new InvalidOperationException("Le profil Windows a changé. La régulation est arrêtée.");
        Save(journal with { Heartbeat = DateTimeOffset.UtcNow });
    }

    private SessionJournal Require(Guid id)
    {
        var journal = Read();
        return journal?.Id == id ? journal : throw new InvalidOperationException("La session a été restaurée par le processus de secours.");
    }

    public void Restore()
    {
        var journal = Read();
        if (journal is null) return;
        // Respect a power plan explicitly selected by the user while the application was running.
        if (platform.ActiveScheme == journal.Temporary) platform.Activate(journal.Original);
        platform.Delete(journal.Temporary);
        // Keep the journal when recovery fails, allowing retry at next launch.
        File.Delete(journalPath);
    }
}
