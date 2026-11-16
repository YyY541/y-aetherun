using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Aetherun.Core.Models;

namespace Aetherun.Desktop.Controls;

/// <summary>
/// 操场 3D 视图：把 12 个点位当成闭合跑道，用透视相机投影成有厚度、有分道线的田径场，
/// 跑动时按当前里程推进一个小人，带拖影、脚步摆动与打卡点脉冲。
/// 全部是 2D 绘制指令算出来的透视（无 GPU 依赖，headless 环境也能出图）。
/// </summary>
public sealed class TrackScene3D : Control
{
    // ---- 依赖属性 ----
    public static readonly StyledProperty<TrackLoop?> TrackProperty =
        AvaloniaProperty.Register<TrackScene3D, TrackLoop?>(nameof(Track));

    public static readonly StyledProperty<double> DistanceMetersProperty =
        AvaloniaProperty.Register<TrackScene3D, double>(nameof(DistanceMeters));

    public static readonly StyledProperty<double> TargetMetersProperty =
        AvaloniaProperty.Register<TrackScene3D, double>(nameof(TargetMeters));

    public static readonly StyledProperty<bool> RunningProperty =
        AvaloniaProperty.Register<TrackScene3D, bool>(nameof(Running));

    public static readonly StyledProperty<int> CheckpointCountProperty =
        AvaloniaProperty.Register<TrackScene3D, int>(nameof(CheckpointCount), 5);

    /// <summary>紧凑模式：仪表盘里的小图，省掉网格、分道线与角标。</summary>
    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<TrackScene3D, bool>(nameof(Compact));

    /// <summary>每秒步数，用来给脚步摆动配节奏。</summary>
    public static readonly StyledProperty<double> CadenceProperty =
        AvaloniaProperty.Register<TrackScene3D, double>(nameof(Cadence), 110);

    static TrackScene3D()
    {
        TrackProperty.Changed.AddClassHandler<TrackScene3D>((c, _) => c._centerReady = false);

        AffectsRender<TrackScene3D>(TrackProperty, DistanceMetersProperty, TargetMetersProperty,
            RunningProperty, CheckpointCountProperty, CompactProperty, CadenceProperty);
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

    public double TargetMeters
    {
        get => GetValue(TargetMetersProperty);
        set => SetValue(TargetMetersProperty, value);
    }

    public bool Running
    {
        get => GetValue(RunningProperty);
        set => SetValue(RunningProperty, value);
    }

    public int CheckpointCount
    {
        get => GetValue(CheckpointCountProperty);
        set => SetValue(CheckpointCountProperty, value);
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public double Cadence
    {
        get => GetValue(CadenceProperty);
        set => SetValue(CadenceProperty, value);
    }

    // ---- 调色 ----
    private static readonly Color CSkyTop = Color.Parse("#0A0F16");
    private static readonly Color CSkyBottom = Color.Parse("#101A22");
    private static readonly Color CField = Color.Parse("#123A2C");
    private static readonly Color CFieldDeep = Color.Parse("#0C2A20");
    private static readonly Color CApron = Color.Parse("#2A3646");
    private static readonly Color CTrack = Color.Parse("#2C3648");
    private static readonly Color CTrackEdge = Color.Parse("#18202C");
    private static readonly Color CTrackDone = Color.Parse("#2E6B54");
    private static readonly Color CLane = Color.Parse("#94A6BE");
    private static readonly Color CAccent = Color.Parse("#3DDC97");
    private static readonly Color CAccentAlt = Color.Parse("#38BDF8");
    private static readonly Color CShadow = Color.Parse("#05080C");

    // ---- 动画状态 ----
    /// <summary>
    /// 最近一次绘制时奶蛙落在屏幕上的位置、朝向与高度。自检用——
    /// 没有它就只能靠肉眼判断「它在动」，而画面上还有跑道、打卡点、拖影一堆东西。
    /// </summary>
    public (double X, double Y, double Yaw, double Height)? DebugFrog { get; private set; }

    /// <summary>平滑后的里程（自检用）。</summary>
    public double DebugDisplayDistance => _displayDistance;

    /// <summary>奶蛙位置的中间量（自检用）。</summary>
    public string DebugFrogDetail { get; private set; } = "";

    private double _clock;
    private double _displayDistance;
    private double _distanceVelocity;
    private bool _timerOn;
    private DispatcherTimer? _timer;
    private readonly List<(double X, double Y, double T)> _trail = new();
    private double _lastTrailAt = -1;
    private bool _painted = true;

    /// <summary>把距离平滑追赶目标值，避免每秒一次的台阶感。返回是否还需要下一帧。</summary>
    public bool Advance(double dt)
    {
        EnsureCenter();
        _clock += dt;

        double gap = DistanceMeters - _displayDistance;
        if (Math.Abs(gap) < 0.02)
        {
            _displayDistance = DistanceMeters;
            _distanceVelocity = 0;
        }
        else
        {
            // 一阶低通 + 速度项，看起来像真的在跑而不是跳
            _distanceVelocity += gap * 16.0 * dt;
            _distanceVelocity *= Math.Exp(-6.5 * dt);
            _displayDistance += _distanceVelocity * dt;
        }

        if (Running && _clock - _lastTrailAt > 0.09)
        {
            _lastTrailAt = _clock;
            var p = LocalAt(GeoAt(Frac(_displayDistance)));
            _trail.Add((p.X, p.Y, _clock));
            while (_trail.Count > 22) _trail.RemoveAt(0);
        }
        else if (!Running)
        {
            _trail.Clear();
        }

        while (_trail.Count > 0 && _clock - _trail[0].T > 2.4) _trail.RemoveAt(0);

        // 静止且追平了就报告「不用再画」。原来两个场景（仪表盘小图 + 跑道页大图）
        // 各挂一个 16ms 计时器，一直在跑 60fps 重绘，切到别的页面也在烧 CPU。
        double rest = Math.Abs(DistanceMeters - _displayDistance);
        return rest > 0.02 || _trail.Count > 0 || Running;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureCenter();
        _displayDistance = DistanceMeters;
        if (_timerOn) return;
        _timerOn = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            // 页面被切走时（IsVisible=false）不推进、也不重绘
            if (!IsEffectivelyVisible) return;

            bool more = Advance(0.016);
            if (more || _painted) InvalidateVisual();
            _painted = more;
        });
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
        _timerOn = false;
    }

    // ================= 几何 =================

    private GeoPoint _center;
    private double _cosLat = 1;
    private bool _centerReady;

    /// <summary>
    /// 跑道中心只算一次，属性换掉重算。
    /// 原来只在 Render 里算，而 Advance（计时器里）比第一帧 Render 早跑，
    /// 于是开局那一小段拖影是按 (0,0) 这个假中心算出来的，会飞到画面外。
    /// </summary>
    private void EnsureCenter()
    {
        var t = Track;
        if (t is null || t.Count < 3) { _centerReady = false; return; }
        if (_centerReady) return;

        var pts = t.Points;
        _center = new GeoPoint(pts.Average(p => p.Lat), pts.Average(p => p.Lon));
        _cosLat = Math.Cos(_center.Lat * Math.PI / 180.0);
        _centerReady = true;
    }

    /// <summary>经纬度换成以跑道中心为原点的本地平面坐标（米，x=东，y=北）。</summary>
    private Point LocalAt(GeoPoint p)
    {
        double y = (p.Lat - _center.Lat) * 111320.0;
        double x = (p.Lon - _center.Lon) * 111320.0 * _cosLat;
        return new Point(x, y);
    }

    /// <summary>
    /// 把「圈内位置 0..1」换成经纬度。
    /// 原来这里写的是 <c>t.At(Frac(f * t.Perimeter))</c>：<c>Frac</c> 是
    /// 「米 → 0..1」，<c>At</c> 收的是**米**，两个单位互换之后相当于
    /// 每次都把 0..1 当成米去插值 → 奶蛙、打卡点、拖影全被钉在起点那 1 米内，
    /// 画面上就是「奶蛙不动」。现在直接把分数乘回周长。
    /// </summary>
    private GeoPoint GeoAt(double f)
    {
        var t = Track;
        if (t is null) return _center;
        return t.At(f * t.Perimeter);
    }

    /// <summary>0..1 归一化的小数部分（负数也能回绕）。</summary>
    private static double Fract(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
        double r = v % 1.0;
        return r < 0 ? r + 1 : r;
    }

    /// <summary>米 → 0..1 的圈内位置。</summary>
    private double Frac(double meters) => Track is { Perimeter: > 0 } t
        ? Fract(meters / t.Perimeter)
        : 0;

    /// <summary>把 12 个折点做 Catmull-Rom 插值，画出来才像跑道而不是多边形。</summary>
    private List<Point> Smoothed(double[] xs, double[] ys, int perSeg = 14)
    {
        int n = xs.Length;
        var outp = new List<Point>(n * perSeg);
        for (int i = 0; i < n; i++)
        {
            int i0 = (i - 1 + n) % n, i1 = i, i2 = (i + 1) % n, i3 = (i + 2) % n;
            for (int k = 0; k < perSeg; k++)
            {
                double t = k / (double)perSeg;
                double t2 = t * t, t3 = t2 * t;
                double x = 0.5 * ((2 * xs[i1]) + (-xs[i0] + xs[i2]) * t
                                  + (2 * xs[i0] - 5 * xs[i1] + 4 * xs[i2] - xs[i3]) * t2
                                  + (-xs[i0] + 3 * xs[i1] - 3 * xs[i2] + xs[i3]) * t3);
                double y = 0.5 * ((2 * ys[i1]) + (-ys[i0] + ys[i2]) * t
                                  + (2 * ys[i0] - 5 * ys[i1] + 4 * ys[i2] - ys[i3]) * t2
                                  + (-ys[i0] + 3 * ys[i1] - 3 * ys[i2] + ys[i3]) * t3);
                outp.Add(new Point(x, y));
            }
        }
        return outp;
    }

    /// <summary>跑道半宽（米）。真操场 8 道约 9.8 米，这里按视觉取 5.4。</summary>
    private const double HalfWidth = 5.4;

    /// <summary>分道数，决定画几条分道线。</summary>
    private const int Lanes = 8;

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 24 || bounds.Height < 24) return;

        // ---- 背景 ----
        ctx.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(CSkyTop, 0),
                new GradientStop(CSkyBottom, 1),
            }
        }, bounds);

        var t = Track;
        if (t is null || t.Count < 3) return;

        // ---- 本地平面 ----
        EnsureCenter();
        var pts = t.Points;

        var local = pts.Select(LocalAt).ToArray();
        var smooth = Smoothed(local.Select(p => p.X).ToArray(), local.Select(p => p.Y).ToArray());
        int n = smooth.Count;

        double halfX = Math.Max(smooth.Max(p => Math.Abs(p.X)), 1);
        double halfY = Math.Max(smooth.Max(p => Math.Abs(p.Y)), 1);
        double worldR = Math.Max(halfX, halfY);

        // ---- 透视相机 ----
        // 仰角 pitch（离地平线的角度），越大越像俯视；相机距离按场地尺寸缩放，
        // 得到真正的近大远小，而不是把平面压扁。
        double pitch = (Compact ? 38.0 : 44.0) * Math.PI / 180.0;
        double sinP = Math.Sin(pitch), cosP = Math.Cos(pitch);
        double camDist = worldR * (Compact ? 2.0 : 2.3);   // 米

        // 跑动时相机轻微环绕，让画面有"活着"的感觉
        double yaw = Running ? Math.Sin(_clock * 0.5) * 2.5 * Math.PI / 180.0 : 0.0;
        double cosY = Math.Cos(yaw), sinY = Math.Sin(yaw);

        // 先把中心线的法线算出来（定缩放时也要用到外圈，所以提前算）
        var normals = new Point[n];
        for (int i = 0; i < n; i++)
        {
            var a = smooth[(i - 1 + n) % n];
            var b = smooth[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) len = 1;
            normals[i] = new Point(-dy / len, dx / len);
        }

        // 按 yaw=0 求投影包围盒（含场地外圈，不然边缘会被裁掉），用来定缩放，
        // 这样跑动时相机环绕也不会让画面忽大忽小
        double umin = double.MaxValue, umax = double.MinValue;
        double vmin = double.MaxValue, vmax = double.MinValue;
        void Include(double wx, double wy)
        {
            double depth0 = camDist + wy * cosP;
            if (depth0 < 1e-6) depth0 = 1e-6;
            double u0 = wx / depth0;
            double v0 = wy * sinP / depth0;
            if (u0 < umin) umin = u0;
            if (u0 > umax) umax = u0;
            if (v0 < vmin) vmin = v0;
            if (v0 > vmax) vmax = v0;
        }
        foreach (var p in smooth) Include(p.X, p.Y);
        foreach (var (w, i) in Enumerable.Range(0, n).SelectMany(i =>
                     new[] { (HalfWidth + 10.0, i), (-(HalfWidth + 10.0), i) }))
            Include(smooth[i].X + normals[i].X * w, smooth[i].Y + normals[i].Y * w);
        double spanU = Math.Max(umax - umin, 1e-6);
        double spanV = Math.Max(vmax - vmin, 1e-6);

        // 跑道有厚度，横向要多留一点
        double margin = Compact ? 0.94 : 0.88;
        double scale = Math.Min(bounds.Width * margin / spanU, bounds.Height * margin / spanV);

        double cx = bounds.Width / 2.0 - (umin + umax) / 2.0 * scale;
        double cy = bounds.Height / 2.0 + (vmin + vmax) / 2.0 * scale;

        Point Project(double wx, double wy)
        {
            double rx = wx * cosY - wy * sinY;
            double ry = wx * sinY + wy * cosY;
            double depth = camDist + ry * cosP;
            if (depth < 1e-6) depth = 1e-6;
            return new Point(cx + rx / depth * scale, cy - ry * sinP / depth * scale);
        }

        Point[] Offset(double w, int from, int count)
        {
            var arr = new Point[count];
            for (int k = 0; k < count; k++)
            {
                int i = (from + k) % n;
                arr[k] = Project(smooth[i].X + normals[i].X * w, smooth[i].Y + normals[i].Y * w);
            }
            return arr;
        }

        StreamGeometry Poly(Point[] q)
        {
            var geo = new StreamGeometry();
            using var g = geo.Open();
            g.BeginFigure(q[0], true);
            for (int i = 1; i < q.Length; i++) g.LineTo(q[i]);
            g.EndFigure(true);
            return geo;
        }

        StreamGeometry Band(int from, int count, double w)
        {
            var o = Offset(w, from, count);
            var i2 = Offset(-w, from, count);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(o[0], true);
                for (int k = 1; k < count; k++) g.LineTo(o[k]);
                for (int k = count - 1; k >= 0; k--) g.LineTo(i2[k]);
                g.EndFigure(true);
            }
            return geo;
        }

        // ---- 场地在地面上的落影（让整块跑道看起来"浮"起来，立体感来源之一）----
        {
            double dropY = Compact ? 6 : 10;
            var shadow = Offset(HalfWidth + 11.0, 0, n)
                .Select(q => new Point(q.X, q.Y + dropY)).ToArray();
            var sgeo = Poly(shadow);
            ctx.DrawGeometry(new SolidColorBrush(CShadow, 0.55), null, sgeo);
        }

        // ---- 场地外圈（水泥地）----
        ctx.DrawGeometry(new SolidColorBrush(CApron, 0.85), null, Poly(Offset(HalfWidth + 9.0, 0, n)));

        // ---- 跑道立体侧壁：外沿往下拉一圈深色，做出橡胶跑道的厚度 ----
        {
            double wall = Compact ? 3.5 : 6.0;
            var skirt = new Point[n * 2];
            var outer = Offset(HalfWidth, 0, n);
            for (int i = 0; i < n; i++) skirt[i] = outer[i];
            for (int i = 0; i < n; i++) skirt[n + i] = new Point(outer[n - 1 - i].X, outer[n - 1 - i].Y + wall);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(skirt[0], true);
                for (int i = 1; i < skirt.Length; i++) g.LineTo(skirt[i]);
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(CTrackEdge), null, geo);
        }

        // 外圈边线
        {
            var o = Offset(HalfWidth + 8.6, 0, n);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(o[0], false);
                for (int i = 1; i < n; i++) g.LineTo(o[i]);
                g.EndFigure(true);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(CLane, 0.18), Compact ? 0.8 : 1.1), geo);
        }

        // ---- 内场草坪（渐变）----
        {
            var inner = Offset(-HalfWidth, 0, n);
            var g = Poly(inner);
            ctx.DrawGeometry(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.3, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.7, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(CField, 0),
                    new GradientStop(CFieldDeep, 1),
                }
            }, new Pen(new SolidColorBrush(CLane, 0.22), Compact ? 1 : 1.4), g);
        }

        // ---- 跑道底色 ----
        ctx.DrawGeometry(new SolidColorBrush(CTrack), null, Band(0, n, HalfWidth));
        // 内外两条边线
        foreach (double w in new[] { HalfWidth, -HalfWidth })
        {
            var o = Offset(w, 0, n);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(o[0], false);
                for (int i = 1; i < n; i++) g.LineTo(o[i]);
                g.EndFigure(true);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(CLane, 0.55), Compact ? 0.9 : 1.2), geo);
        }

        // ---- 已跑里程 ----
        double totalM = Math.Max(t.Perimeter, 1);
        int lapsDone = (int)Math.Floor(_displayDistance / totalM);
        double lapFrac = Fract(_displayDistance / totalM);
        int lapIdx = (int)Math.Min(Math.Round(lapFrac * n), n);

        if (lapsDone >= 1)
        {
            ctx.DrawGeometry(new SolidColorBrush(CTrackDone, 0.92), null, Band(0, n, HalfWidth));
        }
        else if (lapIdx >= 1)
        {
            ctx.DrawGeometry(new SolidColorBrush(CTrackDone, 0.92), null, Band(0, Math.Min(lapIdx + 1, n), HalfWidth));
        }

        // ---- 分道线（大图才画，8 道）----
        if (!Compact)
        {
            for (int lane = 1; lane < Lanes; lane++)
            {
                double w = -HalfWidth + 2 * HalfWidth * lane / Lanes;
                var o = Offset(w, 0, n);
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(o[0], false);
                    for (int i = 1; i < n; i++) g.LineTo(o[i]);
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(CLane, 0.16), 0.9), geo);
            }
        }

        // ---- 当前圈的亮色高光 ----
        int hiCount = Math.Min(lapIdx + 1, n);
        if (hiCount >= 2)
        {
            var hi = Offset(HalfWidth * 0.66, 0, hiCount);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(hi[0], false);
                for (int k = 1; k < hi.Length; k++) g.LineTo(hi[k]);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(CAccent, 0.85), Compact ? 2.2 : 3.0,
                lineCap: PenLineCap.Round), geo);
        }

        // ---- 起终点线 ----
        {
            var p0 = smooth[0];
            var nrm = normals[0];
            var a = Project(p0.X + nrm.X * HalfWidth, p0.Y + nrm.Y * HalfWidth);
            var b = Project(p0.X - nrm.X * HalfWidth, p0.Y - nrm.Y * HalfWidth);
            ctx.DrawLine(new Pen(new SolidColorBrush(CLane, 0.75), Compact ? 1.4 : 2.0), a, b);
        }

        // ---- 网格（仅大图）----
        if (!Compact)
        {
            var gridPen = new Pen(new SolidColorBrush(CLane, 0.10), 0.8);
            double step = 20;
            for (double gy = -Math.Ceiling(halfY / step) * step; gy <= halfY + step; gy += step)
            {
                var a = Project(-halfX - 30, gy);
                var b = Project(halfX + 30, gy);
                if (Math.Max(a.Y, b.Y) < 0 || Math.Min(a.Y, b.Y) > bounds.Height) continue;
                ctx.DrawLine(gridPen, a, b);
            }
            for (double gx = -Math.Ceiling(halfX / step) * step; gx <= halfX + step; gx += step)
            {
                var a = Project(gx, -halfY - 30);
                var b = Project(gx, halfY + 30);
                if (Math.Max(a.X, b.X) < 0 || Math.Min(a.X, b.X) > bounds.Width) continue;
                ctx.DrawLine(gridPen, a, b);
            }
        }

        // ---- 打卡点 ----
        int cps = Math.Clamp(CheckpointCount, 0, 12);
        for (int k = 0; k < cps; k++)
        {
            double f = (double)k / cps;
            var lp = LocalAt(GeoAt(f));
            var sp = Project(lp.X, lp.Y);
            bool passed = _displayDistance > 0 && Fract(_displayDistance / totalM) >= f
                          || lapsDone >= 1;
            double pulse = 0.5 + 0.5 * Math.Sin(_clock * 3.0 + k * 1.1);
            double r = Compact ? 3.0 : 4.4;
            var col = passed ? CAccent : CAccentAlt;

            if (!passed && Running)
            {
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(col, 0.30 + 0.45 * pulse), 1.2),
                    sp, r + 3 + pulse * 5, r + 3 + pulse * 5);
            }
            ctx.DrawEllipse(new SolidColorBrush(col, passed ? 0.95 : 0.5), null, sp, r, r);
            if (!Compact)
            {
                var ft = new FormattedText((k + 1).ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 9,
                    new SolidColorBrush(col, 0.85));
                ctx.DrawText(ft, new Point(sp.X - ft.Width / 2, sp.Y - r - 12));
            }
        }

        // ---- 拖影 ----
        for (int k = 0; k < _trail.Count; k++)
        {
            var tr = _trail[k];
            double age = Math.Clamp(1.0 - (_clock - tr.T) / 2.4, 0, 1);
            var sp = Project(tr.X, tr.Y);
            double r = (Compact ? 1.9 : 2.6) * age + 0.5;
            ctx.DrawEllipse(new SolidColorBrush(CAccent, 0.40 * age), null, sp, r, r);
        }

        // ---- 奶蛙 ----
        // 以前是位图图集 + 逐帧步态：256×256 的格子放大到场景里就糊，
        // 而且每帧换姿态看起来「一跳一跳」。现在直接画建模（软件光栅的矢量三角形），
        // 场景多大都清晰；姿态固定，只跟着跑道位置移动 —— 用户要的就是「建模在动」。
        {
            double f0 = Frac(_displayDistance);
            var lp = LocalAt(GeoAt(f0));
            var ground = Project(lp.X, lp.Y);

            // 透视缩放：远侧（ry 大）的人小一点，近侧大一点，才有纵深
            double ry = lp.X * sinY + lp.Y * cosY;
            double depth = Math.Max(camDist + ry * cosP, 1e-3);
            double persp = Math.Clamp(camDist / depth, 0.62, 1.55);

            // 朝向：左半边面向用户、右半边背对用户，中间平滑过渡
            double facingYaw = MeshSprite.YawForTrackPosition(lp.X, halfX);

            double h = (Compact ? 46 : 88) * persp;
            DebugFrogDetail = $"f0={f0:F3} lp=({lp.X:F1},{lp.Y:F1}) halfX={halfX:F1} ground=({ground.X:F1},{ground.Y:F1})";
            DebugFrog = (ground.X, ground.Y, facingYaw, h);
            MeshSprite.Draw(ctx, new Point(ground.X, ground.Y), h, facingYaw, Compact ? 16 : 12, MeshSprite.Tier.Auto);
        }

        // ---- 角标 ----
        if (!Compact)
        {
            var live = new FormattedText(Running ? "● LIVE" : "◦ 待机", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 10,
                new SolidColorBrush(Running ? CAccent : Color.Parse("#55637A")));
            ctx.DrawText(live, new Point(12, 9));

            var hud = new FormattedText(
                $"{(_displayDistance / 1000.0):F2} / {(TargetMeters > 0 ? TargetMeters / 1000.0 : totalM / 1000.0):F2} km"
                + $" · 第 {lapsDone + 1} 圈 {(lapFrac * 100):F0}%",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Normal), 10,
                new SolidColorBrush(Color.Parse("#7D8DA3")));
            ctx.DrawText(hud, new Point(bounds.Width - hud.Width - 12, 9));
        }
    }
}
