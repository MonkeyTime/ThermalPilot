using System.Globalization;
using System.Windows.Media;

namespace ThermalPilot;

public sealed class TemperatureChart : FrameworkElement
{
    private readonly Queue<double?> _values = new();
    public int Target { get; set; } = 75;
    private static readonly SolidColorBrush Green = new(Color.FromRgb(109, 229, 192));
    public void Add(double? value)
    {
        _values.Enqueue(value);
        while (_values.Count > 120) _values.Dequeue();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = Math.Max(0, ActualWidth - 35), height = Math.Max(0, ActualHeight - 12);
        if (width < 1 || height < 1) return;
        var data = _values.ToArray();
        double max = Math.Max(100, data.Where(v => v.HasValue).Select(v => v!.Value + 5).DefaultIfEmpty(100).Max());
        double Y(double t) => height - (Math.Clamp(t, 30, max) - 30) / (max - 30) * height + 5;
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(37, 52, 72)), 1);
        foreach (int temperature in new[] { 40, 60, 80, 100 })
        {
            double y = Y(temperature);
            dc.DrawLine(gridPen, new Point(0, y), new Point(width, y));
            var label = new FormattedText(temperature.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(101, 123, 148)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(width + 9, y - 7));
        }
        var targetPen = new Pen(new SolidColorBrush(Color.FromRgb(232, 180, 107)), 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(targetPen, new Point(0, Y(Target)), new Point(width, Y(Target)));
        var linePen = new Pen(Green, 2);
        Point? previous = null;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] is not double value || !ThermalController.IsValid(value)) { previous = null; continue; }
            var point = new Point(width * (120 - data.Length + i) / 119d, Y(value));
            if (previous is Point from) dc.DrawLine(linePen, from, point);
            previous = point;
        }
        if (previous is Point end) dc.DrawEllipse(Green, null, end, 3, 3);
    }
}
