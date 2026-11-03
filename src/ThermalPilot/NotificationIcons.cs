using System.Drawing;

namespace ThermalPilot;

internal enum NotificationState { Idle, Active, Cooling, Alert }

/// <summary>Keep native icons alive while NotifyIcon owns their handles.</summary>
internal sealed class NotificationIcons : IDisposable
{
    private readonly Dictionary<NotificationState, Icon> _icons = new();

    public NotificationIcons()
    {
        foreach (var state in Enum.GetValues<NotificationState>())
        {
            var uri = new Uri($"pack://application:,,,/Assets/Tray{state}.ico", UriKind.Absolute);
            var resource = System.Windows.Application.GetResourceStream(uri)
                ?? throw new FileNotFoundException("Icône de notification introuvable.", uri.ToString());
            using var stream = resource.Stream;
            using var source = new Icon(stream, new System.Drawing.Size(32, 32));
            _icons.Add(state, (Icon)source.Clone());
        }
    }

    public Icon this[NotificationState state] => _icons[state];

    public void Dispose()
    {
        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear();
    }
}
