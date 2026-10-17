using System.Windows;
using System.Windows.Controls;
using SetupAssistant.ViewModels;

namespace SetupAssistant.Views;

/// <summary>Журнал действий.</summary>
public partial class LogViewerView : UserControl
{
    public LogViewerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach(DataContext as LogViewerViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach(e.OldValue as LogViewerViewModel);
        if (e.NewValue is LogViewerViewModel vm)
        {
            vm.ScrollToEndRequested += OnScrollToEndRequested;
        }
    }

    private void Detach(LogViewerViewModel? vm)
    {
        if (vm is not null)
        {
            vm.ScrollToEndRequested -= OnScrollToEndRequested;
        }
    }

    private void OnScrollToEndRequested(object? sender, EventArgs e)
    {
        // Список обновляется асинхронно: прокручиваем, когда разметка уже учла новые строки.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (EntriesGrid.Items.Count > 0)
            {
                EntriesGrid.ScrollIntoView(EntriesGrid.Items[^1]);
            }
        });
    }
}
