using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Application;

/// <summary>Compõe os serviços locais de conhecimento e HTML para uma execução já concluída.</summary>
public sealed class DiagnosticAssessmentService(
    IKnowledgeRepository knowledgeRepository,
    IRepairAuditLog repairAuditLog,
    RecommendationEngine recommendationEngine,
    RootCauseAnalyzer rootCauseAnalyzer,
    HtmlDiagnosticReportFormatter htmlFormatter)
{
    public async Task<string> CreateHtmlReportAsync(DiagnosticRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        var rules = await knowledgeRepository.GetLatestRulesAsync(cancellationToken).ConfigureAwait(false);
        var recommendations = run.Report is null
            ? Array.Empty<DiagnosticRecommendation>()
            : recommendationEngine.Recommend(run.Report, rules);
        var analysis = run.Report is null
            ? new RootCauseAnalysis("A execução não contém resultados diagnósticos; a causa raiz permanece indeterminada.", Array.Empty<CorrelationObservation>())
            : rootCauseAnalyzer.Analyze(run.Report);
        var history = await repairAuditLog.GetRecentAsync(100, cancellationToken).ConfigureAwait(false);
        return htmlFormatter.Format(run, recommendations, analysis, history, rules.Count);
    }
}
