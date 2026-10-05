using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

/// <summary>Resultado observacional por achado. Candidatos incompletos nunca são apresentados como match confirmado.</summary>
public sealed record ManualRecommendationFindingAssessment(
    DiagnosticResult Finding,
    IReadOnlyList<ManualRecommendationRuleMatch> Matches,
    IReadOnlyList<ManualRecommendationIncompleteCandidate> IncompleteCandidates);

public sealed record ManualRecommendationRuleMatch(KnowledgeRule Rule, DiagnosticRecommendation Recommendation);

public sealed record ManualRecommendationIncompleteCandidate(
    KnowledgeRule Rule,
    string Reason,
    bool StructuredApplicabilityVerified);

/// <summary>Projeta somente conteúdo redigido e ManualOnly para exibição; não grava achados, candidatos ou evidência.</summary>
public static class ManualGuidanceProjector
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");

    public static ManualGuidanceAssessment Project(
        DiagnosticRun run,
        IReadOnlyList<KnowledgeRule> rules,
        IReadOnlyList<KnowledgeRuleProvenance> provenance,
        IReadOnlyList<ManualRecommendationFindingAssessment> findings)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(findings);

        var redactionInventory = run.Inventory;
        var safeRun = DiagnosticPrivacyRedactor.Redact(run);
        var packageByRule = provenance.ToDictionary(
            item => (item.RuleId, item.RuleVersion),
            item => item,
            RuleProvenanceKeyComparer.Instance);
        var runReference = FormatRunReference(safeRun);
        var safeFindings = findings.Select(finding =>
        {
            var safeFinding = RedactFinding(finding.Finding, redactionInventory);
            return (Assessment: finding, Finding: safeFinding, Identity: BuildFindingIdentity(run.Id, safeFinding));
        }).ToArray();
        var duplicateIdentities = safeFindings
            .GroupBy(item => item.Identity, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var projectedFindings = safeFindings.Select(item => ProjectFinding(
            item.Assessment,
            item.Finding,
            item.Identity,
            duplicateIdentities.Contains(item.Identity),
            runReference,
            rules.Count == 0,
            redactionInventory,
            packageByRule)).ToArray();
        var knowledgeBaseIsEmpty = rules.Count == 0;

        if (safeRun.Report is null)
        {
            return new ManualGuidanceAssessment(
                ManualGuidanceAssessmentStatus.NoDiagnosticReport,
                "Avaliação incompleta: esta execução não contém resultados diagnósticos estruturados. Não é possível concluir match nem fazer uma avaliação da condição do computador.",
                knowledgeBaseIsEmpty,
                Array.Empty<ManualGuidanceFinding>());
        }

        if (projectedFindings.Length == 0)
        {
            var emptyBaseNote = knowledgeBaseIsEmpty
                ? " A base de conhecimento também está vazia."
                : string.Empty;
            return new ManualGuidanceAssessment(
                ManualGuidanceAssessmentStatus.NoFindings,
                "Nenhum achado foi produzido nesta execução; isso não exclui problemas nem constitui uma avaliação positiva da condição do computador." + emptyBaseNote,
                knowledgeBaseIsEmpty,
                projectedFindings);
        }

        var ambiguousCount = projectedFindings.Count(item => item.IsAmbiguousDuplicate);
        if (ambiguousCount > 0)
        {
            var incompleteCount = projectedFindings.Sum(item => item.IncompleteCandidates.Count);
            var notes = new List<string>();
            if (incompleteCount > 0) notes.Add($"{incompleteCount} candidato(s) também têm evidência/aplicabilidade incompleta.");
            if (knowledgeBaseIsEmpty) notes.Add("A base de conhecimento está vazia.");
            var suffix = notes.Count == 0 ? string.Empty : " " + string.Join(" ", notes);
            return new ManualGuidanceAssessment(
                ManualGuidanceAssessmentStatus.AmbiguousFindings,
                $"Identidade ambígua: {ambiguousCount} achado(s) são indistinguíveis após redação. Orientações associadas às duplicatas não são acionáveis.{suffix}",
                knowledgeBaseIsEmpty,
                projectedFindings);
        }

        if (knowledgeBaseIsEmpty)
        {
            return new ManualGuidanceAssessment(
                ManualGuidanceAssessmentStatus.KnowledgeBaseEmpty,
                "Base de conhecimento vazia: não há regras ManualOnly para avaliar os achados; a ausência de regras não permite concluir que um problema inexiste.",
                true,
                projectedFindings);
        }

        if (projectedFindings.Any(item => item.IncompleteCandidates.Count > 0))
        {
            var matchedCount = projectedFindings.Sum(item => item.Recommendations.Count);
            var incompleteCount = projectedFindings.Sum(item => item.IncompleteCandidates.Count);
            var matchedNote = matchedCount > 0 ? $" {matchedCount} orientação(ões) completa(s) continuam listadas para revisão humana." : string.Empty;
            return new ManualGuidanceAssessment(
                ManualGuidanceAssessmentStatus.Incomplete,
                $"Avaliação incompleta: {incompleteCount} candidato(s) exigem evidência adicional ou aplicabilidade verificável; nenhuma conclusão sobre a condição do sistema é possível.{matchedNote}",
                false,
                projectedFindings);
        }

        var recommendationCount = projectedFindings.Sum(item => item.Recommendations.Count);
        if (recommendationCount == 0)
        {
            return new ManualGuidanceAssessment(
                ManualGuidanceAssessmentStatus.NoMatchingRule,
                "Nenhuma regra ManualOnly correspondeu aos achados avaliados. Um resultado sem match não prova que um problema inexiste.",
                false,
                projectedFindings);
        }

        return new ManualGuidanceAssessment(
            ManualGuidanceAssessmentStatus.GuidanceAvailable,
            $"{recommendationCount} orientação(ões) ManualOnly exibida(s) por achado. Revise cada item manualmente; nenhuma ação foi executada.",
            false,
            projectedFindings);
    }

    private static ManualGuidanceFinding ProjectFinding(
        ManualRecommendationFindingAssessment finding,
        DiagnosticResult safeFinding,
        string findingIdentity,
        bool isAmbiguousDuplicate,
        string runReference,
        bool knowledgeBaseIsEmpty,
        ComputerInventory inventory,
        IReadOnlyDictionary<(string RuleId, int RuleVersion), KnowledgeRuleProvenance> packageByRule)
    {
        var provider = DiagnosticSourceMetadata.NormalizeProvider(safeFinding.SourceMetadata?.Provider);
        var candidates = finding.Matches.Select(match => ProjectRecommendation(match, inventory, packageByRule, isAmbiguousDuplicate)).ToArray();
        var incomplete = finding.IncompleteCandidates.Select(candidate => ProjectIncompleteCandidate(candidate, inventory, packageByRule)).ToArray();
        var status = isAmbiguousDuplicate
            ? ManualGuidanceFindingStatus.AmbiguousDuplicate
            : knowledgeBaseIsEmpty
            ? ManualGuidanceFindingStatus.KnowledgeBaseEmpty
            : incomplete.Length > 0
                ? ManualGuidanceFindingStatus.Incomplete
                : candidates.Length > 1
                ? ManualGuidanceFindingStatus.MultipleCandidates
                : candidates.Length == 1
                    ? ManualGuidanceFindingStatus.SingleCandidate
                    : ManualGuidanceFindingStatus.NoMatch;
        var statusText = status switch
        {
            ManualGuidanceFindingStatus.AmbiguousDuplicate => "Achado indistinguível de outra saída após redação. A identidade é ambígua e as orientações associadas não são acionáveis.",
            ManualGuidanceFindingStatus.KnowledgeBaseEmpty => "Não avaliado: a base de conhecimento está vazia.",
            ManualGuidanceFindingStatus.MultipleCandidates => $"{candidates.Length} regras candidatas; todas estão listadas para comparação humana, sem escolha automática.",
            ManualGuidanceFindingStatus.SingleCandidate => "Uma regra candidata ManualOnly; match observacional, não causal.",
            ManualGuidanceFindingStatus.Incomplete => candidates.Length > 0
                ? $"Evidência insuficiente/incompleta para {incomplete.Length} candidato(s); orientações completas continuam listadas separadamente."
                : "Evidência insuficiente/incompleta: não foi possível confirmar os requisitos de um ou mais candidatos.",
            _ => "Nenhuma regra ManualOnly correspondeu a este achado; isso não prova que um problema inexiste."
        };
        var findingHeading = $"{Safe(safeFinding.Title, inventory)} · scanner {Safe(safeFinding.ScannerName, inventory)} · categoria {Safe(safeFinding.Category, inventory)}";
        var evidence = string.IsNullOrWhiteSpace(safeFinding.Evidence)
            ? "Evidência redigida: indisponível neste resultado."
            : "Evidência redigida: " + Safe(safeFinding.Evidence, inventory);
        var providerText = provider is null
            ? "Provider estruturado: ausente ou não reconhecido; nenhum valor livre foi exibido."
            : $"Provider estruturado (allowlist): {provider}";
        return new ManualGuidanceFinding(
            runReference,
            $"Identidade estável nesta execução: {findingIdentity}",
            findingHeading,
            isAmbiguousDuplicate,
            evidence,
            providerText,
            status,
            statusText,
            candidates,
            incomplete);
    }

    private static ManualGuidanceRecommendation ProjectRecommendation(
        ManualRecommendationRuleMatch match,
        ComputerInventory inventory,
        IReadOnlyDictionary<(string RuleId, int RuleVersion), KnowledgeRuleProvenance> packageByRule,
        bool isAmbiguousDuplicate)
    {
        var rule = match.Rule;
        var procedure = rule.Procedure!;
        packageByRule.TryGetValue((rule.Id, rule.Version), out var provenance);
        var applicability = rule.OsTarget is null
            ? "Aplicabilidade legada não verificada automaticamente; não confirma elegibilidade ou causalidade."
            : "Aplicabilidade estruturada verificada contra a família Windows e o build do inventário local; isso verifica apenas elegibilidade. ";
        var sourceText = rule.OsTarget is null && string.IsNullOrWhiteSpace(rule.Applicability)
            ? "Não declarada"
            : Safe(rule.Applicability ?? "", inventory);
        var confidence = match.Recommendation.Confidence switch
        {
            MatchConfidence.High => "alta",
            MatchConfidence.Moderate => "moderada",
            MatchConfidence.Low => "baixa",
            _ => "indeterminada"
        };
        var solutions = isAmbiguousDuplicate
            ? Array.Empty<string>()
            : (rule.Solutions ?? Array.Empty<string>()).Select(solution => Safe(solution, inventory)).ToArray();
        var references = ProjectReferences(rule.References, inventory);
        return new ManualGuidanceRecommendation(
            isAmbiguousDuplicate
                ? "Ambíguo / não acionável por duplicidade · ManualOnly / não executada"
                : "ManualOnly / não executada",
            $"{Safe(rule.Id, inventory)} · v{rule.Version}",
            Safe(rule.Title, inventory),
            Safe(rule.Domain, inventory),
            $"Força do match: {confidence}. Não é probabilidade causal nem previsão de eficácia.",
            Safe(match.Recommendation.ConfidenceExplanation, inventory),
            Safe(match.Recommendation.Explanation, inventory),
            applicability,
            sourceText,
            PackageSourceText(provenance, inventory),
            PackageVersionText(provenance, inventory),
            PackageHashText(provenance, inventory),
            isAmbiguousDuplicate
                ? "Ação diagnóstica não exibida: a identidade do achado é ambígua e nenhuma instrução pode ser associada com segurança a esta ocorrência."
                : Safe(procedure.DiagnosticAction, inventory),
            isAmbiguousDuplicate
                ? "Não acionável: achados indistinguíveis após redação; não associe esta orientação a uma ocorrência individual."
                : Safe(procedure.CorrectiveAction, inventory),
            isAmbiguousDuplicate
                ? "Soluções não exibidas: a duplicidade torna a associação ao achado ambígua e não acionável."
                : solutions.Length == 0
                    ? "Nenhuma solução foi declarada pela regra."
                    : "Soluções declaradas pela regra (não executadas).",
            solutions,
            isAmbiguousDuplicate
                ? "Referências declaradas em HTTPS; autenticidade não verificada e associação ao achado ambígua."
                : "Referências declaradas em HTTPS; autenticidade não verificada.",
            references,
            Safe(procedure.Risk, inventory),
            Safe(procedure.RequiredPrivilege, inventory),
            procedure.RequiresElevation
                ? "Elevação declarada pela regra: necessária. Nenhuma elevação foi solicitada ou executada."
                : "Elevação declarada pela regra: não necessária. Nenhuma elevação será usada nesta orientação.",
            Safe(procedure.Backup, inventory),
            Safe(procedure.Rollback, inventory),
            Safe(procedure.SourceLimitation, inventory));
    }

    private static ManualGuidanceIncompleteCandidate ProjectIncompleteCandidate(
        ManualRecommendationIncompleteCandidate candidate,
        ComputerInventory inventory,
        IReadOnlyDictionary<(string RuleId, int RuleVersion), KnowledgeRuleProvenance> packageByRule)
    {
        packageByRule.TryGetValue((candidate.Rule.Id, candidate.Rule.Version), out var provenance);
        var applicability = candidate.StructuredApplicabilityVerified
            ? "Aplicabilidade estruturada verificada para este inventário; isso verifica apenas elegibilidade, não causa."
            : candidate.Rule.OsTarget is null
                ? "Aplicabilidade legada não verificada automaticamente; não confirma elegibilidade ou causalidade."
                : "Aplicabilidade estruturada não verificada devido a dados de inventário ausentes ou inválidos.";
        return new ManualGuidanceIncompleteCandidate(
            $"{Safe(candidate.Rule.Id, inventory)} · v{candidate.Rule.Version}",
            Safe(candidate.Rule.Title, inventory),
            "Força do match: não confirmada enquanto os requisitos permanecerem incompletos; não é probabilidade causal.",
            applicability,
            Safe(candidate.Reason, inventory),
            PackageSourceText(provenance, inventory),
            PackageVersionText(provenance, inventory),
            PackageHashText(provenance, inventory));
    }

    private static string PackageSourceText(KnowledgeRuleProvenance? provenance, ComputerInventory inventory) =>
        $"Fonte do pacote declarada, não autenticada: {(provenance is null ? "não disponível" : Safe(provenance.DeclaredSource, inventory))}";

    private static string PackageVersionText(KnowledgeRuleProvenance? provenance, ComputerInventory inventory) =>
        $"Versão do pacote declarada, não autenticada: {(provenance is null ? "não disponível" : Safe(provenance.PackageVersion, inventory))}";

    private static string PackageHashText(KnowledgeRuleProvenance? provenance, ComputerInventory inventory) =>
        $"SHA-256 declarado do pacote (não autentica autoria nem conteúdo): {(provenance is null ? "não disponível" : Safe(provenance.Sha256, inventory))}";

    private static IReadOnlyList<ManualGuidanceReference> ProjectReferences(
        IReadOnlyList<KnowledgeReference>? references,
        ComputerInventory inventory)
    {
        var projected = new List<ManualGuidanceReference>();
        foreach (var reference in references ?? Array.Empty<KnowledgeReference>())
        {
            var safeUrl = DiagnosticPrivacyRedactor.RedactText(reference.Url, inventory);
            if (!Uri.TryCreate(safeUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                continue;
            }

            projected.Add(new ManualGuidanceReference(Safe(reference.Title, inventory), uri.AbsoluteUri));
        }

        return projected;
    }

    private static DiagnosticResult RedactFinding(DiagnosticResult finding, ComputerInventory inventory)
    {
        var timestamp = finding.Timestamp;
        var report = new DiagnosticReport([finding], timestamp, timestamp, TimeSpan.Zero, null);
        return DiagnosticPrivacyRedactor.RedactReport(report, inventory)!.Results[0];
    }

    private static string BuildFindingIdentity(Guid runId, DiagnosticResult finding)
    {
        var canonicalFinding = JsonSerializer.Serialize(new
        {
            RunId = runId.ToString("N"),
            TimestampUtcTicks = finding.Timestamp.ToUniversalTime().UtcDateTime.Ticks,
            DurationTicks = finding.Duration.Ticks,
            Scanner = finding.ScannerName,
            Category = finding.Category,
            Status = finding.Status.ToString(),
            Severity = finding.Severity.ToString(),
            Title = finding.Title,
            Description = finding.Description,
            Evidence = finding.Evidence,
            Recommendation = finding.Recommendation,
            Provider = DiagnosticSourceMetadata.NormalizeProvider(finding.SourceMetadata?.Provider)
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalFinding))).ToLowerInvariant();
        return $"f1-{hash[..24]}";
    }

    private static string FormatRunReference(DiagnosticRun run) =>
        $"Execução {run.Id.ToString("N", CultureInfo.InvariantCulture)[..12]} · concluída em {run.CompletedAtUtc.ToUniversalTime().ToString("dd/MM/yyyy HH:mm:ss 'UTC'", BrazilianCulture)}";

    private static string Safe(string? value, ComputerInventory inventory) =>
        DiagnosticPrivacyRedactor.RedactText(value, inventory);

    private sealed class RuleProvenanceKeyComparer : IEqualityComparer<(string RuleId, int RuleVersion)>
    {
        public static RuleProvenanceKeyComparer Instance { get; } = new();

        public bool Equals((string RuleId, int RuleVersion) x, (string RuleId, int RuleVersion) y) =>
            x.RuleVersion == y.RuleVersion && string.Equals(x.RuleId, y.RuleId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string RuleId, int RuleVersion) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RuleId), obj.RuleVersion);
    }
}
