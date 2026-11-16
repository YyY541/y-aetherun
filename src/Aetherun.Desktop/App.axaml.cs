using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Aetherun.Desktop.Services;
using Aetherun.Desktop.ViewModels;
using Aetherun.Desktop.Views;

namespace Aetherun.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = new AppShell();
            var vm = new MainViewModel(shell);
            var main = new MainWindow { DataContext = vm };

            // 开屏动画不再是「先弹一个小窗挡在前面」——
            // 那样它是 1000×750 的独立窗口，主界面还没出现，看起来就不像覆盖全应用。
            // 现在主窗先显示，开屏作为主窗内部最上层的一层遮罩铺满整个窗口，
            // 播完（或点击跳过）淡出，露出下面的真实界面。
            desktop.MainWindow = main;
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            main.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
