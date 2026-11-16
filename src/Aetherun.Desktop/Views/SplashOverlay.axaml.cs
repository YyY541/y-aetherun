using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Aetherun.Desktop.Controls;
using Aetherun.Desktop.Services;

namespace Aetherun.Desktop.Views;

/// <summary>
/// 开屏动画遮罩：铺满主窗最上层，播完（或点击/按键跳过）淡出隐藏。
///
/// 之所以不是独立窗口：独立窗口只有 1000×750，主界面还没显示，
/// 用户看到的是「一个小窗单独在放动画」，而不是覆盖整个应用。
/// 盖在主窗里就永远跟客户区同尺寸，缩放、最大化都不会露边。
/// 淡出后只是 <c>IsVisible=false</c>，不摘出可视化树 —— 顶栏的「开屏」按钮还要重播。
/// </summary>
public partial class SplashOverlay : UserControl
{
    private readonly DispatcherTimer? _poll;
    private bool _running;

    public SplashOverlay()
    {
        AvaloniaXamlLoader.Load(this);

        if (Design.IsDesignMode) return;

        var bar = this.FindControl<Border>("Bar");
        var tagline = this.FindControl<StackPanel>("Tagline");

        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _poll.Tick += (_, _) =>
        {
            if (!_running) return;
            var player = this.FindControl<SplashPlayer>("Player");
            if (player is null) return;

            if (bar is not null && Bounds.Width > 0)
                bar.Width = Math.Max(0, Bounds.Width * player.Progress);

            if (tagline is not null)
            {
                // 前 8% 进度里淡入，别一开场就把标题压在画面上
                double t = Math.Clamp((player.Progress - 0.08) / 0.22, 0, 1);
                tagline.Opacity = t;
                tagline.RenderTransform = new TranslateTransform(0, (1 - t) * 16);
            }

            if (player.IsFinished) Finish();
        };
    }

    /// <summary>播完或用户跳过时触发（主窗据此把焦点还给界面）。</summary>
    public event EventHandler? Finished;

    /// <summary>开始（或重播）一遍。已淡出的层会先复位再播。</summary>
    public void Start()
    {
        var player = this.FindControl<SplashPlayer>("Player");
        if (player is null) return;

        Opacity = 1;
        IsVisible = true;
        _running = true;

        player.Play();
        IntroAudio.Play();
        _poll?.Start();
    }

    /// <summary>淡出 260ms 后隐藏（不摘出可视化树，方便重播）。</summary>
    public void Finish()
    {
        if (!_running) return;
        _running = false;

        _poll?.Stop();
        IntroAudio.Stop();
        this.FindControl<SplashPlayer>("Player")?.Pause();

        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(260),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0d) } },
            }
        };

        fade.RunAsync(this).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (!_running) IsVisible = false;
            Finished?.Invoke(this, EventArgs.Empty);
        }), TaskScheduler.Default);
    }

    private void OnSkip(object? sender, RoutedEventArgs e) => Finish();

    /// <summary>点任意处 / 按任意键跳过。用 Tunnel 保证在按钮之前先拦到。</summary>
    public void HookSkip(InputElement host)
    {
        host.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!_running) return;
            Finish();
            e.Handled = true;   // 别让这一下顺手点到下面的按钮
        }, RoutingStrategies.Tunnel);

        host.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!_running) return;
            Finish();
            e.Handled = true;   // 别让这次按键再触发 F5 开跑之类的快捷键
        }, RoutingStrategies.Tunnel);
    }
}
