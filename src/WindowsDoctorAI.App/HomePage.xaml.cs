using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindowsDoctorAI.App;

public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _viewModel;

    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs args) => await _viewModel.LoadLatestAsync();
}
