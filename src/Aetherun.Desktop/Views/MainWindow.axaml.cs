using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Aetherun.Desktop.ViewModels;

namespace Aetherun.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 侧栏选中态原来只在鼠标点击时刷新：
        //  · 启动后第一个页面没有高亮；
        //  · 点顶栏「关于」、或启动失败自动跳到「设置」时，高亮还停在上一个页面。
        // 统一改成跟着 SelectedPage 走。
        DataContextChanged += (_, _) => HookViewModel();
        HookViewModel();

        // XAML 里挂着的这层开屏遮罩，得有人让它真的播起来。
        // 之前只声明不启动，启动后画面就冻在第 0 帧、也永远不淡出。
        StartSplashLayer();
    }

    private SplashOverlay? _splash;

    private void StartSplashLayer()
    {
        _splash = this.FindControl<SplashOverlay>("SplashLayer");
        if (_splash is null) return;

        // 点任意处 / 按任意键跳过：挂在窗口上（Tunnel），
        // 这样点顶栏按钮也会先被拦成「跳过」而不是顺手触发那个按钮。
        _splash.HookSkip(this);
        _splash.Finished += OnSplashFinished;
        _splash.Start();
    }

    /// <summary>
    /// 键盘快捷键：F5 开始 / F6 停止 / F7 环境自检。
    /// 开屏还在播时先吃掉这次按键（当成跳过），不误触发跑步。
    /// </summary>
    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            switch (e.Key)
            {
                case Avalonia.Input.Key.F5 when vm.StartCommand.CanExecute(null):
                    vm.StartCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Avalonia.Input.Key.F6 when vm.StopCommand.CanExecute(null):
                    vm.StopCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Avalonia.Input.Key.F7 when vm.RedetectCommand.CanExecute(null):
                    vm.RedetectCommand.Execute(null);
                    e.Handled = true;
                    return;
            }
        }
        base.OnKeyDown(e);
    }

    private void OnSplashFinished(object? sender, EventArgs e)
    {
        // 播完把焦点交给界面，键盘快捷键立刻可用
        try { Focus(); } catch { }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MainViewModel? _hooked;

    private void HookViewModel()
    {
        if (_hooked is not null) _hooked.PropertyChanged -= OnVmPropertyChanged;
        _hooked = DataContext as MainViewModel;
        if (_hooked is null) return;

        _hooked.PropertyChanged += OnVmPropertyChanged;
        SyncHighlight();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPage)) SyncHighlight();
    }

    /// <summary>按当前页面把侧栏那一项点亮。</summary>
    private void SyncHighlight()
    {
        if (_hooked is null) return;
        var target = _hooked.SelectedPage;

        foreach (var btn in this.GetVisualDescendants().OfType<Button>())
        {
            if (!btn.Classes.Contains("nav")) continue;
            bool on = btn.Tag is NavItem item && item.Key == target.Key;
            if (on && !btn.Classes.Contains("nav.active")) btn.Classes.Add("nav.active");
            else if (!on) btn.Classes.Remove("nav.active");
        }
    }

    private void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Button { Tag: NavItem item }) return;
        vm.SelectedPage = item;
        SyncHighlight();
    }

    private void OnReplaySplash(object? sender, RoutedEventArgs e) => ReplaySplash();

    /// <summary>
    /// 重播开屏：遮罩播完会把自己从父容器摘掉，所以这里重新挂一份到最上层。
    /// 先清掉可能残留的旧层，免得连点两次叠两层。
    /// </summary>
    public void ReplaySplash()
    {
        var host = this.FindControl<Grid>("RootHost");
        if (host is null) return;

        for (int i = host.Children.Count - 1; i >= 0; i--)
            if (host.Children[i] is SplashOverlay) host.Children.RemoveAt(i);

        var fresh = new SplashOverlay();
        host.Children.Add(fresh);

        if (_splash is not null) _splash.Finished -= OnSplashFinished;
        _splash = fresh;
        fresh.HookSkip(this);
        fresh.Finished += OnSplashFinished;
        fresh.Start();
    }

    private void OnOpenAbout(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.NavigateTo(NavKey.About);
    }

    // 直接点右上角关窗时，跑步循环还在后台发 ldconsole，
    // 时钟计时器也还在跑。这里先收干净再放行。
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_hooked is null) return;
        try { _hooked.ShutdownAsync().GetAwaiter().GetResult(); } catch { }
    }
}
