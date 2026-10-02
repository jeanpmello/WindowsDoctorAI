using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.App;

public sealed record DiagnosticRetentionOption(int Days, string Label);

public partial class SettingsViewModel(
    IUserSettingsRepository settingsRepository,
    DiagnosticPreferencesService preferencesService,
    DiagnosticHistoryMaintenanceService historyMaintenance,
    ILogger<SettingsViewModel> logger) : ObservableObject
{
    [ObservableProperty] private bool _saveDiagnosticHistory;
    [ObservableProperty] private int _diagnosticRetentionDays;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "As preferências são mantidas localmente neste computador.";

    public IReadOnlyList<DiagnosticRetentionOption> RetentionOptions { get; } =
    [
        new(0, "Nunca (conservar sem expurgo por idade)"),
        new(30, "30 dias"),
        new(90, "90 dias"),
        new(180, "180 dias"),
        new(365, "365 dias")
    ];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await settingsRepository.GetAsync();
            SaveDiagnosticHistory = settings.SaveDiagnosticHistory;
            DiagnosticRetentionDays = settings.DiagnosticRetentionDays;
        }
        catch (Exception)
        {
            logger.LogWarning("As preferências locais não puderam ser carregadas; detalhes omitidos por privacidade.");
            StatusMessage = "Não foi possível carregar as preferências salvas.";
        }
        finally { IsBusy = false; }
    }

    public async Task SaveAsync(bool confirmAgeBasedPurge)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (!RetentionOptions.Any(option => option.Days == DiagnosticRetentionDays))
            {
                StatusMessage = "Selecione um prazo de retenção disponível.";
                return;
            }

            var settings = new UserSettings
            {
                SaveDiagnosticHistory = SaveDiagnosticHistory,
                DiagnosticRetentionDays = DiagnosticRetentionDays
            };
            var result = await preferencesService.SaveAsync(settings, confirmAgeBasedPurge, DateTimeOffset.UtcNow);
            StatusMessage = result.WasCanceled
                ? "Operação cancelada; preferências e histórico não foram alterados."
                : result.PurgedRecords == 0
                    ? "Configurações salvas localmente. Nenhum registro expirado precisou ser removido."
                    : $"Configurações salvas; {result.PurgedRecords} execução(ões) expirada(s) foram removidas do histórico SQLite.";
        }
        catch (Exception)
        {
            logger.LogError("As preferências não puderam ser salvas/aplicadas; detalhes omitidos por privacidade.");
            StatusMessage = "Não foi possível salvar ou aplicar as configurações no banco local.";
        }
        finally { IsBusy = false; }
    }

    public async Task ClearDiagnosticHistoryAsync(Func<CancellationToken, Task<bool>> confirmAsync)
    {
        ArgumentNullException.ThrowIfNull(confirmAsync);
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await historyMaintenance.ClearAllAsync(confirmAsync);
            StatusMessage = result.WasCanceled
                ? "Exclusão cancelada; nenhum registro foi removido."
                : result.DeletedRecords == 0
                    ? "Não havia execuções no histórico SQLite para remover."
                    : $"Histórico de diagnósticos apagado: {result.DeletedRecords} execução(ões) removida(s).";
        }
        catch (Exception)
        {
            logger.LogError("Não foi possível apagar o histórico; detalhes omitidos por privacidade.");
            StatusMessage = "Não foi possível apagar o histórico. Nenhum outro arquivo ou dado foi alterado.";
        }
        finally { IsBusy = false; }
    }
}
