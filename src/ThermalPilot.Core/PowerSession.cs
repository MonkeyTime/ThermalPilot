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

public record ManagedScheme(Guid Original, Guid Temporary);
public record PowerSynchronization(bool ProfileChanged, bool Pending);
public record SessionJournal(Guid Id, Guid Original, Guid Temporary, int OwnerPid,
    long OwnerStartTicks, DateTimeOffset Heartbeat, IReadOnlyList<ManagedScheme>? Schemes = null, int Limit = 100);

/// <summary>Caller serializes access across the UI process and recovery process.</summary>
public sealed class PowerSession(IPowerPlatform platform, string journalPath)
{
    public SessionJournal? Read()
    {
        string path = File.Exists(journalPath) ? journalPath : journalPath + ".bak";
        if (!File.Exists(path)) return null;
        try { return ReadJournal(path); }
        catch (JsonException ex)
        {
            if (path == journalPath && File.Exists(journalPath + ".bak"))
            {
                try { return ReadJournal(journalPath + ".bak"); }
                catch (JsonException) { }
            }
            throw new IOException("Le journal de restauration est endommagé et aucune copie de secours valide n’est disponible. " +
                "Conserve le journal pour le diagnostic et sélectionne ton profil d’alimentation habituel dans Windows.", ex);
        }
    }

    private static SessionJournal ReadJournal(string path)
    {
        var journal = JsonSerializer.Deserialize<SessionJournal>(File.ReadAllText(path))
            ?? throw new JsonException("Journal de restauration vide.");
        if (journal.Id == Guid.Empty || journal.Original == Guid.Empty || journal.Temporary == Guid.Empty ||
            journal.Original == journal.Temporary ||
            Managed(journal).Count == 0 || Managed(journal).Any(s => s.Original == Guid.Empty ||
                s.Temporary == Guid.Empty || s.Original == s.Temporary))
            throw new JsonException("Journal de restauration incomplet.");
        return journal;
    }

    private void Save(SessionJournal journal, bool recoveryChanged = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
        // Persist recovery metadata separately before creating or activating a clone.
        // Heartbeat updates must never overwrite this independent recovery copy.
        if (recoveryChanged) WriteJournal(journalPath + ".bak", journal);
        WriteJournal(journalPath, journal);
    }

    private static void WriteJournal(string path, SessionJournal journal)
    {
        string temporary = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, journal);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public SessionJournal Prepare(int pid, long startTicks)
    {
        if (Read() is not null) throw new InvalidOperationException("Une session doit d’abord être restaurée.");
        Guid original = platform.ActiveScheme, temporary = Guid.NewGuid();
        var journal = new SessionJournal(Guid.NewGuid(), original, temporary,
            pid, startTicks, DateTimeOffset.UtcNow, [new(original, temporary)]);
        // Journal precedes all mutations, including duplication, so interrupted startup is recoverable.
        Save(journal, recoveryChanged: true);
        try { platform.Duplicate(journal.Original, journal.Temporary); }
        catch { Restore(); throw; }
        return journal;
    }

    public void Start(Guid id, int initialLimit)
    {
        Apply(id, initialLimit);
    }

    public PowerSynchronization Apply(Guid id, int limit)
    {
        if (limit is < ThermalController.MinimumLimit or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        bool profileChanged = false;
        // OEM utilities may switch again during duplication. Follow their latest choice,
        // with bounded work per sample rather than disabling regulation or spinning forever.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var journal = Require(id);
            Guid active = platform.ActiveScheme;
            bool adopting = active != journal.Temporary;
            if (adopting)
            {
                var schemes = Managed(journal).ToList();
                // A game can restore a temporary scheme captured when it launched.
                // Resolve it to its source instead of cloning an already capped profile.
                var selected = schemes.FirstOrDefault(s => s.Temporary == active)
                    ?? schemes.FirstOrDefault(s => s.Original == active);
                bool duplicate = selected is null;
                selected ??= new ManagedScheme(active, Guid.NewGuid());
                if (duplicate) schemes.Add(selected);
                profileChanged |= selected.Original != journal.Original;
                journal = journal with
                {
                    Original = selected.Original, Temporary = selected.Temporary,
                    Schemes = schemes, Heartbeat = DateTimeOffset.UtcNow
                };
                // Persist every owned clone before creation, so recovery can clean up
                // even if the process stops halfway through following a new profile.
                Save(journal, recoveryChanged: true);
                if (duplicate) platform.Duplicate(selected.Original, selected.Temporary);
            }

            if (adopting || limit != journal.Limit)
            {
                platform.SetLimit(journal.Temporary, limit);
                if (platform.ActiveScheme != active) continue;
                platform.Activate(journal.Temporary);
            }
            Save(journal with { Limit = limit, Heartbeat = DateTimeOffset.UtcNow });
            if (platform.ActiveScheme == journal.Temporary) return new(profileChanged, false);
        }
        return new(profileChanged, true);
    }

    public PowerSynchronization Heartbeat(Guid id) => Apply(id, Require(id).Limit);

    private static IReadOnlyList<ManagedScheme> Managed(SessionJournal journal) =>
        journal.Schemes ?? [new(journal.Original, journal.Temporary)];

    private SessionJournal Require(Guid id)
    {
        var journal = Read();
        return journal?.Id == id ? journal : throw new InvalidOperationException("La session a été restaurée par le processus de secours.");
    }

    public void Restore()
    {
        var journal = Read();
        if (journal is null) return;
        var schemes = Managed(journal);
        Guid active = platform.ActiveScheme;
        var current = schemes.FirstOrDefault(s => s.Temporary == active);
        // Restore the source of whichever managed clone is actually active. Leave
        // an external profile alone if the application has not adopted it yet.
        if (current is not null) platform.Activate(current.Original);
        foreach (var scheme in schemes) platform.Delete(scheme.Temporary);
        // Keep the journal when recovery fails, allowing retry at next launch.
        File.Delete(journalPath);
        File.Delete(journalPath + ".bak");
    }
}
