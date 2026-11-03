using System.ComponentModel;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace ThermalPilot;

public partial class MainWindow : Window
{
    private readonly RegulationService _service;
    private readonly bool _demo;
    private readonly string[] _args;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private bool _closing;
    private bool _busy;
    private bool _valid;
    private readonly Forms.NotifyIcon _tray;
    private readonly NotificationIcons _notificationIcons = new();
    private NotificationState _notificationState = NotificationState.Idle;
    private readonly PreferencesStore _preferencesStore;
    private AppPreferences _preferences = new();
    private bool _preferencesReady;
    private bool _changingStartup;
    private bool _autoStartPending;
    private readonly bool _startupMode;

    public MainWindow(bool demo, string[] args)
    {
        _demo = demo;
        _args = args;
        _service = new(demo);
        _preferencesStore = new(Path.Combine(Recovery.DataDirectory, demo ? "demo-settings.json" : "settings.json"));
        _startupMode = !demo && args.Contains("--startup");
        InitializeComponent();
        // Keep the window inside the desktop work area; smaller displays retain scrolling.
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        _tray = new Forms.NotifyIcon
        {
            Icon = _notificationIcons[NotificationState.Idle], Text = "Thermal Pilot · En veille", Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("Ouvrir Thermal Pilot", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        _tray.ContextMenuStrip.Items.Add("Arrêter et quitter", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        bool pawnInstalled = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
        if (demo || !Recovery.IsAdmin || !pawnInstalled)
        {
            NoticePanel.Visibility = Visibility.Visible;
            NoticeText.Text = demo ? "MODE DÉMONSTRATION · Températures simulées, aucun réglage du PC n’est modifié."
                : !pawnInstalled ? "PawnIO est absent. Installe-le, puis relance l’application en administrateur."
                : "La régulation et certaines sondes nécessitent les droits administrateur.";
            AdminButton.Visibility = demo || Recovery.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
            PawnButton.Visibility = !demo && !pawnInstalled ? Visibility.Visible : Visibility.Collapsed;
            if (demo) ModeBadge.Text = "DÉMONSTRATION";
        }
        try
        {
            _preferences = _preferencesStore.Load();
            TargetSlider.Value = _preferences.Target;
            StartupCheckBox.IsChecked = _preferences.StartWithWindows;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Recovery.Log(ex);
            PreferencesStatusText.Text = "Les préférences n’ont pas pu être lues : " + ex.Message;
        }
        _preferencesReady = true;
        _autoStartPending = _startupMode && _preferences.StartWithWindows;
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_startupMode) { Hide(); Opacity = 1; ShowInTaskbar = true; }
        _loop = RunAsync();
        await InitializeStartupOptionAsync();
        int capture = Array.IndexOf(_args, "--capture");
        if (_demo && capture >= 0 && capture + 1 < _args.Length)
        {
            int heightIndex = Array.IndexOf(_args, "--capture-height");
            if (heightIndex >= 0 && heightIndex + 1 < _args.Length && int.TryParse(_args[heightIndex + 1], out int captureHeight))
                Height = Math.Clamp(captureHeight, (int)MinHeight, (int)SystemParameters.WorkArea.Height);
            // Deterministic offline preview; this code never constructs a power session.
            for (int i = 0; i < 100; i++) Chart.Add(69 + 7 * Math.Sin(i / 14d));
            await Task.Delay(1800);
            var surface = (FrameworkElement)Content;
            var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.GetFullPath(_args[capture + 1]))) encoder.Save(stream);
            File.WriteAllText(Path.GetFullPath(_args[capture + 1]) + ".json", JsonSerializer.Serialize(new
            {
                WindowHeight = ActualHeight, ViewportHeight = MainScrollViewer.ViewportHeight,
                ExtentHeight = MainScrollViewer.ExtentHeight, ScrollableHeight = MainScrollViewer.ScrollableHeight,
                ScrollBarVisibility = MainScrollViewer.ComputedVerticalScrollBarVisibility.ToString()
            }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _service.SampleAsync();
                if (_stop.IsCancellationRequested) break;
                _valid = ThermalController.IsValid(snapshot.Reading.Temperature);
                TemperatureText.Text = snapshot.Reading.Temperature?.ToString("F1") ?? "—";
                CpuNameText.Text = snapshot.Reading.Cpu;
                CpuNameText.ToolTip = snapshot.Reading.Cpu;
                SensorText.Text = snapshot.Reading.Sensor + " · actualisation 1 s";
                CompatibilityText.Text = _service.Compatibility;
                StatusText.Text = snapshot.Status;
                DetailText.Text = snapshot.Detail;
                var color = snapshot.Error ? "#EBAC70" : snapshot.Active ? "#6DE5C0" : "#91A0B6";
                StatusDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
                TemperatureText.Foreground = (Brush)new BrushConverter().ConvertFromString(
                    snapshot.Reading.Temperature > _service.Target ? "#F0B472" : "#EDF3FA")!;
                LimitText.Text = snapshot.Active ? snapshot.Limit + " % du maximum Windows" : "Profil Windows initial";
                LimitBar.Value = snapshot.Active ? snapshot.Limit : 100;
                LoadText.Text = snapshot.Reading.Load?.ToString("F0") + (snapshot.Reading.Load is null ? "—" : " %");
                Chart.Target = _service.Target;
                Chart.Add(snapshot.Reading.Temperature);
                UpdateNotification(snapshot);
                RefreshButton();
                await TryAutomaticStartAsync(snapshot);
            }
            catch (Exception ex)
            {
                Recovery.Log(ex);
                StatusText.Text = "Intervention nécessaire";
                DetailText.Text = ex.Message;
                SetNotificationState(NotificationState.Alert);
                _tray.Text = "Thermal Pilot · Intervention nécessaire";
                RefreshButton();
            }
            try { await Task.Delay(1000, _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void RefreshButton()
    {
        ToggleButton.Content = _service.Active ? "Arrêter la régulation" : "Activer la régulation";
        ToggleButton.IsEnabled = !_busy && (_service.Active || (_valid && _service.PowerSettingsAvailable && (_demo || Recovery.IsAdmin)));
    }

    private async Task InitializeStartupOptionAsync()
    {
        if (_demo)
        {
            PreferencesStatusText.Text = "Cible enregistrée automatiquement · démarrage Windows indisponible en démonstration.";
            return;
        }
        try
        {
            bool registered = await Task.Run(WindowsStartup.IsEnabled);
            if (_preferences.StartWithWindows && !registered)
            {
                _changingStartup = true;
                _preferences = _preferences with { StartWithWindows = false };
                StartupCheckBox.IsChecked = false;
                _autoStartPending = false;
                SavePreferences();
                _changingStartup = false;
            }
            StartupCheckBox.IsEnabled = Recovery.IsAdmin;
            UpdatePreferencesStatus();
            if (_preferences.StartWithWindows && registered && !await Task.Run(WindowsStartup.TargetsCurrentInstallation))
                PreferencesStatusText.Text = "Démarrage configuré depuis un autre dossier. Décoche puis recoche l’option pour utiliser cette installation.";
        }
        catch (Exception ex)
        {
            Recovery.Log(ex);
            PreferencesStatusText.Text = "Vérification du démarrage Windows impossible : " + ex.Message;
        }
    }

    private void UpdatePreferencesStatus()
    {
        PreferencesStatusText.Text = _demo
            ? "Cible enregistrée automatiquement · démarrage Windows indisponible en démonstration."
            : !Recovery.IsAdmin
                ? "Cible enregistrée automatiquement · relance en administrateur pour modifier le démarrage Windows."
                : _preferences.StartWithWindows
                    ? "Cible enregistrée · régulation en arrière-plan à l’ouverture de session. Conserve ce dossier à son emplacement."
                    : "La température cible est enregistrée automatiquement à chaque changement.";
    }

    private bool SavePreferences()
    {
        if (!_preferencesReady) return true;
        try
        {
            _preferences = _preferences with { Target = _service.Target };
            _preferencesStore.Save(_preferences);
            UpdatePreferencesStatus();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Recovery.Log(ex);
            PreferencesStatusText.Text = "Enregistrement impossible : " + ex.Message;
            return false;
        }
    }

    private async void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_preferencesReady || _changingStartup || _demo) return;
        bool requested = StartupCheckBox.IsChecked == true;
        var previous = _preferences;
        _changingStartup = true;
        StartupCheckBox.IsEnabled = false;
        try
        {
            _preferences = _preferences with { StartWithWindows = requested };
            if (!SavePreferences()) throw new IOException("L’option n’a pas pu être enregistrée.");
            await Task.Run(() => WindowsStartup.SetEnabled(requested));
            if (!requested) _autoStartPending = false;
            UpdatePreferencesStatus();
        }
        catch (Exception ex)
        {
            Recovery.Log(ex);
            _preferences = previous with { Target = _service.Target };
            StartupCheckBox.IsChecked = previous.StartWithWindows;
            SavePreferences();
            System.Windows.MessageBox.Show("Le démarrage Windows n’a pas pu être modifié.\n\n" + ex.Message, "Thermal Pilot");
        }
        finally { _changingStartup = false; StartupCheckBox.IsEnabled = Recovery.IsAdmin; }
    }

    private async Task TryAutomaticStartAsync(Snapshot snapshot)
    {
        if (!_autoStartPending || _busy || snapshot.Active || _stop.IsCancellationRequested) return;
        if (!Recovery.IsAdmin)
        {
            _autoStartPending = false;
            DetailText.Text = "Le démarrage automatique nécessite les droits administrateur. Réactive l’option après élévation.";
            _tray.ShowBalloonTip(5000, "Thermal Pilot", DetailText.Text, Forms.ToolTipIcon.Warning);
            return;
        }
        if (!_valid || !_service.PowerSettingsAvailable)
        {
            DetailText.Text = "Démarrage automatique en attente : " + (!_valid
                ? "une température CPU valide est nécessaire." : "les réglages Windows ne sont pas encore accessibles.");
            return;
        }
        _autoStartPending = false;
        _busy = true;
        RefreshButton();
        try { await _service.StartAsync(); }
        catch (Exception ex)
        {
            Recovery.Log(ex);
            DetailText.Text = "La régulation automatique n’a pas démarré : " + ex.Message;
            _tray.ShowBalloonTip(5000, "Thermal Pilot", DetailText.Text, Forms.ToolTipIcon.Warning);
            SetNotificationState(NotificationState.Alert);
        }
        finally { _busy = false; RefreshButton(); }
    }

    private void SetNotificationState(NotificationState state)
    {
        if (_notificationState == state) return;
        _tray.Icon = _notificationIcons[state];
        _notificationState = state;
    }

    private void UpdateNotification(Snapshot snapshot)
    {
        var state = snapshot.Error || !_valid ? NotificationState.Alert
            : !snapshot.Active ? NotificationState.Idle
            : snapshot.Limit < 100 || snapshot.Reading.Temperature > _service.Target ? NotificationState.Cooling
            : NotificationState.Active;
        SetNotificationState(state);
        string status = !_valid ? "Sonde absente" : snapshot.Status;
        string tooltip = $"Thermal Pilot{(_demo ? " (démo)" : "")} · {TemperatureText.Text} °C · {status}";
        _tray.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _autoStartPending = false;
        SavePreferences();
        _busy = true;
        RefreshButton();
        try
        {
            if (_service.Active) await _service.StopAsync(); else await _service.StartAsync();
        }
        catch (Exception ex)
        {
            Recovery.Log(ex);
            System.Windows.MessageBox.Show(ex.Message, "Thermal Pilot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _busy = false; RefreshButton(); }
    }

    private void Target_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_service is null || TargetText is null) return;
        _service.Target = (int)e.NewValue;
        TargetText.Text = _service.Target.ToString();
        if (Chart is not null) { Chart.Target = _service.Target; Chart.InvalidateVisual(); }
        SavePreferences();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void Pawn_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/namazso/PawnIO.Setup/releases/latest") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Thermal Pilot"); }
    }
    private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            start.ArgumentList.Add("--wait-parent");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(start)?.Dispose();
            Close();
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Thermal Pilot"); }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        _busy = true;
        ToggleButton.IsEnabled = false;
        SavePreferences();
        _stop.Cancel();
        try
        {
            if (_loop is not null) await _loop.WaitAsync(TimeSpan.FromSeconds(5));
            await _service.StopAsync();
            _service.Dispose();
        }
        catch (Exception ex)
        {
            Recovery.Log(ex);
            System.Windows.MessageBox.Show("La restauration n’a pas abouti. Le processus de secours réessaiera après la fermeture.\n\n" + ex.Message, "Thermal Pilot");
        }
        _tray.Dispose();
        _notificationIcons.Dispose();
        _stop.Dispose();
        _closing = true;
        Close();
    }

}
