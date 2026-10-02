using System.Net;
using System.Text;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Reporting;

/// <summary>Gera HTML local com encoding de saída; não carrega scripts, estilos ou imagens remotos.</summary>
public sealed class HtmlDiagnosticReportFormatter
{
    public string Format(
        DiagnosticRun run,
        IReadOnlyList<DiagnosticRecommendation> recommendations,
        RootCauseAnalysis analysis,
        IReadOnlyList<RepairHistoryRecord> repairHistory,
        int knowledgeRuleCount = -1)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(recommendations);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(repairHistory);
        var report = run.Report;
        var builder = new StringBuilder(8192);
        builder.AppendLine("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<title>Windows Doctor AI — Relatório de diagnóstico</title><style>");
        builder.AppendLine("body{font:15px/1.55 system-ui,Segoe UI,Arial,sans-serif;color:#17212b;max-width:980px;margin:2rem auto;padding:0 1.25rem;background:#f5f7fa}header,.card{background:#fff;border:1px solid #d9e0e8;border-radius:12px;padding:1.25rem 1.5rem;margin:1rem 0;box-shadow:0 2px 7px #1525360d}h1,h2{line-height:1.2}h1{font-size:1.8rem}h2{font-size:1.2rem;border-bottom:1px solid #e4e9ef;padding-bottom:.5rem}.score{font-size:2rem;font-weight:700;color:#124d7a}.muted{color:#516170}.item{border-left:4px solid #8094a8;padding:.35rem 0 .35rem 1rem;margin:.8rem 0}.critical{border-color:#b42318}.warning{border-color:#b54708}.pill{display:inline-block;padding:.12rem .5rem;border-radius:1rem;background:#eaf0f6;font-size:.85rem}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:inherit;margin:.35rem 0}a{color:#075985;overflow-wrap:anywhere}footer{font-size:.85rem;color:#52616d;margin:2rem 0}@media print{body{background:#fff;margin:0;max-width:none}.card,header{box-shadow:none;break-inside:avoid}a{color:inherit;text-decoration:none}}");
        builder.AppendLine("</style></head><body><header><h1>Windows Doctor AI</h1><p>Relatório local de diagnóstico</p>");
        builder.Append("<p class=\"muted\">Execução <code>").Append(E(run.Id.ToString("D"))).Append("</code> · concluída em ").Append(E(run.CompletedAtUtc.ToString("u"))).Append(" · duração ").Append(E(run.Duration.ToString("g"))).AppendLine("</p></header>");

        builder.AppendLine("<section class=\"card\"><h2>Resumo executivo</h2>");
        if (report is null)
        {
            builder.AppendLine("<p>Esta execução histórica não contém resultados do Diagnostic Engine; não há Health Score ou evidências de scanner calculáveis.</p>");
        }
        else
        {
            builder.Append("<p class=\"score\">Health Score: ").Append(report.HealthScore is { } score ? E(score.Value.ToString()) : "Não calculado").AppendLine("</p>");
            builder.Append("<p>").Append(report.VerifiedChecks).Append(" verificações confirmadas; ").Append(report.CriticalProblems).Append(" problemas críticos; ").Append(report.Warnings).Append(" avisos; ").Append(report.UnavailableChecks).Append(" indisponíveis; ").Append(report.NotVerifiedChecks).AppendLine(" não verificadas.</p>");
            builder.AppendLine("<p class=\"muted\">O Health Score é heurístico e não mede saúde global. Estados indisponíveis ou não verificados não são contabilizados como saudáveis.</p>");
        }
        builder.AppendLine("</section>");

        builder.AppendLine("<section class=\"card\"><h2>Análise de correlações</h2><p>").Append(E(analysis.Summary)).AppendLine("</p>");
        foreach (var correlation in analysis.Correlations)
        {
            builder.Append("<article class=\"item\"><strong>").Append(E(correlation.SharedIdentifier)).Append("</strong> <span class=\"pill\">força de associação: ").Append(E(Label(correlation.AssociationStrength))).Append("</span><p>").Append(E(correlation.Explanation)).AppendLine("</p>");
            foreach (var evidence in correlation.Evidence)
                builder.Append("<p><strong>").Append(E(evidence.Category)).Append(" / ").Append(E(evidence.ScannerName)).Append(":</strong> ").Append(E(evidence.Title)).Append(" · ").Append(E(evidence.Evidence)).AppendLine("</p>");
            builder.AppendLine("</article>");
        }
        builder.AppendLine("</section>");

        builder.AppendLine("<section class=\"card\"><h2>Recomendações</h2>");
        if (knowledgeRuleCount == 0)
            builder.AppendLine("<p>A Knowledge Base está vazia: nenhum pacote foi importado. Nenhuma recomendação de conhecimento foi gerada.</p>");
        else if (recommendations.Count == 0)
            builder.AppendLine("<p>Nenhuma regra importada correspondeu aos achados desta execução; nenhuma recomendação de conhecimento foi gerada.</p>");
        foreach (var recommendation in recommendations)
        {
            builder.Append("<article class=\"item\"><h3>").Append(E(recommendation.Title)).Append("</h3><p>").Append(E(recommendation.Domain)).Append(" · impacto informado: ").Append(E(Label(recommendation.Impact))).Append(" · força do match literal: ").Append(E(Label(recommendation.Confidence))).AppendLine("</p>");
            builder.AppendLine("<p class=\"muted\">A força do match literal não é probabilidade de causa ou de sucesso; impacto e referências são declarações do pacote, não verificadas pelo aplicativo.</p>");
            builder.Append("<p>").Append(E(recommendation.Explanation)).Append(" ").Append(E(recommendation.ConfidenceExplanation)).AppendLine("</p>");
            AppendList(builder, "Causas descritas pela regra", recommendation.Causes);
            AppendList(builder, "Soluções descritas pela regra (não executadas)", recommendation.Solutions);
            builder.AppendLine("<p><strong>Evidências correspondentes</strong></p>");
            foreach (var evidence in recommendation.Evidence)
                builder.Append("<p>").Append(E(evidence.Category)).Append(" / ").Append(E(evidence.ScannerName)).Append(" · ").Append(E(evidence.Title)).Append(" · indicador <code>").Append(E(evidence.MatchedIndicator)).Append("</code> · ").Append(E(evidence.Evidence)).AppendLine("</p>");
            builder.AppendLine("<p><strong>Referências declaradas (não verificadas pelo aplicativo)</strong></p><ul>");
            foreach (var reference in recommendation.References)
            {
                builder.Append("<li>");
                if (Uri.TryCreate(reference.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                    builder.Append("<a rel=\"noreferrer noopener\" href=\"").Append(E(uri.AbsoluteUri)).Append("\">").Append(E(reference.Title)).Append("</a>");
                else builder.Append(E(reference.Title));
                builder.AppendLine("</li>");
            }
            builder.AppendLine("</ul></article>");
        }
        builder.AppendLine("</section>");

        builder.AppendLine("<section class=\"card\"><h2>Evidências diagnósticas</h2>");
        if (report is null || report.Results.Count == 0) builder.AppendLine("<p>Não há resultados disponíveis.</p>");
        else foreach (var result in report.Results)
        {
            var css = result.Severity == DiagnosticSeverity.Critical ? "critical" : result.Severity == DiagnosticSeverity.Warning ? "warning" : string.Empty;
            builder.Append("<article class=\"item ").Append(css).Append("\"><h3>").Append(E(result.Title)).Append("</h3><p>").Append(E(result.Category)).Append(" / ").Append(E(result.ScannerName)).Append(" · ").Append(E(Label(result.Status))).Append(" · ").Append(E(Label(result.Severity))).AppendLine("</p>");
            builder.Append("<p>").Append(E(result.Description)).AppendLine("</p><p><strong>Evidência:</strong> ");
            builder.Append(E(result.Evidence)).AppendLine("</p><p class=\"muted\">Recomendação do scanner: ");
            builder.Append(E(result.Recommendation)).Append(" · ").Append(E(result.Timestamp.ToString("u"))).AppendLine("</p></article>");
        }
        builder.AppendLine("</section>");

        builder.AppendLine("<section class=\"card\"><h2>Histórico de propostas de reparo</h2>");
        if (repairHistory.Count == 0) builder.AppendLine("<p>Nenhuma proposta de reparo foi registrada.</p>");
        else foreach (var item in repairHistory)
            builder.Append("<article class=\"item\"><strong>").Append(E(item.Title)).Append("</strong> · ").Append(E(Label(item.Status))).Append(" · risco informado: ").Append(E(Label(item.Risk))).Append(" · confirmação: ").Append(item.UserConfirmed ? "sim" : "não").Append(" · rollback anunciado: ").Append(item.RollbackSupported ? "sim" : "não").Append("<p>").Append(E(item.Details)).Append("</p><p class=\"muted\">").Append(E(item.CompletedAtUtc.ToString("u"))).AppendLine("</p></article>");
        builder.AppendLine("</section><footer>Relatório gerado localmente. Pacotes e fontes declaradas não têm autoria autenticada pelo aplicativo; regras, referências, causas, soluções e impacto são dados declarados. A força de match descreve correspondência textual, não probabilidade de causa ou sucesso. Correlações não determinam causa. Nenhum reparo é executado por este relatório.</footer></body></html>");
        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.Append("<p><strong>").Append(E(title)).AppendLine("</strong></p><ul>");
        foreach (var value in values) builder.Append("<li>").Append(E(value)).AppendLine("</li>");
        builder.AppendLine("</ul>");
    }

    private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Label(Enum value) => E(value.ToString());
}
