using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindowsDoctorAI.App;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs args) => await _viewModel.LoadCommand.ExecuteAsync(null);
}
