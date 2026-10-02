using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

public sealed record DiagnosticOutcome(DiagnosticRun Run, bool HistorySaved, string? PersistenceWarning = null);

/// <summary>Coordena a coleta e, quando habilitado nas preferências, o armazenamento local do resultado.</summary>
public sealed class RunComputerInventoryDiagnosticUseCase(
    IComputerInventoryScanner scanner,
    IDiagnosticRunRepository history,
    IUserSettingsRepository settings,
    ILogger<RunComputerInventoryDiagnosticUseCase> logger)
{
    public async Task<DiagnosticOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var inventory = await scanner.ScanAsync(cancellationToken).ConfigureAwait(false);
        var completedAt = DateTimeOffset.UtcNow;
        var run = new DiagnosticRun(Guid.NewGuid(), startedAt, completedAt,
            completedAt - startedAt, inventory, HealthScore.InitialMilestoneScore);

        UserSettings preferences;
        try
        {
            preferences = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Não foi possível carregar preferências; a execução será exibida sem ser salva.");
            return new DiagnosticOutcome(run, false, "Preferências indisponíveis; o resultado não foi salvo no histórico.");
        }

        if (!preferences.SaveDiagnosticHistory)
        {
            return new DiagnosticOutcome(run, false);
        }

        try
        {
            await history.SaveAsync(run, cancellationToken).ConfigureAwait(false);
            return new DiagnosticOutcome(run, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "O diagnóstico terminou, mas não foi possível gravá-lo no histórico local.");
            return new DiagnosticOutcome(run, false, "O diagnóstico foi concluído, mas não foi possível salvá-lo no histórico local.");
        }
    }
}
