using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Aetherun.Desktop.ViewModels;

namespace Aetherun.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => AvaloniaXamlLoader.Load(this);

    private void OnPreset(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not Button { Tag: string name }) return;

        switch (name)
        {
            case "slow": vm.PresetSlowCommand.Execute(null); break;
            case "fast": vm.PresetFastCommand.Execute(null); break;
            default: vm.PresetStandardCommand.Execute(null); break;
        }
    }
}
