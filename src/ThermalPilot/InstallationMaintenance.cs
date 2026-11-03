using System.Security.Principal;
using System.Text.Json;

namespace ThermalPilot;

internal static class InstallationMaintenance
{
    public static void CheckNotRunning()
    {
        if (Mutex.TryOpenExisting(Recovery.AppMutex, out var existing))
        {
            existing.Dispose();
            throw new InvalidOperationException("Ferme Thermal Pilot depuis son icône près de l’horloge, puis réessaie.");
        }
    }

    public static int CleanForUninstall(string[] arguments)
    {
        CheckNotRunning();
        string userSid = WindowsIdentity.GetCurrent().User!.Value;
        int userIndex = Array.IndexOf(arguments, "--cleanup-user");
        if (userIndex >= 0 && (userIndex + 1 >= arguments.Length || arguments[userIndex + 1] != userSid))
            throw new InvalidOperationException("La désinstallation doit utiliser le même compte Windows que la régulation.");

        bool hasRegistration = WindowsStartup.Exists();
        bool hasTask = WindowsStartup.TargetsCurrentInstallation();
        bool hasJournal = File.Exists(Recovery.JournalPath);
        if (!Recovery.IsAdmin && (hasTask || hasJournal))
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            info.ArgumentList.Add("--uninstall-cleanup");
            info.ArgumentList.Add("--cleanup-user");
            info.ArgumentList.Add(userSid);
            if (arguments.Contains("--quiet")) info.ArgumentList.Add("--quiet");
            using var elevated = Process.Start(info) ?? throw new IOException("Le nettoyage n’a pas pu démarrer.");
            elevated.WaitForExit();
            return elevated.ExitCode;
        }

        using var mutex = new Mutex(true, Recovery.AppMutex, out bool created);
        if (!created) throw new InvalidOperationException("Thermal Pilot vient d’être ouvert. Ferme-le, puis réessaie.");
        try
        {
            if (hasTask) WindowsStartup.SetEnabled(false);
            if (hasJournal) Recovery.Restore();
            string settings = Path.Combine(Recovery.DataDirectory, "settings.json");
            if (File.Exists(settings) && (!hasRegistration || hasTask))
            {
                var store = new PreferencesStore(settings);
                try { store.Save(store.Load() with { StartWithWindows = false }); }
                catch (JsonException ex) { Recovery.Log(ex); } // Keep corrupt preferences for diagnosis; the task is removed.
            }
            return 0;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
