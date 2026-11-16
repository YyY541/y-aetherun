using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Aetherun.Desktop.Controls;

/// <summary>
/// 开屏动画播放器。帧序列从 Assets/splash/s0001.jpg 起，24 fps、共 381 帧（1024x768）。
/// 不做视频解码，只按索引切图，所以启动开销接近零、也不挑机器。
/// </summary>
public sealed class SplashPlayer : Control
{
    /// <summary>帧总数，与 Assets/splash 下的文件数一致。</summary>
    public const int FrameCount = 381;

    public const int Fps = 24;

    private const string FrameUriFormat = "avares://Aetherun/Assets/splash/s{0:0000}.jpg";

    private readonly Dictionary<int, Bitmap?> _cache = new();
    private readonly DispatcherTimer? _timer;

    private int _frame;
    private double _acc;
    private bool _playing;

    public SplashPlayer()
    {
        ClipToBounds = true;

        if (!Design.IsDesignMode)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / Fps) };
            // 控件被藏起来（切到别的页面）时不要继续换帧：
            // 24fps 去读 JPEG 解码，在后台空跑纯属浪费。
            _timer.Tick += (_, _) =>
            {
                if (!IsEffectivelyVisible) return;
                Advance(1.0 / Fps);
            };
        }
    }

    // ==== 属性 ====

    /// <summary>是否循环播放。默认播放一遍停在末帧。</summary>
    public static readonly StyledProperty<bool> LoopProperty =
        AvaloniaProperty.Register<SplashPlayer, bool>(nameof(Loop));

    public bool Loop
    {
        get => GetValue(LoopProperty);
        set => SetValue(LoopProperty, value);
    }

    /// <summary>画面是否要做暗角与上下渐隐（开屏窗口用 true，页内预览用 false）。</summary>
    public static readonly StyledProperty<bool> CinematicProperty =
        AvaloniaProperty.Register<SplashPlayer, bool>(nameof(Cinematic), true);

    public bool Cinematic
    {
        get => GetValue(CinematicProperty);
        set => SetValue(CinematicProperty, value);
    }

    public static readonly DirectProperty<SplashPlayer, bool> IsPlayingProperty =
        AvaloniaProperty.RegisterDirect<SplashPlayer, bool>(nameof(IsPlaying), o => o.IsPlaying);

    /// <summary>当前是否在播放。</summary>
    public bool IsPlaying
    {
        get => _playing;
        private set => SetAndRaise(IsPlayingProperty, ref _playing, value);
    }

    public static readonly DirectProperty<SplashPlayer, int> FrameIndexProperty =
        AvaloniaProperty.RegisterDirect<SplashPlayer, int>(nameof(FrameIndex), o => o.FrameIndex);

    /// <summary>当前帧下标（0 起）。</summary>
    public int FrameIndex
    {
        get => _frame;
        private set
        {
            if (_frame == value) return;
            SetAndRaise(FrameIndexProperty, ref _frame, value);
            InvalidateVisual();
        }
    }

    public static readonly DirectProperty<SplashPlayer, double> ProgressProperty =
        AvaloniaProperty.RegisterDirect<SplashPlayer, double>(nameof(Progress), o => o.Progress);

    /// <summary>播放进度 0..1，给外部的进度条用。</summary>
    public double Progress => FrameCount <= 1 ? 1 : (double)_frame / (FrameCount - 1);

    /// <summary>播完了没有（用于外部自动关闭）。</summary>
    public bool IsFinished => !_playing && _frame >= FrameCount - 1;

    // ==== 控制 ====

    /// <summary>从头开始播。</summary>
    public void Play()
    {
        _frame = 0;
        _acc = 0;
        SetAndRaise(FrameIndexProperty, ref _frame, 0);
        IsPlaying = true;
        OnPropertyChanged(new AvaloniaPropertyChangedEventArgs<double>(this, ProgressProperty, 0.0, Progress, Avalonia.Data.BindingPriority.LocalValue));
        InvalidateVisual();
        EnsureTimer();
    }

    /// <summary>停在当前帧。</summary>
    public void Pause()
    {
        IsPlaying = false;
        _timer?.Stop();
    }

    /// <summary>直接跳到某一帧（用于外部拖动进度）。</summary>
    public void Seek(int frame)
    {
        _acc = 0;
        FrameIndex = Math.Clamp(frame, 0, FrameCount - 1);
        OnPropertyChanged(new AvaloniaPropertyChangedEventArgs<double>(this, ProgressProperty, 0.0, Progress, Avalonia.Data.BindingPriority.LocalValue));
    }

    /// <summary>按秒推进；测试里可以直接喂 dt，不依赖真实时钟。</summary>
    public void Advance(double dt)
    {
        if (!IsPlaying) return;

        _acc += dt;
        double step = 1.0 / Fps;
        if (_acc < step) return;

        int advance = (int)(_acc / step);
        _acc -= advance * step;

        int next = _frame + advance;
        if (next >= FrameCount)
        {
            if (Loop)
            {
                next %= FrameCount;
            }
            else
            {
                next = FrameCount - 1;
                IsPlaying = false;
                _timer?.Stop();
            }
        }

        FrameIndex = next;
        OnPropertyChanged(new AvaloniaPropertyChangedEventArgs<double>(this, ProgressProperty, 0.0, Progress, Avalonia.Data.BindingPriority.LocalValue));
    }

    private void EnsureTimer()
    {
        _timer?.Start();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsPlaying) EnsureTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
    }

    // ==== 绘制 ====

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 2 || bounds.Height < 2) return;

        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#05070A")), bounds);

        var bmp = Frame(_frame);
        if (bmp is null) return;

        double srcW = bmp.PixelSize.Width;
        double srcH = bmp.PixelSize.Height;
        if (srcW <= 0 || srcH <= 0) return;

        // 铺满（cover）：宁可裁掉边角，也不要在两侧留黑边
        double scale = Math.Max(bounds.Width / srcW, bounds.Height / srcH);
        double w = srcW * scale;
        double h = srcH * scale;
        var dest = new Rect((bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h);

        // 注意力集中在画面中心：淡入 + 缓慢推近
        double t = FrameCount <= 1 ? 1 : (double)_frame / (FrameCount - 1);
        double zoom = Cinematic ? 1.0 + 0.055 * t : 1.0;
        if (zoom > 1.0)
        {
            double zw = dest.Width * zoom;
            double zh = dest.Height * zoom;
            dest = new Rect(dest.X - (zw - dest.Width) / 2, dest.Y - (zh - dest.Height) / 2, zw, zh);
        }

        ctx.DrawImage(bmp, new Rect(0, 0, srcW, srcH), dest);

        if (!Cinematic) return;

        // 起手黑场淡入（前 28 帧）
        double fade = Math.Clamp(_frame / 28.0, 0, 1);
        if (fade < 1.0)
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * (1 - fade)), 5, 7, 10)), bounds);

        // 上下渐隐，让标题与进度条压得住画面
        var topFade = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(235, 4, 6, 9), 0),
                new GradientStop(Color.FromArgb(150, 4, 6, 9), 0.22),
                new GradientStop(Color.FromArgb(0, 4, 6, 9), 0.46),
            }
        };
        ctx.FillRectangle(topFade, new Rect(0, 0, bounds.Width, bounds.Height * 0.55));

        var bottomFade = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(245, 4, 6, 9), 0),
                new GradientStop(Color.FromArgb(170, 4, 6, 9), 0.20),
                new GradientStop(Color.FromArgb(0, 4, 6, 9), 0.45),
            }
        };
        ctx.FillRectangle(bottomFade, new Rect(0, bounds.Height * 0.55, bounds.Width, bounds.Height * 0.45));

        // 左右各压一点，暗角更重
        var leftVig = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(120, 4, 6, 9), 0),
                new GradientStop(Color.FromArgb(0, 4, 6, 9), 0.18),
            }
        };
        ctx.FillRectangle(leftVig, new Rect(0, 0, bounds.Width, bounds.Height));

        var rightVig = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(120, 4, 6, 9), 0),
                new GradientStop(Color.FromArgb(0, 4, 6, 9), 0.18),
            }
        };
        ctx.FillRectangle(rightVig, new Rect(0, 0, bounds.Width, bounds.Height));
    }

    private Bitmap? Frame(int index)
    {
        index = Math.Clamp(index, 0, FrameCount - 1);
        if (_cache.TryGetValue(index, out var cached)) return cached;

        Bitmap? bmp = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri(string.Format(FrameUriFormat, index + 1)));
            bmp = new Bitmap(stream);
        }
        catch
        {
            bmp = null;
        }

        _cache[index] = bmp;
        Trim();
        return bmp;
    }

    /// <summary>只留当前帧附近的十几张，避免 381 张全解码吃掉几百兆。</summary>
    private void Trim()
    {
        if (_cache.Count <= 16) return;

        foreach (int key in _cache.Keys.OrderBy(k => Math.Abs(k - _frame)).Skip(12).ToList())
        {
            if (key == _frame) continue;
            _cache[key]?.Dispose();
            _cache.Remove(key);
        }
    }
}
