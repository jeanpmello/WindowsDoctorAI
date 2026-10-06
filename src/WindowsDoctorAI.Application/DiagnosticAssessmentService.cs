using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Application;

/// <summary>Compõe conhecimento e HTML para uma execução já concluída, sem agregar repairs de outros escopos.</summary>
public sealed class DiagnosticAssessmentService(
    IKnowledgeRepository knowledgeRepository,
    RecommendationEngine recommendationEngine,
    RootCauseAnalyzer rootCauseAnalyzer,
    HtmlDiagnosticReportFormatter htmlFormatter,
    ILogger<DiagnosticAssessmentService>? logger = null)
{
    private const int HtmlReportTimeoutSeconds = 30;

    public async Task<string> CreateHtmlReportAsync(DiagnosticRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        
        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(HtmlReportTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, reportTimeout.Token);
        
        try
        {
            var safeRun = DiagnosticPrivacyRedactor.Redact(run);
            var manualGuidance = await CreateManualGuidanceAssessmentAsync(run, linkedCts.Token).ConfigureAwait(false);
            var analysis = safeRun.Report is null
                ? new RootCauseAnalysis("A execução não contém resultados diagnósticos; a causa raiz permanece indeterminada.", Array.Empty<CorrelationObservation>())
                : rootCauseAnalyzer.Analyze(safeRun.Report);
            return htmlFormatter.Format(safeRun, manualGuidance, analysis);
        }
        catch (OperationCanceledException) when (reportTimeout.Token.IsCancellationRequested)
        {
            logger?.LogWarning("Geração de relatório HTML expirou após {TimeoutSeconds} segundos.", HtmlReportTimeoutSeconds);
            throw new OperationCanceledException("A geração do relatório excedeu o tempo limite.", new TimeoutException());
        }
    }

    /// <summary>Cria uma projeção temporária por achado; não grava evidência nem encaminha orientações ao fluxo de reparo.</summary>
    public async Task<ManualGuidanceAssessment> CreateManualGuidanceAssessmentAsync(
        DiagnosticRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        
        try
        {
            var safeRun = DiagnosticPrivacyRedactor.Redact(run);
            var rules = await knowledgeRepository.GetLatestRulesAsync(cancellationToken).ConfigureAwait(false);
            var provenance = await knowledgeRepository.GetLatestRuleProvenanceAsync(cancellationToken).ConfigureAwait(false);
            
            var findings = safeRun.Report is null
                ? Array.Empty<ManualRecommendationFindingAssessment>()
                : recommendationEngine.AssessManualFindings(safeRun.Report, rules, safeRun.Inventory);
            
            return ManualGuidanceProjector.Project(run, rules, provenance, findings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Falha ao criar avaliação de orientações manuais; tipo de exceção: {ExceptionType}", exception.GetType().Name);
            throw;
        }
    }
}
