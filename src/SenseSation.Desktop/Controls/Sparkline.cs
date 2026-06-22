using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SenseSation.Desktop.Controls;

/// <summary>Minimal line chart: draws a normalized polyline + soft area from a list of values.</summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> PointsProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Points));

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush>(nameof(Stroke), Brushes.White);

    public IReadOnlyList<double>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IBrush Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    static Sparkline() => AffectsRender<Sparkline>(PointsProperty, StrokeProperty, BoundsProperty);

    public override void Render(DrawingContext ctx)
    {
        var pts = Points;
        if (pts is null || pts.Count < 2) return;

        double w = Bounds.Width, h = Bounds.Height;
        const double pad = 6;
        double min = pts.Min(), max = pts.Max();
        double range = max - min < 1 ? 1 : max - min;

        var coords = new Point[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            double x = pad + (w - 2 * pad) * i / (pts.Count - 1);
            double y = h - pad - (h - 2 * pad) * (pts[i] - min) / range;
            coords[i] = new Point(x, y);
        }

        // area fill
        var fill = new StreamGeometry();
        using (var c = fill.Open())
        {
            c.BeginFigure(new Point(coords[0].X, h - pad), true);
            foreach (var p in coords) c.LineTo(p);
            c.LineTo(new Point(coords[^1].X, h - pad));
            c.EndFigure(true);
        }
        var col = (Stroke as ISolidColorBrush)?.Color ?? Colors.White;
        ctx.DrawGeometry(new SolidColorBrush(col, 0.18), null, fill);

        // line
        var pen = new Pen(Stroke, 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        for (int i = 1; i < coords.Length; i++) ctx.DrawLine(pen, coords[i - 1], coords[i]);

        // last point dot
        ctx.DrawEllipse(Stroke, null, coords[^1], 3.5, 3.5);
    }
}
