using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Application;

/// <summary>Compõe conhecimento e HTML para uma execução já concluída, sem agregar repairs de outros escopos.</summary>
public sealed class DiagnosticAssessmentService(
    IKnowledgeRepository knowledgeRepository,
    RecommendationEngine recommendationEngine,
    RootCauseAnalyzer rootCauseAnalyzer,
    HtmlDiagnosticReportFormatter htmlFormatter)
{
    public async Task<string> CreateHtmlReportAsync(DiagnosticRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        run = DiagnosticPrivacyRedactor.Redact(run);
        var rules = await knowledgeRepository.GetLatestRulesAsync(cancellationToken).ConfigureAwait(false);
        var recommendations = run.Report is null
            ? Array.Empty<DiagnosticRecommendation>()
            : recommendationEngine.Recommend(run.Report, rules, run.Inventory);
        var analysis = run.Report is null
            ? new RootCauseAnalysis("A execução não contém resultados diagnósticos; a causa raiz permanece indeterminada.", Array.Empty<CorrelationObservation>())
            : rootCauseAnalyzer.Analyze(run.Report);
        return htmlFormatter.Format(run, recommendations, analysis, rules.Count);
    }
}
