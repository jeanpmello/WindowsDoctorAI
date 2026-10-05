using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

public sealed record DiagnosticHistoryDeletionResult(bool WasCanceled, int DeletedRecords);

/// <summary>Aplica retenção explícita apenas ao histórico local de execuções diagnósticas.</summary>
public sealed class DiagnosticHistoryMaintenanceService(
    IDiagnosticRunRepository repository,
    IRepairEvidenceGate? evidenceGate = null)
{
    private readonly IRepairEvidenceGate _evidenceGate = evidenceGate ?? new RepairEvidenceGate();
    private const int MaximumRetentionDays = 3650;

    public async Task<int> PurgeExpiredAsync(
        UserSettings settings,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.DiagnosticRetentionDays == 0) return 0;
        if (settings.DiagnosticRetentionDays is < 1 or > MaximumRetentionDays)
            throw new ArgumentOutOfRangeException(nameof(settings), "A retenção de diagnósticos deve ser 0 (desativada) ou entre 1 e 3650 dias.");

        var cutoffUtc = nowUtc.ToUniversalTime().AddDays(-settings.DiagnosticRetentionDays);
        await using var lease = await _evidenceGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        lease.AdvanceGeneration();
        return await repository.DeleteCompletedBeforeAsync(cutoffUtc, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Solicita confirmação antes de apagar qualquer execução; cancelamento não modifica o banco.</summary>
    public async Task<DiagnosticHistoryDeletionResult> ClearAllAsync(
        Func<CancellationToken, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmAsync);
        if (!await confirmAsync(cancellationToken).ConfigureAwait(false))
            return new DiagnosticHistoryDeletionResult(WasCanceled: true, DeletedRecords: 0);

        await using var lease = await _evidenceGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        lease.AdvanceGeneration();
        var deleted = await repository.DeleteAllAsync(cancellationToken).ConfigureAwait(false);
        return new DiagnosticHistoryDeletionResult(WasCanceled: false, DeletedRecords: deleted);
    }
}
