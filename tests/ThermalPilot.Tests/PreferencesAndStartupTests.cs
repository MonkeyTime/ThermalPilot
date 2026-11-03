using System.Text.Json;
using System.Xml.Linq;
using ThermalPilot.Core;
using Xunit;

namespace ThermalPilot.Tests;

public sealed class PreferencesAndStartupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ThermalPilot-settings-tests", Guid.NewGuid().ToString());
    private string SettingsPath => Path.Combine(_directory, "settings.json");
    private static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact] public void NewInstallationDoesNotAutoStart()
        => Assert.Equal(new AppPreferences(75, false), new PreferencesStore(SettingsPath).Load());

    [Fact] public void OldTargetOnlySettingsAreMigratedWithoutEnablingStartup()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{\"Target\":68}");
        Assert.Equal(new AppPreferences(68, false), new PreferencesStore(SettingsPath).Load());
    }

    [Fact] public void ChangesAreReadableByNextProcessWithoutWaitingForShutdown()
    {
        var store = new PreferencesStore(SettingsPath);
        store.Save(new(62, true));
        Assert.Equal(new AppPreferences(62, true), new PreferencesStore(SettingsPath).Load());
        store.Save(new(82, true));
        Assert.Equal(new AppPreferences(82, true), new PreferencesStore(SettingsPath).Load());
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact] public void DisablingStartupPreservesSliderPosition()
    {
        var store = new PreferencesStore(SettingsPath);
        store.Save(new(67, true));
        store.Save(store.Load() with { StartWithWindows = false });
        Assert.Equal(new AppPreferences(67, false), new PreferencesStore(SettingsPath).Load());
    }

    [Theory] [InlineData(15, 45)] [InlineData(120, 95)]
    public void InvalidStoredTargetIsClamped(int original, int expected)
    {
        var store = new PreferencesStore(SettingsPath);
        store.Save(new(original, false));
        Assert.Equal(expected, store.Load().Target);
    }

    [Fact] public void CorruptSettingsNeverSilentlyEnableStartup()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{broken");
        Assert.Throws<JsonException>(() => new PreferencesStore(SettingsPath).Load());
    }

    [Fact] public void FailedWritePreservesPreviouslySavedPreferences()
    {
        var store = new PreferencesStore(SettingsPath);
        store.Save(new(70, true));
        Directory.CreateDirectory(SettingsPath + ".tmp");
        Assert.Throws<UnauthorizedAccessException>(() => store.Save(new(80, false)));
        Assert.Equal(new AppPreferences(70, true), store.Load());
    }

    [Fact] public void LogonTaskUsesCurrentUserAndElevationWithoutPassword()
    {
        string sid = "S-1-5-21-123-456-789-1001";
        var task = XDocument.Parse(StartupTaskDefinition.Create(Path.GetFullPath("ThermalPilot.exe"), sid));
        Assert.Equal(sid, task.Descendants(TaskNs + "LogonTrigger").Single().Element(TaskNs + "UserId")!.Value);
        Assert.Empty(task.Descendants(TaskNs + "BootTrigger"));
        Assert.Equal("InteractiveToken", task.Descendants(TaskNs + "LogonType").Single().Value);
        Assert.Equal("HighestAvailable", task.Descendants(TaskNs + "RunLevel").Single().Value);
        Assert.Empty(task.Descendants(TaskNs + "Password"));
        Assert.Equal("PT10S", task.Descendants(TaskNs + "Delay").Single().Value);
    }

    [Fact] public void TaskRunsOnBatteryWithoutTimeLimit()
    {
        var task = XDocument.Parse(StartupTaskDefinition.Create(Path.GetFullPath("ThermalPilot.exe"), "S-1-5-21-1"));
        Assert.Equal("false", task.Descendants(TaskNs + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal("false", task.Descendants(TaskNs + "StopIfGoingOnBatteries").Single().Value);
        Assert.Equal("PT0S", task.Descendants(TaskNs + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("IgnoreNew", task.Descendants(TaskNs + "MultipleInstancesPolicy").Single().Value);
    }

    [Fact] public void ExecutablePathWithSpacesAndXmlCharactersIsPreserved()
    {
        string path = Path.GetFullPath(Path.Combine("Dossier & éléments", "Thermal Pilot", "ThermalPilot.exe"));
        var task = XDocument.Parse(StartupTaskDefinition.Create(path, "S-1-5-21-1"));
        Assert.Equal(path, task.Descendants(TaskNs + "Command").Single().Value);
        Assert.Equal("--startup", task.Descendants(TaskNs + "Arguments").Single().Value);
        Assert.Equal(Path.GetDirectoryName(path), task.Descendants(TaskNs + "WorkingDirectory").Single().Value);
    }

    [Fact] public void RejectsRelativeExecutablePath()
        => Assert.Throws<ArgumentException>(() => StartupTaskDefinition.Create("ThermalPilot.exe", "S-1-5-21-1"));

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
