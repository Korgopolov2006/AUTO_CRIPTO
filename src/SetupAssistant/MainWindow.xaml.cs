using System.Windows;
using SetupAssistant.ViewModels;

namespace SetupAssistant;

/// <summary>Главное окно приложения — хост текущей страницы (Shell).</summary>
public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
