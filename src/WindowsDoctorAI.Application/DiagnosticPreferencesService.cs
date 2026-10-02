using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

public sealed record DiagnosticPreferencesSaveResult(bool WasCanceled, int PurgedRecords);

/// <summary>Salva preferências e só aplica retenção por idade após confirmação explícita do usuário.</summary>
public sealed class DiagnosticPreferencesService(
    IUserSettingsRepository settingsRepository,
    DiagnosticHistoryMaintenanceService historyMaintenance)
{
    public async Task<DiagnosticPreferencesSaveResult> SaveAsync(
        UserSettings settings,
        bool confirmAgeBasedPurge,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.DiagnosticRetentionDays is < 0 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(settings), "A retenção deve ser 0 (desativada) ou entre 1 e 3650 dias.");
        if (settings.DiagnosticRetentionDays > 0 && !confirmAgeBasedPurge)
            return new DiagnosticPreferencesSaveResult(WasCanceled: true, PurgedRecords: 0);

        await settingsRepository.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        var purged = settings.DiagnosticRetentionDays == 0
            ? 0
            : await historyMaintenance.PurgeExpiredAsync(settings, nowUtc, cancellationToken).ConfigureAwait(false);
        return new DiagnosticPreferencesSaveResult(WasCanceled: false, PurgedRecords: purged);
    }
}
