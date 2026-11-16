using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Aetherun.Desktop.Controls;

/// <summary>
/// 值滑轮：横向拖拽或滚轮改变数值，指针固定在中间，刻度在下面滚动。
/// 数值永远被钳在 [Minimum, Maximum] 之间并按 Step 吸附，所以用户没法敲出离谱的数字。
/// </summary>
public class ValueDial : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ValueDial, double>(nameof(Value), 0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ValueDial, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ValueDial, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<ValueDial, double>(nameof(Step), 1);

    public static readonly StyledProperty<double> PixelsPerStepProperty =
        AvaloniaProperty.Register<ValueDial, double>(nameof(PixelsPerStep), 11);

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<ValueDial, string>(nameof(Unit), "");

    public static readonly StyledProperty<string> FormatProperty =
        AvaloniaProperty.Register<ValueDial, string>(nameof(Format), "F2");

    public static readonly StyledProperty<IBrush?> ValueBrushProperty =
        AvaloniaProperty.Register<ValueDial, IBrush?>(nameof(ValueBrush), Brushes.White);

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<ValueDial, IBrush?>(nameof(AccentBrush), Brushes.SpringGreen);

    public static readonly StyledProperty<IBrush?> DimBrushProperty =
        AvaloniaProperty.Register<ValueDial, IBrush?>(nameof(DimBrush),
            new SolidColorBrush(Color.Parse("#55637A")));

    /// <summary>刻度两端的淡出遮罩色，取所在卡片的背景色，视觉上刻度像融进卡片。</summary>
    public static readonly StyledProperty<IBrush?> FadeBrushProperty =
        AvaloniaProperty.Register<ValueDial, IBrush?>(nameof(FadeBrush),
            new SolidColorBrush(Color.Parse("#131A24")));

    private double _dragStartValue;
    private double _dragStartX;
    private bool _dragging;

    static ValueDial()
    {
        AffectsRender<ValueDial>(ValueProperty, MinimumProperty, MaximumProperty, StepProperty,
            PixelsPerStepProperty, UnitProperty, FormatProperty,
            ValueBrushProperty, AccentBrushProperty, DimBrushProperty, FadeBrushProperty);
        FocusableProperty.OverrideDefaultValue<ValueDial>(true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEnabledProperty) InvalidateVisual();
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double PixelsPerStep
    {
        get => GetValue(PixelsPerStepProperty);
        set => SetValue(PixelsPerStepProperty, value);
    }

    public string Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public IBrush? ValueBrush
    {
        get => GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? DimBrush
    {
        get => GetValue(DimBrushProperty);
        set => SetValue(DimBrushProperty, value);
    }

    public IBrush? FadeBrush
    {
        get => GetValue(FadeBrushProperty);
        set => SetValue(FadeBrushProperty, value);
    }

    /// <summary>
    /// 按 Step 吸附后钳进 [min, max]。NaN / ∞ 一律退到边界，绝不把脏值传给引擎。
    /// 吸附走整数乘法 + 六位小数回舍，避免 0.1 * 22 这类浮点累积误差。
    /// </summary>
    public static double Snap(double raw, double min, double max, double step)
    {
        if (double.IsNaN(raw)) return min;
        if (double.IsPositiveInfinity(raw)) return max;
        if (double.IsNegativeInfinity(raw)) return min;
        if (step <= 0) return Math.Clamp(raw, min, max);

        double n = Math.Round(raw / step, MidpointRounding.AwayFromZero);
        double v = Math.Round(n * step, 6);
        return Math.Clamp(v, min, max);
    }

    private void Commit(double raw)
    {
        double v = Snap(raw, Minimum, Maximum, Step);
        if (Math.Abs(v - Value) < 1e-9) return;
        SetCurrentValue(ValueProperty, v);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _dragStartX = e.GetPosition(this).X;
        _dragStartValue = Value;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        double dx = e.GetPosition(this).X - _dragStartX;
        double pps = Math.Max(1, PixelsPerStep);
        // 往左拖 = 变大，像把刻度往左拉
        Commit(_dragStartValue - dx / pps * Step);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEnabled) return;
        Commit(Value + Math.Sign(e.Delta.Y) * Step);
        e.Handled = true;
    }

    /// <summary>方向键微调：←/↑ 变大，→/↓ 变小，按住 Shift 一次走 5 格。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled) return;
        int dir = e.Key switch
        {
            Key.Left or Key.Up => 1,
            Key.Right or Key.Down => -1,
            _ => 0
        };
        if (dir == 0) return;

        int mult = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 1;
        Commit(Value + dir * Step * mult);
        e.Handled = true;
    }

    /// <summary>
    /// Format 是普通 .NET 数字格式串时直接套用；写 "pace" 则把值当作「分钟每公里」，
    /// 渲染成 6'50" 这种跑步习惯的写法。
    /// </summary>
    public string FormatValue(double v)
    {
        if (!string.Equals(Format, "pace", StringComparison.OrdinalIgnoreCase))
            return v.ToString(string.IsNullOrWhiteSpace(Format) ? "F2" : Format, CultureInfo.InvariantCulture);

        int totalSec = (int)Math.Round(v * 60);
        int m = totalSec / 60;
        int s = totalSec % 60;
        return $"{m}'{s:00}\"";
    }

    /// <summary>按比例降低画刷不透明度，用于禁用态。</summary>
    private static IBrush Fade(IBrush brush, double alpha)
    {
        if (brush is not ISolidColorBrush s) return brush;
        var c = s.Color;
        byte a = (byte)Math.Clamp(Math.Round(c.A * alpha), 0, 255);
        return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        var value = Snap(Value, Minimum, Maximum, Step);
        var valueBrush = ValueBrush ?? Brushes.White;
        var accent = AccentBrush ?? Brushes.SpringGreen;
        var dim = DimBrush ?? Brushes.Gray;

        // 跑步中会禁用滑轮（防止中途改参数把引擎搞乱），禁用时整体减淡给出视觉反馈
        double alpha = IsEnabled ? 1.0 : 0.42;
        valueBrush = Fade(valueBrush, alpha);
        accent = Fade(accent, alpha);
        dim = Fade(dim, alpha);
        var fadeColor = (FadeBrush as ISolidColorBrush)?.Color ?? Color.Parse("#131A24");
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

        // ---- 数值 ----
        string text = FormatValue(value) + Unit;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, 18, valueBrush);
        ctx.DrawText(ft, new Point((w - ft.Width) / 2, 0));

        // ---- 刻度尺：底部对齐，长刻度每 5 格一条 ----
        double rulerBottom = h - 6;
        double majorLen = 12;
        double minorLen = 7;
        double centerX = w / 2;
        double pps = Math.Max(1, PixelsPerStep);

        double stepped = value / Math.Max(Step, 1e-9);
        long baseIndex = (long)Math.Round(stepped);
        double frac = stepped - baseIndex;   // 拖到两格中间时，刻度整体平移

        int half = (int)Math.Ceiling(w / 2 / pps) + 2;
        var minorPen = new Pen(dim, 1);
        var majorPen = new Pen(dim, 1.6);

        for (long i = baseIndex - half; i <= baseIndex + half; i++)
        {
            double tv = i * Step;
            if (tv < Minimum - 1e-9 || tv > Maximum + 1e-9) continue;

            double x = centerX + (i - baseIndex) * pps - frac * pps;
            if (x < -2 || x > w + 2) continue;

            bool major = i % 5 == 0;
            double len = major ? majorLen : minorLen;
            ctx.DrawLine(major ? majorPen : minorPen,
                new Point(x, rulerBottom - len), new Point(x, rulerBottom));
        }

        // ---- 中央指针：一根竖线 + 底部小三角 ----
        var pointerPen = new Pen(accent, 2, lineCap: PenLineCap.Round);
        ctx.DrawLine(pointerPen, new Point(centerX, rulerBottom - majorLen - 3), new Point(centerX, rulerBottom));
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(centerX - 3.5, rulerBottom + 1), true);
            g.LineTo(new Point(centerX + 3.5, rulerBottom + 1));
            g.LineTo(new Point(centerX, rulerBottom + 4.5));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(accent, null, tri);

        // ---- 两端淡出：做成"滚筒"的观感，中间清晰两头消失 ----
        var fade = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(fadeColor, 0),
                new GradientStop(Color.FromArgb(0, fadeColor.R, fadeColor.G, fadeColor.B), 0.18),
                new GradientStop(Color.FromArgb(0, fadeColor.R, fadeColor.G, fadeColor.B), 0.82),
                new GradientStop(fadeColor, 1),
            }
        };
        ctx.FillRectangle(fade, new Rect(0, rulerBottom - majorLen - 2, w, majorLen + 8));
    }
}
