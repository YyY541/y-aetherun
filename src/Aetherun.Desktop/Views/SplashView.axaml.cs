using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Aetherun.Desktop.Controls;

namespace Aetherun.Desktop.Views;

public partial class SplashView : UserControl
{
    private readonly DispatcherTimer? _poll;
    private bool _syncing;

    // 页面切走时是「我们」把播放暂停的，切回来要接着播；
    // 用户自己按的暂停不能因为切页就丢掉。
    private bool _autoPaused;

    public SplashView()
    {
        AvaloniaXamlLoader.Load(this);

        if (Design.IsDesignMode) return;

        var player = this.FindControl<SplashPlayer>("Player");
        var seek = this.FindControl<Slider>("Seek");
        var label = this.FindControl<TextBlock>("FrameLabel");
        var loop = this.FindControl<CheckBox>("LoopBox");
        var pause = this.FindControl<Button>("PauseBtn");
        var stateLabel = this.FindControl<TextBlock>("StateLabel");
        var timeLabel = this.FindControl<TextBlock>("TimeLabel");
        var playDot = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("PlayDot");

        if (player is null) return;

        // 进页面就播
        player.Play();

        if (loop is not null)
            loop.IsCheckedChanged += (_, _) => player.Loop = loop.IsChecked == true;

        // 注意：PauseBtn 的 Click 已在 XAML 里绑到 OnPause，
        // 这里不能再挂一次，否则一次点击触发两次切换，等于没反应。

        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _poll.Tick += (_, _) =>
        {
            // 页面被切走时（IsVisible=false）别再推进：否则在仪表盘上跑着的时候，
            // 开屏动画还在后台按 24fps 换图，白烧 CPU。
            if (!IsEffectivelyVisible)
            {
                // 切走时停住，切回来保持原帧，不用重新解码一遍
                if (player.IsPlaying)
                {
                    player.Pause();
                    _autoPaused = true;
                }
                return;
            }

            // 切回来：只有是被我们自动停掉的那次才自动续播。
            // 否则第一次进「开屏动画」页会看到卡在第 1 帧、按钮写着 PAUSED ——
            // 构造函数里的 Play() 在页面隐藏时就被上面那段暂停了。
            if (_autoPaused)
            {
                _autoPaused = false;
                player.Play();
            }

            // 按钮文字跟着真实状态走：自动暂停、播完停住时不能还写着「暂停」
            if (pause is not null)
            {
                var want = player.IsPlaying ? "暂停" : "继续";
                if (!Equals(pause.Content, want)) pause.Content = want;
            }

            if (seek is not null)
            {
                _syncing = true;
                seek.Value = player.FrameIndex;
                _syncing = false;
            }
            if (label is not null)
                label.Text = $"{player.FrameIndex + 1:000} / {SplashPlayer.FrameCount}";

            if (stateLabel is not null)
                stateLabel.Text = player.IsPlaying ? "PLAYING" : "PAUSED";
            if (playDot is not null)
                playDot.Fill = player.IsPlaying
                    ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4ADE80"))
                    : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5A6879"));

            if (timeLabel is not null)
            {
                double sec = player.FrameIndex / (double)SplashPlayer.Fps;
                double total = SplashPlayer.FrameCount / (double)SplashPlayer.Fps;
                timeLabel.Text = $"{sec:00.0} / {total:00.0} s";
            }
        };
        _poll.Start();
    }

    private void OnReplay(object? sender, RoutedEventArgs e)
    {
        var player = this.FindControl<SplashPlayer>("Player");
        var pause = this.FindControl<Button>("PauseBtn");
        if (pause is not null) pause.Content = "暂停";
        player?.Play();
    }

    private void OnPause(object? sender, RoutedEventArgs e)
    {
        var player = this.FindControl<SplashPlayer>("Player");
        var pause = this.FindControl<Button>("PauseBtn");
        if (player is null) return;

        if (player.IsPlaying)
        {
            player.Pause();
            if (pause is not null) pause.Content = "继续";
        }
        else
        {
            player.Play();
            if (pause is not null) pause.Content = "暂停";
        }
    }

    private void OnSeek(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        var player = this.FindControl<SplashPlayer>("Player");
        var pause = this.FindControl<Button>("PauseBtn");
        player?.Seek((int)Math.Round(e.NewValue));
        player?.Pause();
        if (pause is not null) pause.Content = "继续";
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _poll?.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _poll?.Stop();
        // 离开页面把动画停住（记成自动暂停，回到页面自动续播）
        var pl = this.FindControl<SplashPlayer>("Player");
        if (pl is not null && pl.IsPlaying)
        {
            pl.Pause();
            _autoPaused = true;
        }
        base.OnDetachedFromVisualTree(e);
    }
}
