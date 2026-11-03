using System.Security.Principal;

namespace ThermalPilot;

internal static class Recovery
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThermalPilot");
    public static string JournalPath => Path.Combine(DataDirectory, "power-session.json");
    private static string UserId => WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    public static string AppMutex => @"Local\ThermalPilot.UI." + UserId;
    private static string LockName => @"Local\ThermalPilot.Power." + UserId;
    public static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static T Locked<T>(Func<PowerSession, T> action)
    {
        using var mutex = new Mutex(false, LockName);
        bool held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new TimeoutException("Le contrôle d’alimentation ne répond pas.");
            return action(Session);
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    private static readonly PowerSession Session = new(new WindowsPowerPlatform(), JournalPath);

    public static void Restore() => Locked(session => { session.Restore(); return true; });

    public static void StartWatchdog(SessionJournal journal)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable introuvable.");
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--watchdog");
        info.ArgumentList.Add(journal.Id.ToString());
        using var process = Process.Start(info) ?? throw new IOException("Le processus de secours ne démarre pas.");
        string ready = Path.Combine(DataDirectory, journal.Id + ".ready");
        // No power limit is applied until the recovery process acknowledges startup.
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (File.Exists(ready)) { File.Delete(ready); return; }
            if (process.HasExited) break;
            Thread.Sleep(50);
        }
        throw new IOException("Le processus de secours n’a pas répondu. Aucun bridage activé.");
    }

    public static void Watch(Guid id)
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(Path.Combine(DataDirectory, id + ".ready"), "ready");
        while (true)
        {
            try
            {
                bool done = Locked(session =>
                {
                    var journal = session.Read();
                    if (journal?.Id != id) return true;
                    if (!OwnerAlive(journal) || DateTimeOffset.UtcNow - journal.Heartbeat > TimeSpan.FromSeconds(15))
                    {
                        session.Restore();
                        return true;
                    }
                    return false;
                });
                if (done) return;
            }
            catch (Exception ex) { Log(ex); }
            Thread.Sleep(1000);
        }
    }

    private static bool OwnerAlive(SessionJournal journal)
    {
        try
        {
            using var owner = Process.GetProcessById(journal.OwnerPid);
            return !owner.HasExited && owner.StartTime.ToUniversalTime().Ticks == journal.OwnerStartTicks;
        }
        catch (ArgumentException) { return false; }
    }

    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string path = Path.Combine(DataDirectory, "errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {ex}\n");
        }
        catch (IOException) { }
    }
}
