using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

public sealed record DiagnosticOutcome(DiagnosticRun Run, bool HistorySaved, string? PersistenceWarning = null);

/// <summary>Coordena inventário Milestone 1, plugins diagnósticos e, conforme preferência, histórico local.</summary>
public sealed class RunComputerInventoryDiagnosticUseCase(
    IComputerInventoryScanner inventoryScanner,
    IDiagnosticEngine diagnosticEngine,
    IDiagnosticRunRepository history,
    IUserSettingsRepository settings,
    ILogger<RunComputerInventoryDiagnosticUseCase> logger)
{
    public async Task<DiagnosticOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var inventory = await inventoryScanner.ScanAsync(cancellationToken).ConfigureAwait(false);
        var report = await diagnosticEngine.RunAsync(cancellationToken).ConfigureAwait(false);
        var completedAt = DateTimeOffset.UtcNow;
        var run = new DiagnosticRun(Guid.NewGuid(), startedAt, completedAt,
            completedAt - startedAt, inventory, report);

        UserSettings preferences;
        try
        {
            preferences = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Não foi possível carregar preferências; detalhes omitidos por privacidade e execução não será salva.");
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
            logger.LogWarning("O diagnóstico terminou, mas não foi possível gravá-lo no histórico local; detalhes omitidos por privacidade.");
            return new DiagnosticOutcome(run, false, "O diagnóstico foi concluído, mas não foi possível salvá-lo no histórico local.");
        }
    }
}
