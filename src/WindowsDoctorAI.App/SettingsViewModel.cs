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
    DiagnosticHistoryMaintenanceService historyMaintenance,
    ILogger<SettingsViewModel> logger) : ObservableObject
{
    [ObservableProperty] private bool _saveDiagnosticHistory = true;
    [ObservableProperty] private int _diagnosticRetentionDays;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "As preferências são mantidas localmente neste computador.";

    public IReadOnlyList<DiagnosticRetentionOption> RetentionOptions { get; } =
    [
        new(0, "Nunca (conservar sem expurgo automático)"),
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
            await settingsRepository.SaveAsync(settings);
            try
            {
                var purged = await historyMaintenance.PurgeExpiredAsync(settings, DateTimeOffset.UtcNow);
                StatusMessage = purged == 0
                    ? "Configurações salvas localmente. Nenhum registro expirado precisou ser removido."
                    : $"Configurações salvas; {purged} execução(ões) expirada(s) foram removidas do histórico SQLite.";
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "As configurações foram salvas, mas o expurgo de retenção falhou.");
                StatusMessage = "Configurações salvas, mas não foi possível aplicar a retenção. O histórico permanece no banco.";
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "As preferências locais não puderam ser salvas.");
            StatusMessage = "Não foi possível salvar as configurações no banco local.";
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
        catch (Exception exception)
        {
            logger.LogError(exception, "Não foi possível apagar o histórico diagnóstico local.");
            StatusMessage = "Não foi possível apagar o histórico. Nenhum outro arquivo ou dado foi alterado.";
        }
        finally { IsBusy = false; }
    }
}
