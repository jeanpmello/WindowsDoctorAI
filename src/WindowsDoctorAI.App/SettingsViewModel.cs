using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.App;

public partial class SettingsViewModel(IUserSettingsRepository settingsRepository, ILogger<SettingsViewModel> logger) : ObservableObject
{
    [ObservableProperty] private bool _saveDiagnosticHistory = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "As preferências são mantidas localmente neste computador.";

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            SaveDiagnosticHistory = (await settingsRepository.GetAsync()).SaveDiagnosticHistory;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "As preferências locais não puderam ser carregadas.");
            StatusMessage = "Não foi possível carregar as preferências salvas.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await settingsRepository.SaveAsync(new UserSettings { SaveDiagnosticHistory = SaveDiagnosticHistory });
            StatusMessage = "Configurações salvas localmente.";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "As preferências locais não puderam ser salvas.");
            StatusMessage = "Não foi possível salvar as configurações no banco local.";
        }
        finally { IsBusy = false; }
    }
}
