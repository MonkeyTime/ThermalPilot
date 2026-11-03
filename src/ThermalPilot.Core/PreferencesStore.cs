using System.Text.Json;

namespace ThermalPilot.Core;

public sealed record AppPreferences(int Target = 75, bool StartWithWindows = false)
{
    public AppPreferences Normalize() => this with { Target = Math.Clamp(Target, 45, 95) };
}

public sealed class PreferencesStore(string path)
{
    public AppPreferences Load() => !File.Exists(path) ? new() :
        (JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path))
            ?? throw new JsonException("Les préférences sont vides.")).Normalize();

    public void Save(AppPreferences preferences)
    {
        string absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        string temporary = absolute + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, preferences.Normalize());
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, absolute, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
