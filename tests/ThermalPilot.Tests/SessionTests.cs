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

    [Fact] public void ExternalPlanChangeIsRespected()
    {
        var session = new PowerSession(_power, PathName);
        var journal = session.Prepare(1, 1);
        session.Start(journal.Id, 90);
        var external = Guid.NewGuid();
        _power.Schemes.Add(external);
        _power.Activate(external);
        Assert.Throws<InvalidOperationException>(() => session.Apply(journal.Id, 50));
        session.Restore();
        Assert.Equal(external, _power.ActiveScheme);
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
        public Action? BeforeDuplicate { get; set; }
        public void Duplicate(Guid source, Guid destination) { BeforeDuplicate?.Invoke(); Schemes.Add(source); Schemes.Add(destination); }
        public void Activate(Guid scheme)
        {
            if (FailActivation) throw new IOException("Simulated power API failure");
            if (!Schemes.Contains(scheme)) throw new IOException("Missing scheme");
            ActiveScheme = scheme;
        }
        public void Delete(Guid scheme) { if (ActiveScheme == scheme) throw new IOException("Active scheme"); Schemes.Remove(scheme); }
        public void SetLimit(Guid scheme, int percent) => Limits[scheme] = percent;
    }
}
