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

    private async void ClearDiagnosticHistory_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new ContentDialog
        {
            Title = "Apagar todo o histórico de diagnósticos?",
            Content = "Esta ação remove permanentemente apenas as execuções de diagnóstico salvas no SQLite e não pode ser desfeita. Arquivos HTML exportados, preferências, pacotes e histórico de reparos não serão apagados.",
            PrimaryButtonText = "Apagar histórico",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        await _viewModel.ClearDiagnosticHistoryAsync(_ => Task.FromResult(result == ContentDialogResult.Primary));
    }
}
