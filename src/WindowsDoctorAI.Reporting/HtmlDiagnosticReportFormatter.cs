using System.Net;
using System.Text;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Reporting;

/// <summary>Gera HTML local; minimização de privacidade ocorre antes do encoding e não carrega recursos remotos.</summary>
public sealed class HtmlDiagnosticReportFormatter
{
    public string Format(
        DiagnosticRun run,
        IReadOnlyList<DiagnosticRecommendation> recommendations,
        RootCauseAnalysis analysis,
        int knowledgeRuleCount = -1) =>
        FormatCore(run, recommendations, analysis, knowledgeRuleCount, null);

    public string Format(DiagnosticRun run, ManualGuidanceAssessment manualGuidance, RootCauseAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(manualGuidance);
        return FormatCore(run, Array.Empty<DiagnosticRecommendation>(), analysis, -1, manualGuidance);
    }

    private static string FormatCore(
        DiagnosticRun run,
        IReadOnlyList<DiagnosticRecommendation> recommendations,
        RootCauseAnalysis analysis,
        int knowledgeRuleCount,
        ManualGuidanceAssessment? manualGuidance)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(recommendations);
        ArgumentNullException.ThrowIfNull(analysis);
        var sourceInventory = run.Inventory;
        run = DiagnosticPrivacyRedactor.Redact(run);
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

        builder.AppendLine("<section class=\"card\"><h2>Análise de correlações</h2><p>").Append(S(analysis.Summary, sourceInventory)).AppendLine("</p>");
        foreach (var correlation in analysis.Correlations)
        {
            builder.Append("<article class=\"item\"><strong>").Append(S(correlation.SharedIdentifier, sourceInventory)).Append("</strong> <span class=\"pill\">força de associação: ").Append(E(Label(correlation.AssociationStrength))).Append("</span><p>").Append(S(correlation.Explanation, sourceInventory)).AppendLine("</p>");
            foreach (var evidence in correlation.Evidence)
                builder.Append("<p><strong>").Append(S(evidence.Category, sourceInventory)).Append(" / ").Append(S(evidence.ScannerName, sourceInventory)).Append(":</strong> ").Append(S(evidence.Title, sourceInventory)).Append(" · ").Append(S(evidence.Evidence, sourceInventory)).AppendLine("</p>");
            builder.AppendLine("</article>");
        }
        builder.AppendLine("</section>");

        builder.AppendLine(manualGuidance is null
            ? "<section class=\"card\"><h2>Recomendações</h2>"
            : "<section class=\"card\"><h2>Orientações manuais por achado</h2>");
        if (manualGuidance is not null)
            AppendManualGuidance(builder, manualGuidance, sourceInventory);
        else if (knowledgeRuleCount == 0)
            builder.AppendLine("<p>A base de conhecimento está vazia: nenhuma regra está disponível. Isso não permite concluir que um problema inexiste ou que o computador está saudável.</p>");
        else if (recommendations.Count == 0)
            builder.AppendLine("<p>Nenhuma orientação pré-avaliada foi fornecida a este formatter; esse vazio não demonstra ausência de correspondência nem de problema.</p>");
        else foreach (var recommendation in recommendations)
        {
            builder.Append("<article class=\"item\"><h3>").Append(S(recommendation.Title, sourceInventory)).Append("</h3><p>").Append(S(recommendation.Domain, sourceInventory)).Append(" · impacto informado: ").Append(E(Label(recommendation.Impact))).Append(" · força do match literal: ").Append(E(Label(recommendation.Confidence))).AppendLine("</p>");
            builder.AppendLine("<p class=\"muted\">A força do match literal não é probabilidade de causa ou de sucesso; impacto e referências são declarações do pacote, não verificadas pelo aplicativo.</p>");
            builder.Append("<p>").Append(S(recommendation.Explanation, sourceInventory)).Append(" ").Append(S(recommendation.ConfidenceExplanation, sourceInventory)).AppendLine("</p>");
            if (recommendation.OsTarget is not null)
                builder.AppendLine("<p><strong>Alvo OS+build estruturado verificado:</strong> o inventário local conhecido passou pelo alvo declarado desta regra. Isso verifica elegibilidade da regra, não estabelece causa para o achado.</p>");
            else
                builder.AppendLine("<p><strong>Regra legada — aplicabilidade não verificada automaticamente:</strong> o texto de aplicabilidade é explicativo e não foi usado como filtro.</p>");
            if (!string.IsNullOrWhiteSpace(recommendation.Applicability))
                builder.Append("<p><strong>Aplicabilidade declarada (texto explicativo; não usada como filtro):</strong> ").Append(S(recommendation.Applicability, sourceInventory)).AppendLine("</p>");
            AppendList(builder, "Causas descritas pela regra", recommendation.Causes, sourceInventory);
            AppendList(builder, "Soluções descritas pela regra (não executadas)", recommendation.Solutions, sourceInventory);
            if (recommendation.RequiredEvidence is { Count: > 0 })
                AppendList(builder, "Evidência necessária para aplicar a regra", recommendation.RequiredEvidence, sourceInventory);
            if (recommendation.Procedure is { } procedure)
            {
                builder.Append("<p><strong>Ação diagnóstica descrita (não executada):</strong> ").Append(S(procedure.DiagnosticAction, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Ação corretiva manual (não executada):</strong> ").Append(S(procedure.CorrectiveAction, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Privilégio:</strong> ").Append(S(procedure.RequiredPrivilege, sourceInventory))
                    .Append(" · elevação necessária: ").Append(procedure.RequiresElevation ? "sim" : "não")
                    .Append(" · ação modificadora: ").Append(procedure.IsModifying ? "sim" : "não")
                    .Append(" · confirmação explícita: ").Append(procedure.RequiresUserConfirmation ? "sim" : "não")
                    .Append(" · somente manual: ").Append(procedure.ManualOnly ? "sim" : "não").AppendLine("</p>");
                builder.Append("<p><strong>Risco declarado:</strong> ").Append(S(procedure.Risk, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Backup:</strong> ").Append(S(procedure.Backup, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Rollback:</strong> ").Append(S(procedure.Rollback, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Limitação da fonte:</strong> ").Append(S(procedure.SourceLimitation, sourceInventory)).AppendLine("</p>");
            }
            builder.AppendLine("<p><strong>Evidências correspondentes</strong></p>");
            foreach (var evidence in recommendation.Evidence)
                builder.Append("<p>").Append(S(evidence.Category, sourceInventory)).Append(" / ").Append(S(evidence.ScannerName, sourceInventory)).Append(" · ").Append(S(evidence.Title, sourceInventory)).Append(" · indicador <code>").Append(S(evidence.MatchedIndicator, sourceInventory)).Append("</code> · ").Append(S(evidence.Evidence, sourceInventory)).AppendLine("</p>");
            builder.AppendLine("<p><strong>Referências declaradas (não verificadas pelo aplicativo)</strong></p><ul>");
            foreach (var reference in recommendation.References)
            {
                builder.Append("<li>");
                var safeUrl = DiagnosticPrivacyRedactor.RedactText(reference.Url, sourceInventory);
                if (Uri.TryCreate(safeUrl, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps &&
                    !string.IsNullOrWhiteSpace(uri.Host) &&
                    string.IsNullOrEmpty(uri.UserInfo))
                    builder.Append("<a rel=\"noreferrer noopener\" href=\"").Append(E(uri.AbsoluteUri)).Append("\">").Append(S(reference.Title, sourceInventory)).Append("</a>");
                else builder.Append(S(reference.Title, sourceInventory));
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
            builder.Append("<article class=\"item ").Append(css).Append("\"><h3>").Append(S(result.Title, sourceInventory)).Append("</h3><p>").Append(S(result.Category, sourceInventory)).Append(" / ").Append(S(result.ScannerName, sourceInventory)).Append(" · ").Append(E(Label(result.Status))).Append(" · ").Append(E(Label(result.Severity))).AppendLine("</p>");
            var provider = DiagnosticSourceMetadata.NormalizeProvider(result.SourceMetadata?.Provider);
            if (provider is not null)
                builder.Append("<p><strong>Fonte estruturada:</strong> ").Append(E(provider)).AppendLine("</p>");
            builder.Append("<p>").Append(S(result.Description, sourceInventory)).AppendLine("</p><p><strong>Evidência:</strong> ");
            builder.Append(S(result.Evidence, sourceInventory)).AppendLine("</p><p class=\"muted\">Recomendação do scanner: ");
            builder.Append(S(result.Recommendation, sourceInventory)).Append(" · ").Append(E(result.Timestamp.ToString("u"))).AppendLine("</p></article>");
        }
        builder.AppendLine("</section>");
        builder.AppendLine("<footer>Relatório gerado localmente. Pacotes e fontes declaradas não têm autoria autenticada pelo aplicativo; regras, referências, causas, soluções e impacto são dados declarados. A força de match descreve correspondência textual, não probabilidade de causa ou sucesso. Correlações não determinam causa. Nenhum reparo é executado por este relatório. O histórico de reparos permanece no SQLite e não é agregado a este relatório.</footer></body></html>");
        return builder.ToString();
    }

    private static void AppendManualGuidance(
        StringBuilder builder,
        ManualGuidanceAssessment assessment,
        ComputerInventory sourceInventory)
    {
        builder.Append("<p class=\"muted\"><strong>Estado:</strong> ").Append(S(assessment.StatusText, sourceInventory)).AppendLine("</p>");
        if (assessment.Findings.Count == 0)
        {
            builder.AppendLine("<p>Nenhum cartão individual foi projetado. Isso não constitui uma avaliação positiva da condição do computador.</p>");
            return;
        }

        foreach (var finding in assessment.Findings)
        {
            builder.AppendLine("<article class=\"item\">");
            builder.Append("<p><strong>").Append(S(finding.RunReferenceText, sourceInventory)).AppendLine("</strong></p>");
            builder.Append("<p><strong>").Append(S(finding.FindingIdentityText, sourceInventory)).AppendLine("</strong></p>");
            builder.Append("<h3>").Append(S(finding.FindingHeading, sourceInventory)).AppendLine("</h3>");
            if (finding.IsAmbiguousDuplicate)
                builder.AppendLine("<p><span class=\"pill\">Ambíguo / duplicado · não acionável</span></p>");
            builder.Append("<p><span class=\"pill\">").Append(S(finding.StatusText, sourceInventory)).AppendLine("</span></p>");
            builder.Append("<p>").Append(S(finding.EvidenceText, sourceInventory)).AppendLine("</p>");
            builder.Append("<p>").Append(S(finding.ProviderText, sourceInventory)).AppendLine("</p>");

            foreach (var recommendation in finding.Recommendations)
            {
                builder.Append("<section class=\"item\"><h4>").Append(S(recommendation.ManualOnlyStatusText, sourceInventory)).AppendLine("</h4>");
                builder.Append("<p><strong>").Append(S(recommendation.Title, sourceInventory)).Append("</strong> · ").Append(S(recommendation.Domain, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Regra/versão:</strong> ").Append(S(recommendation.RuleIdentityText, sourceInventory)).AppendLine("</p>");
                builder.Append("<p>").Append(S(recommendation.MatchStrengthText, sourceInventory)).Append(" ").Append(S(recommendation.MatchExplanation, sourceInventory)).AppendLine("</p>");
                builder.Append("<p>").Append(S(recommendation.Explanation, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Aplicabilidade:</strong> ").Append(S(recommendation.ApplicabilityText, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Aplicabilidade declarada:</strong> ").Append(S(recommendation.DeclaredApplicability, sourceInventory)).AppendLine("</p>");
                builder.Append("<p>").Append(S(recommendation.DeclaredPackageSourceText, sourceInventory)).AppendLine("</p>");
                builder.Append("<p>").Append(S(recommendation.PackageVersionText, sourceInventory)).AppendLine("</p>");
                builder.Append("<p>").Append(S(recommendation.PackageSha256Text, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Ação diagnóstica (não executada):</strong> ").Append(S(recommendation.DiagnosticAction, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Orientação corretiva (não executada):</strong> ").Append(S(recommendation.CorrectiveAction, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>").Append(S(recommendation.SolutionsDisclosureText, sourceInventory)).AppendLine("</strong></p>");
                if (recommendation.Solutions.Count > 0)
                    AppendList(builder, "Soluções", recommendation.Solutions, sourceInventory);
                builder.Append("<p><strong>").Append(S(recommendation.ReferencesDisclosureText, sourceInventory)).AppendLine("</strong></p>");
                AppendManualReferences(builder, recommendation.References, sourceInventory);
                builder.Append("<p><strong>Risco declarado:</strong> ").Append(S(recommendation.Risk, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Privilégio requerido:</strong> ").Append(S(recommendation.RequiredPrivilege, sourceInventory)).AppendLine("</p>");
                builder.Append("<p>").Append(S(recommendation.ElevationText, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Backup declarado:</strong> ").Append(S(recommendation.Backup, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Rollback declarado:</strong> ").Append(S(recommendation.Rollback, sourceInventory)).AppendLine("</p>");
                builder.Append("<p><strong>Limitação da fonte:</strong> ").Append(S(recommendation.SourceLimitation, sourceInventory)).AppendLine("</p></section>");
            }

            if (finding.IncompleteCandidates.Count > 0)
            {
                builder.AppendLine("<h4>Candidatos com evidência/aplicabilidade incompleta — sem match confirmado</h4>");
                foreach (var candidate in finding.IncompleteCandidates)
                {
                    builder.Append("<div class=\"item\"><p><strong>").Append(S(candidate.RuleIdentityText, sourceInventory)).Append(" · ").Append(S(candidate.Title, sourceInventory)).AppendLine("</strong></p>");
                    builder.Append("<p>").Append(S(candidate.MatchStrengthText, sourceInventory)).AppendLine("</p>");
                    builder.Append("<p>").Append(S(candidate.ApplicabilityText, sourceInventory)).AppendLine("</p>");
                    builder.Append("<p>").Append(S(candidate.Reason, sourceInventory)).AppendLine("</p>");
                    builder.Append("<p>").Append(S(candidate.DeclaredPackageSourceText, sourceInventory)).AppendLine("</p>");
                    builder.Append("<p>").Append(S(candidate.PackageVersionText, sourceInventory)).AppendLine("</p>");
                    builder.Append("<p>").Append(S(candidate.PackageSha256Text, sourceInventory)).AppendLine("</p></div>");
                }
            }

            builder.AppendLine("</article>");
        }
    }

    private static void AppendManualReferences(
        StringBuilder builder,
        IReadOnlyList<ManualGuidanceReference> references,
        ComputerInventory sourceInventory)
    {
        if (references.Count == 0)
        {
            builder.AppendLine("<p>Nenhuma referência HTTPS declarada foi exibida.</p>");
            return;
        }

        builder.AppendLine("<ul>");
        foreach (var reference in references)
        {
            var safeUrl = DiagnosticPrivacyRedactor.RedactText(reference.HttpsUrl, sourceInventory);
            builder.Append("<li>");
            if (Uri.TryCreate(safeUrl, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                !string.IsNullOrWhiteSpace(uri.Host) &&
                string.IsNullOrEmpty(uri.UserInfo))
            {
                builder.Append("<a rel=\"noreferrer noopener\" href=\"").Append(E(uri.AbsoluteUri)).Append("\">").Append(S(reference.Title, sourceInventory)).Append("</a>");
            }
            else
            {
                builder.Append(S(reference.Title, sourceInventory));
            }

            builder.AppendLine("</li>");
        }

        builder.AppendLine("</ul>");
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values, ComputerInventory sourceInventory)
    {
        if (values.Count == 0) return;
        builder.Append("<p><strong>").Append(E(title)).AppendLine("</strong></p><ul>");
        foreach (var value in values) builder.Append("<li>").Append(S(value, sourceInventory)).AppendLine("</li>");
        builder.AppendLine("</ul>");
    }

    private static string S(string value, ComputerInventory sourceInventory)
    {
        var redacted = DiagnosticPrivacyRedactor.RedactText(value, sourceInventory);
        return E(redacted);
    }

    /// <summary>HTML encoding é uma etapa distinta da redação de privacidade.</summary>
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Label(Enum value) => value.ToString();
}
