using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Aetherun.Desktop.ViewModels;

namespace Aetherun.Desktop.Views;

public partial class LogsView : UserControl
{
    private readonly ScrollViewer? _scroll;
    private INotifyCollectionChanged? _watching;

    public LogsView()
    {
        AvaloniaXamlLoader.Load(this);
        _scroll = this.FindControl<ScrollViewer>("LogScroll");
        DataContextChanged += (_, _) => Hook();
        Hook();
    }

    private void Hook()
    {
        if (_watching is not null) _watching.CollectionChanged -= OnLinesChanged;
        _watching = (DataContext as MainViewModel)?.LogLines;
        if (_watching is not null) _watching.CollectionChanged += OnLinesChanged;
    }

    // 日志一多就看不到最新的几条了。跑起来的时候每几秒就有新行，
    // 这里跟着滚到底，省得每次手动拖。
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        if (!IsEffectivelyVisible) return;
        Dispatcher.UIThread.Post(() => _scroll?.ScrollToEnd(), DispatcherPriority.Background);
    }
}
