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

    private async void SaveSettings_Click(object sender, RoutedEventArgs args)
    {
        var confirmed = _viewModel.DiagnosticRetentionDays == 0;
        if (!confirmed)
        {
            var days = _viewModel.DiagnosticRetentionDays;
            var dialog = new ContentDialog
            {
                Title = "Confirmar expurgo do histórico por idade?",
                Content = $"Ao salvar o prazo de {days} dias, execuções SQLite com data UTC anterior ao limite serão removidas agora. Isso também se aplica se o salvamento de novos diagnósticos estiver desligado. Cancelar preserva as preferências atuais e todos os registros. Arquivos HTML exportados, histórico de reparos e pacotes não são apagados.",
                PrimaryButtonText = "Salvar e aplicar expurgo",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        await _viewModel.SaveAsync(confirmed);
    }

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
