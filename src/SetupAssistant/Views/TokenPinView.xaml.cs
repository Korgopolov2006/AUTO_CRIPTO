using System.Windows;
using System.Windows.Controls;
using SetupAssistant.ViewModels;

namespace SetupAssistant.Views;

/// <summary>Смена PIN-кода носителя. PIN-коды читаются из PasswordBox только в момент нажатия и не хранятся в ViewModel.</summary>
public partial class TokenPinView : UserControl
{
    public TokenPinView()
    {
        InitializeComponent();
        Unloaded += (_, _) => ClearFields();
    }

    private async void OnChangeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TokenPinViewModel viewModel)
        {
            return;
        }

        var changed = await viewModel.ChangePinAsync(CurrentPin.Password, NewPin.Password, ConfirmPin.Password);
        if (changed)
        {
            ClearFields();
        }
    }

    private void ClearFields()
    {
        CurrentPin.Clear();
        NewPin.Clear();
        ConfirmPin.Clear();
    }
}
