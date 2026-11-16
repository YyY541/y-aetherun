using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aetherun.Desktop.Controls;

/// <summary>环形进度：底环 + 进度弧，进度按顺时针从 12 点方向增长。</summary>
public sealed class ProgressRing : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Progress));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Thickness), 10.0);

    public static readonly StyledProperty<IBrush?> RingBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(RingBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(TrackBrush));

    static ProgressRing()
    {
        AffectsRender<ProgressRing>(ProgressProperty, ThicknessProperty,
                                    RingBrushProperty, TrackBrushProperty);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public IBrush? RingBrush
    {
        get => GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        double t = Thickness;
        double size = Math.Min(Bounds.Width, Bounds.Height) - t;
        if (size <= 0) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double r = size / 2;

        var track = TrackBrush ?? new SolidColorBrush(Color.Parse("#161D28"));
        var ring = RingBrush ?? new SolidColorBrush(Color.Parse("#3DDC97"));

        ctx.DrawEllipse(null, new Pen(track, t), center, r, r);

        double p = Math.Clamp(Progress, 0, 1);
        if (p <= 0.0005) return;

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            double startRad = -Math.PI / 2;
            var start = new Point(center.X + r * Math.Cos(startRad), center.Y + r * Math.Sin(startRad));

            if (p >= 0.9995)
            {
                g.BeginFigure(start, false);
                g.ArcTo(new Point(center.X + r * Math.Cos(startRad + Math.PI), center.Y + r * Math.Sin(startRad + Math.PI)),
                        new Size(r, r), 0, false, SweepDirection.Clockwise);
                g.ArcTo(start, new Size(r, r), 0, false, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            else
            {
                double endRad = startRad + 2 * Math.PI * p;
                var end = new Point(center.X + r * Math.Cos(endRad), center.Y + r * Math.Sin(endRad));
                g.BeginFigure(start, false);
                g.ArcTo(end, new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
        }

        ctx.DrawGeometry(null, new Pen(ring, t) { LineCap = PenLineCap.Round }, geo);
    }
}
