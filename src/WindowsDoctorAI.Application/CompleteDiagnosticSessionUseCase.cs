using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

public sealed record CompleteDiagnosticSessionResult(
    DiagnosticRun? Run,
    ManualGuidanceAssessment? Guidance);

/// <summary>Executa o ciclo completo de diagnóstico e prepara a avaliação manual associada.</summary>
public sealed class CompleteDiagnosticSessionUseCase(
    RunComputerInventoryDiagnosticUseCase runComputerInventoryDiagnosticUseCase,
    DiagnosticAssessmentService assessmentService)
{
    public async Task<CompleteDiagnosticSessionResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var outcome = await runComputerInventoryDiagnosticUseCase.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (outcome.Run is null)
            return new CompleteDiagnosticSessionResult(null, null);

        var guidance = await assessmentService
            .CreateManualGuidanceAssessmentAsync(outcome.Run, cancellationToken)
            .ConfigureAwait(false);

        return new CompleteDiagnosticSessionResult(outcome.Run, guidance);
    }
}
