using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Aetherun.Desktop.Controls;

namespace Aetherun.Desktop.Views;

public partial class TrackView : UserControl
{
    private bool _syncing;

    public TrackView()
    {
        AvaloniaXamlLoader.Load(this);

        var frog = this.FindControl<FrogPreview>("Frog");
        var yaw = this.FindControl<Slider>("YawSlider");
        if (frog is null || yaw is null) return;

        // 拖滑块 = 手动转角度；停在哪儿就是哪儿，预览不会自己动
        yaw.PropertyChanged += (_, e) =>
        {
            if (_syncing || e.Property != Slider.ValueProperty) return;
            frog.Yaw = yaw.Value;
        };
    }

    private void OnFace(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        if (!double.TryParse(tag, out double deg)) return;

        var frog = this.FindControl<FrogPreview>("Frog");
        var yaw = this.FindControl<Slider>("YawSlider");
        if (frog is null) return;

        frog.Yaw = deg;

        if (yaw is not null)
        {
            _syncing = true;
            yaw.Value = deg;
            _syncing = false;
        }
    }

    private void OnAuto(object? sender, RoutedEventArgs e)
    {
        // 「自动」= 回到默认正面，不再做自转（用户要的是「建模放在那里不动」）
        var frog = this.FindControl<FrogPreview>("Frog");
        var yaw = this.FindControl<Slider>("YawSlider");
        if (frog is null) return;
        frog.Yaw = 0;
        if (yaw is not null)
        {
            _syncing = true;
            yaw.Value = 0;
            _syncing = false;
        }
    }
}
