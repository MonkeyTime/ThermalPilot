using System.Text.Json;

namespace ThermalPilot;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--uninstall-cleanup") || e.Args.Contains("--install-check"))
        {
            try
            {
                if (e.Args.Contains("--uninstall-cleanup")) Shutdown(InstallationMaintenance.CleanForUninstall(e.Args));
                else { InstallationMaintenance.CheckNotRunning(); Shutdown(0); }
            }
            catch (Exception ex)
            {
                Recovery.Log(ex);
                if (!e.Args.Contains("--quiet")) System.Windows.MessageBox.Show(ex.Message, "Thermal Pilot — installation");
                Shutdown(1);
            }
            return;
        }
        int parentIndex = Array.IndexOf(e.Args, "--wait-parent");
        if (parentIndex >= 0 && parentIndex + 1 < e.Args.Length && int.TryParse(e.Args[parentIndex + 1], out int parentPid))
        {
            try { using var parent = Process.GetProcessById(parentPid); parent.WaitForExit(8000); }
            catch (ArgumentException) { }
        }
        if (e.Args.Length == 2 && e.Args[0] == "--watchdog" && Guid.TryParse(e.Args[1], out var id))
        {
            Recovery.Watch(id);
            Shutdown();
            return;
        }
        bool demo = e.Args.Contains("--demo");
        bool startup = !demo && e.Args.Contains("--startup");
        if (startup)
        {
            try
            {
                var store = new PreferencesStore(Path.Combine(Recovery.DataDirectory, "settings.json"));
                if (!store.Load().StartWithWindows) { Shutdown(); return; }
            }
            catch (Exception ex) { Recovery.Log(ex); Shutdown(1); return; }
        }
        if (e.Args.Length == 2 && e.Args[0] == "--diagnostics")
        {
            try
            {
                using var monitor = new CpuMonitor();
                var reading = monitor.Read();
                File.WriteAllText(Path.GetFullPath(e.Args[1]), JsonSerializer.Serialize(new
                {
                    Admin = Recovery.IsAdmin, PawnIOInstalled = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled, Reading = reading,
                    ActivePowerScheme = new WindowsPowerPlatform().ActiveScheme,
                    PowerSupport = WindowsPowerPlatform.Probe(),
                    StartupTask = WindowsStartup.Inspect(),
                    JournalPresent = File.Exists(Recovery.JournalPath)
                }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception ex) { Recovery.Log(ex); Shutdown(1); }
            return;
        }
        _singleInstance = new Mutex(true, Recovery.AppMutex + (demo ? ".Demo" : ""), out bool created);
        if (!created)
        {
            if (!startup) System.Windows.MessageBox.Show("Thermal Pilot est déjà ouvert. Vérifie la zone de notification.", "Thermal Pilot");
            Shutdown();
            return;
        }
        if (!demo)
        {
            try { Recovery.Restore(); }
            catch (Exception ex)
            {
                Recovery.Log(ex);
                System.Windows.MessageBox.Show("La restauration du profil précédent a échoué. Relance en administrateur.\n\n" + ex.Message,
                    "Thermal Pilot — restauration nécessaire");
                Shutdown(1);
                return;
            }
        }
        DispatcherUnhandledException += (_, args) =>
        {
            Recovery.Log(args.Exception);
            if (!demo) { try { Recovery.Restore(); } catch (Exception ex) { Recovery.Log(ex); } }
            System.Windows.MessageBox.Show(args.Exception.Message, "Thermal Pilot");
            args.Handled = true;
            Shutdown(1);
        };
        var window = new MainWindow(demo, e.Args);
        if (startup) { window.Opacity = 0; window.ShowActivated = false; window.ShowInTaskbar = false; }
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
