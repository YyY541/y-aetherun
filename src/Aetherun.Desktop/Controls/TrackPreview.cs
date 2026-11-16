using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Aetherun.Core.Models;

namespace Aetherun.Desktop.Controls;

/// <summary>跑道缩略图：把闭合点位等比缩放画成折线，并在当前位置画游标。</summary>
public sealed class TrackPreview : Control
{
    public static readonly StyledProperty<TrackLoop?> TrackProperty =
        AvaloniaProperty.Register<TrackPreview, TrackLoop?>(nameof(Track));

    public static readonly StyledProperty<double> DistanceMetersProperty =
        AvaloniaProperty.Register<TrackPreview, double>(nameof(DistanceMeters));

    static TrackPreview()
    {
        AffectsRender<TrackPreview>(TrackProperty, DistanceMetersProperty);
    }

    public TrackLoop? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public double DistanceMeters
    {
        get => GetValue(DistanceMetersProperty);
        set => SetValue(DistanceMetersProperty, value);
    }

    private readonly IBrush _lineBrush = new SolidColorBrush(Color.Parse("#243347"));
    private readonly IBrush _dotBrush = new SolidColorBrush(Color.Parse("#3DDC97"));
    private readonly IBrush _startBrush = new SolidColorBrush(Color.Parse("#38BDF8"));

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var track = Track;
        if (track is null || track.Count < 3) return;

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 12 || bounds.Height < 12) return;

        double pad = 10;
        var pts = track.Points;
        double latMin = pts.Min(p => p.Lat), latMax = pts.Max(p => p.Lat);
        double lonMin = pts.Min(p => p.Lon), lonMax = pts.Max(p => p.Lon);
        double spanLat = Math.Max(latMax - latMin, 1e-9);
        double spanLon = Math.Max(lonMax - lonMin, 1e-9);

        double sx = (bounds.Width - pad * 2) / spanLon;
        double sy = (bounds.Height - pad * 2) / spanLat;
        double s = Math.Min(sx, sy);

        double offX = pad + ((bounds.Width - pad * 2) - spanLon * s) / 2;
        double offY = pad + ((bounds.Height - pad * 2) - spanLat * s) / 2;

        Point Project(GeoPoint p) => new(
            offX + (p.Lon - lonMin) * s,
            offY + (latMax - p.Lat) * s);

        var pen = new Pen(_lineBrush, 2);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            bool first = true;
            foreach (var p in pts)
            {
                var q = Project(p);
                if (first) { g.BeginFigure(q, false); first = false; }
                else g.LineTo(q);
            }
            g.EndFigure(true);
        }
        ctx.DrawGeometry(null, pen, geo);

        // start marker
        ctx.DrawEllipse(_startBrush, null, Project(pts[0]), 3.2, 3.2);

        // cursor
        double d = DistanceMeters % track.Perimeter;
        var cur = Project(track.At(d));
        ctx.DrawEllipse(_dotBrush, null, cur, 4.2, 4.2);
        ctx.DrawEllipse(null, new Pen(_dotBrush, 1.2), cur, 8, 8);
    }
}
