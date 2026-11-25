using ThermalPilot.Core;
using Xunit;

namespace ThermalPilot.Tests;

public sealed class SessionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ThermalPilot-tests", Guid.NewGuid().ToString());
    private string PathName => Path.Combine(_directory, "journal.json");
    private readonly FakePower _power = new();

    [Fact] public void CloneIsModifiedAndOriginalRestored()
    {
        var original = _power.ActiveScheme;
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        Assert.Equal(original, _power.ActiveScheme);
        Assert.True(File.Exists(PathName));
        session.Start(journal.Id, 90);
        session.Apply(journal.Id, 70);
        Assert.Equal(journal.Temporary, _power.ActiveScheme);
        Assert.Equal(70, _power.Limits[journal.Temporary]);
        Assert.False(_power.Limits.ContainsKey(original));
        session.Restore();
        Assert.Equal(original, _power.ActiveScheme);
        Assert.DoesNotContain(journal.Temporary, _power.Schemes);
        Assert.False(File.Exists(PathName));
    }

    [Fact] public void NewProcessCanRecoverAfterCrash()
    {
        var original = _power.ActiveScheme;
        var before = new PowerSession(_power, PathName);
        var journal = before.Prepare(1, 1);
        before.Start(journal.Id, 15);
        new PowerSession(_power, PathName).Restore();
        Assert.Equal(original, _power.ActiveScheme);
        Assert.Throws<InvalidOperationException>(() => before.Apply(journal.Id, 100));
    }

    [Fact] public void ExternalPlanChangeKeepsRegulatingAndRestoresLatestSource()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 90);
        var external = Guid.NewGuid();
        _power.Schemes.Add(external);
        _power.Activate(external);
        var result = session.Apply(journal.Id, 50);
        var followed = session.Read()!;
        Assert.True(result.ProfileChanged);
        Assert.False(result.Pending);
        Assert.Equal(journal.Id, followed.Id);
        Assert.Equal(external, followed.Original);
        Assert.Equal(followed.Temporary, _power.ActiveScheme);
        Assert.Equal(50, _power.Limits[followed.Temporary]);
        Assert.False(_power.Limits.ContainsKey(external));
        session.Restore();
        Assert.Equal(external, _power.ActiveScheme);
        Assert.Equal(2, _power.Schemes.Count); // only untouched source profiles remain
    }

    [Fact] public void GameCanRestoreTemporaryProfileCapturedAtLaunch()
    {
        var session = new PowerSession(_power, PathName);
        var original = _power.ActiveScheme;
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 90);
        var game = AddProfile();
        _power.Activate(game);
        session.Apply(journal.Id, 50);
        _power.Activate(journal.Temporary); // game restores the exact GUID it captured
        var result = session.Apply(journal.Id, 50);
        Assert.True(result.ProfileChanged);
        Assert.Equal(50, _power.Limits[journal.Temporary]); // reapply even when global ceiling did not change
        Assert.Equal(original, session.Read()!.Original);
        Assert.Equal(2, session.Read()!.Schemes!.Count);
        session.Restore();
        Assert.Equal(original, _power.ActiveScheme);
        Assert.Equal(2, _power.Schemes.Count);
    }

    [Fact] public void RepeatedGameLaunchAndExitReusesClonesAndKeepsCeiling()
    {
        var session = new PowerSession(_power, PathName);
        var original = _power.ActiveScheme;
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 60);
        var game = AddProfile();
        for (int iteration = 0; iteration < 20; iteration++)
        {
            _power.Activate(game);
            session.Heartbeat(journal.Id);
            Assert.Equal(60, _power.Limits[_power.ActiveScheme]);
            _power.Activate(original);
            session.Heartbeat(journal.Id);
            Assert.Equal(60, _power.Limits[_power.ActiveScheme]);
        }
        Assert.Equal(2, session.Read()!.Schemes!.Count);
        Assert.Equal(4, _power.Schemes.Count);
        session.Restore();
        Assert.Equal(2, _power.Schemes.Count);
    }

    [Fact] public void CrashRecoveryRestoresLatestProfileAndDeletesEveryClone()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 80);
        var external = AddProfile();
        _power.Activate(external);
        session.Apply(journal.Id, 15);
        new PowerSession(_power, PathName).Restore();
        Assert.Equal(external, _power.ActiveScheme);
        Assert.Equal(2, _power.Schemes.Count);
        Assert.False(File.Exists(PathName));
    }

    [Fact] public void UnobservedExternalSelectionIsNotOverwrittenOnStop()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 80);
        var external = AddProfile();
        _power.Activate(external);
        session.Restore();
        Assert.Equal(external, _power.ActiveScheme);
    }

    [Fact] public void StableHeartbeatDoesNotReactivateOrRewritePowerPolicy()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 80);
        int activations = _power.Activations, writes = _power.LimitWrites;
        var result = session.Heartbeat(journal.Id);
        Assert.Equal(new PowerSynchronization(false, false), result);
        Assert.Equal(activations, _power.Activations);
        Assert.Equal(writes, _power.LimitWrites);
    }

    [Fact] public void ProfileCanChangeBetweenPrepareAndStart()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        var external = AddProfile();
        _power.Activate(external);
        session.Start(journal.Id, 80);
        Assert.Equal(external, session.Read()!.Original);
        Assert.Equal(80, _power.Limits[_power.ActiveScheme]);
        session.Restore();
        Assert.Equal(external, _power.ActiveScheme);
    }

    [Fact] public void RapidConcurrentChangesAreBoundedAndRetriedNextSample()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 90);
        _power.Activate(AddProfile());
        int writes = _power.LimitWrites;
        _power.BeforeSetLimit = () => _power.Activate(AddProfile());
        var result = session.Apply(journal.Id, 40);
        Assert.True(result.Pending);
        Assert.Equal(3, _power.LimitWrites - writes);
        Assert.True(File.Exists(PathName));
        _power.BeforeSetLimit = null;
        Guid latest = _power.ActiveScheme;
        Assert.False(session.Apply(journal.Id, 40).Pending);
        Assert.Equal(latest, session.Read()!.Original);
        Assert.Equal(40, _power.Limits[_power.ActiveScheme]);
        session.Restore();
        Assert.Equal(latest, _power.ActiveScheme);
    }

    [Fact] public void InterruptedAdoptionRetainsAllRecoveryInformation()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 80);
        var external = AddProfile();
        _power.Activate(external);
        _power.BeforeDuplicate = () =>
        {
            Assert.Equal(2, session.Read()!.Schemes!.Count);
            throw new IOException("Interrupted duplication");
        };
        Assert.Throws<IOException>(() => session.Apply(journal.Id, 40));
        new PowerSession(_power, PathName).Restore();
        Assert.Equal(external, _power.ActiveScheme);
        Assert.Equal(2, _power.Schemes.Count);
    }

    [Fact] public void RestoresLegacyJournalFromPreviousVersion()
    {
        var original = _power.ActiveScheme;
        var temporary = Guid.NewGuid();
        _power.Duplicate(original, temporary);
        _power.Activate(temporary);
        Directory.CreateDirectory(_directory);
        // Actual pre-0.3.3 shape, without the new optional fields.
        File.WriteAllText(PathName, System.Text.Json.JsonSerializer.Serialize(new
        {
            Id = Guid.NewGuid(), Original = original, Temporary = temporary,
            OwnerPid = 1, OwnerStartTicks = 1, Heartbeat = DateTimeOffset.UtcNow
        }));
        new PowerSession(_power, PathName).Restore();
        Assert.Equal(original, _power.ActiveScheme);
        Assert.DoesNotContain(temporary, _power.Schemes);
    }

    [Fact] public void FailedMultiProfileCleanupKeepsJournalAndCanBeRetried()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 80);
        var external = AddProfile();
        _power.Activate(external);
        session.Apply(journal.Id, 40);
        Guid secondClone = session.Read()!.Temporary;
        _power.FailDelete = secondClone;
        Assert.Throws<IOException>(session.Restore);
        Assert.Equal(external, _power.ActiveScheme);
        Assert.True(File.Exists(PathName));
        Assert.DoesNotContain(journal.Temporary, _power.Schemes);
        _power.FailDelete = null;
        new PowerSession(_power, PathName).Restore();
        Assert.False(File.Exists(PathName));
        Assert.Equal(2, _power.Schemes.Count);
    }

    [Fact] public void InterruptedSwitchRestoresActuallyActiveOlderManagedClone()
    {
        var session = new PowerSession(_power, PathName);
        Guid original = _power.ActiveScheme;
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 80);
        _power.Activate(AddProfile());
        _power.BeforeDuplicate = () =>
        {
            _power.Activate(journal.Temporary);
            throw new IOException("Game restored the launch profile during duplication");
        };
        Assert.Throws<IOException>(() => session.Apply(journal.Id, 40));
        new PowerSession(_power, PathName).Restore();
        Assert.Equal(original, _power.ActiveScheme);
        Assert.False(File.Exists(PathName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackupRecoversNullFilledOrMissingJournal(bool missing)
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 60);
        var external = AddProfile();
        _power.Activate(external);
        session.Apply(journal.Id, 40);
        if (missing) File.Delete(PathName);
        else File.WriteAllBytes(PathName, new byte[374]);

        new PowerSession(_power, PathName).Restore();

        Assert.Equal(external, _power.ActiveScheme);
        Assert.Equal(2, _power.Schemes.Count);
        Assert.False(File.Exists(PathName));
        Assert.False(File.Exists(PathName + ".bak"));
        Assert.Null(session.Read());
    }

    [Fact]
    public void CorruptJournalWithoutBackupPreservesEvidenceAndPowerProfile()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 40);
        File.Delete(PathName + ".bak");
        File.WriteAllBytes(PathName, new byte[374]);

        var error = Assert.Throws<IOException>(session.Restore);

        Assert.Contains("endommagé", error.Message);
        Assert.Equal(journal.Temporary, _power.ActiveScheme);
        Assert.Equal(new byte[374], File.ReadAllBytes(PathName));
    }

    [Fact]
    public void InvalidBackupAlsoPreservesBothFiles()
    {
        var session = new PowerSession(_power, PathName);
        session.Prepare(1, 1);
        File.WriteAllText(PathName, "null");
        File.WriteAllText(PathName + ".bak", "{}");

        Assert.Throws<IOException>(session.Restore);

        Assert.Equal("null", File.ReadAllText(PathName));
        Assert.Equal("{}", File.ReadAllText(PathName + ".bak"));
    }

    [Fact]
    public void HeartbeatDoesNotOverwriteIndependentRecoveryBackup()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 40);
        string backup = File.ReadAllText(PathName + ".bak");
        session.Heartbeat(journal.Id);
        Assert.Equal(backup, File.ReadAllText(PathName + ".bak"));
        session.Restore();
    }

    private Guid AddProfile()
    {
        Guid profile = Guid.NewGuid();
        _power.Schemes.Add(profile);
        return profile;
    }

    [Fact] public void FailedRestoreKeepsJournalForRetry()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 90);
        _power.FailActivation = true;
        Assert.Throws<IOException>(session.Restore);
        Assert.True(File.Exists(PathName));
        _power.FailActivation = false;
        session.Restore();
        Assert.False(File.Exists(PathName));
    }

    [Fact] public void InterruptedPreparationIsRecoverable()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Restore();
        Assert.DoesNotContain(journal.Temporary, _power.Schemes);
        session.Restore(); // idempotent
    }

    [Fact] public void JournalExistsBeforeDuplicate()
    {
        _power.BeforeDuplicate = () => Assert.True(File.Exists(PathName));
        new PowerSession(_power, PathName).Prepare(1, 1);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class FakePower : IPowerPlatform
    {
        public Guid ActiveScheme { get; private set; } = Guid.NewGuid();
        public HashSet<Guid> Schemes { get; } = [];
        public Dictionary<Guid, int> Limits { get; } = [];
        public bool FailActivation { get; set; }
        public Guid? FailDelete { get; set; }
        public Action? BeforeDuplicate { get; set; }
        public Action? BeforeSetLimit { get; set; }
        public int Activations { get; private set; }
        public int LimitWrites { get; private set; }
        public void Duplicate(Guid source, Guid destination) { BeforeDuplicate?.Invoke(); Schemes.Add(source); Schemes.Add(destination); }
        public void Activate(Guid scheme)
        {
            if (FailActivation) throw new IOException("Simulated power API failure");
            if (!Schemes.Contains(scheme)) throw new IOException("Missing scheme");
            ActiveScheme = scheme;
            Activations++;
        }
        public void Delete(Guid scheme)
        {
            if (ActiveScheme == scheme || FailDelete == scheme) throw new IOException("Cannot delete scheme");
            Schemes.Remove(scheme);
        }
        public void SetLimit(Guid scheme, int percent)
        {
            BeforeSetLimit?.Invoke();
            LimitWrites++;
            Limits[scheme] = percent;
        }
    }
}
