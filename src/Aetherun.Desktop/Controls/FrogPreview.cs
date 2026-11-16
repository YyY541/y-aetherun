using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Aetherun.Core.Models;

namespace Aetherun.Desktop.Controls;

/// <summary>
/// 奶蛙预览：一只**静止**的奶蛙建模摆在那儿，左右拖动（或拖滑块）转着看。
///
/// 从图集换成真建模的原因（用户实测反馈）：图集是 256×256 的位图，
/// 放大到面板尺寸就糊成一团。这里直接吃 NWM1 网格、按三角形现算矢量填充，
/// 面板多大都是锐的。
///
/// 它不再自己跑，也不起伏 —— 静止的姿态才看得清细节；跑动交给跑道上的那只。
/// </summary>
public sealed class FrogPreview : Control
{
    /// <summary>朝向（度）。0 = 正面朝镜头，180 = 背对。</summary>
    public static readonly StyledProperty<double> YawProperty =
        AvaloniaProperty.Register<FrogPreview, double>(nameof(Yaw));

    /// <summary>俯角（度）。默认略微俯视，比纯正视更有立体感。</summary>
    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<FrogPreview, double>(nameof(Pitch), 10);

    /// <summary>保留给外部绑定用（跑道页会传当前步频），预览本身不随之运动。</summary>
    public static readonly StyledProperty<double> CadenceProperty =
        AvaloniaProperty.Register<FrogPreview, double>(nameof(Cadence), 110);

    public static readonly StyledProperty<TrackLoop?> TrackProperty =
        AvaloniaProperty.Register<FrogPreview, TrackLoop?>(nameof(Track));

    static FrogPreview()
    {
        AffectsRender<FrogPreview>(YawProperty, PitchProperty, CadenceProperty, TrackProperty);
    }

    public double Yaw
    {
        get => GetValue(YawProperty);
        set => SetValue(YawProperty, value);
    }

    public double Pitch
    {
        get => GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    public double Cadence
    {
        get => GetValue(CadenceProperty);
        set => SetValue(CadenceProperty, value);
    }

    public TrackLoop? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    private bool _dragging;
    private double _dragStartX;
    private double _dragStartYaw;

    public FrogPreview()
    {
        // 拖动就是转角度：按下记起点，横移换算成角度，松手停在原地
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true;
            _dragStartX = e.GetPosition(this).X;
            _dragStartYaw = Yaw;
            e.Pointer.Capture(this);
            e.Handled = true;
        };

        PointerMoved += (_, e) =>
        {
            if (!_dragging) return;
            double dx = e.GetPosition(this).X - _dragStartX;
            // 每 1 像素 ≈ 0.9°，拖过整个面板大约转一圈多一点
            Yaw = (_dragStartYaw - dx * 0.9) % 360.0;
            if (Yaw < 0) Yaw += 360.0;
            e.Handled = true;
        };

        PointerReleased += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
        };

        PointerCaptureLost += (_, _) =>
        {
            if (!_dragging) return;
            _dragging = false;
            InvalidateVisual();
        };
    }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        if (b.Width < 8 || b.Height < 8) return;

        // 背景：一层很淡的径向光，让奶蛙不像是贴在纯色上
        var bg = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.60, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.60, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.76, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.76, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#17261E"), 0),
                new GradientStop(Color.Parse("#0A0F15"), 1),
            }
        };
        ctx.FillRectangle(bg, b);

        // 地面：一圈很淡的椭圆，给建模一个落脚处
        double groundY = b.Height * 0.86;
        ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#1B2A22"), 0.9), null,
            new Point(b.Width / 2, groundY), b.Width * 0.28, b.Height * 0.072);

        DrawYawRuler(ctx, b, groundY);

        // 建模高度：面板宽度的 78%，同时不超过可用高度；脚底钉在 groundY
        double usableH = groundY - b.Height * 0.06;
        double h = Math.Min(b.Width * 0.78, usableH);

        // 拖动时用中等密度 + 1.25× 超采样：每跨 3° 才重算一张，~8 ms 一张跟得上手；
        // 松手立刻换回高模 + 2.5× 超采样，静止时看到的永远是最高画质。
        bool ok = MeshSprite.Draw(ctx, new Point(b.Width / 2, groundY), h, Yaw, Pitch,
            _dragging ? MeshSprite.Tier.Mid : MeshSprite.Tier.High,
            _dragging ? 1.25 : 2.5);

        var tier = _dragging ? MeshSprite.Tier.Mid : MeshSprite.Tier.High;
        var label = new FormattedText(
            ok
                ? $"MESH {MeshSprite.VertexCount(tier)}v / {MeshSprite.TriangleCount(tier)}t   YAW {Normalize(Yaw),3:F0}°"
                : "MESH 资源未加载",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default, 9.5, new SolidColorBrush(Color.Parse("#5F6E85")));
        ctx.DrawText(label, new Point(12, b.Height - 20));
    }

    private static double Normalize(double deg)
    {
        double d = deg % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    private void DrawYawRuler(DrawingContext ctx, Rect b, double groundY)
    {
        var pen = new Pen(new SolidColorBrush(Color.Parse("#1E2A36")), 1);
        double cx = b.Width / 2;
        double r = Math.Min(b.Width * 0.34, b.Height * 0.24);

        for (int i = 0; i < 24; i++)
        {
            double a = i * Math.PI / 12.0;
            double len = i % 6 == 0 ? 8 : 4;
            double x1 = cx + Math.Cos(a) * r;
            double y1 = groundY + Math.Sin(a) * r * 0.34;
            double x2 = cx + Math.Cos(a) * (r + len);
            double y2 = groundY + Math.Sin(a) * (r + len) * 0.34;
            ctx.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
        }
    }
}
