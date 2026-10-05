using System.Globalization;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

public enum ManualGuidanceAssessmentStatus
{
    NoDiagnosticReport,
    NoFindings,
    KnowledgeBaseEmpty,
    NoMatchingRule,
    GuidanceAvailable,
    Incomplete
}

public enum ManualGuidanceFindingStatus
{
    NoMatch,
    SingleCandidate,
    MultipleCandidates,
    Incomplete,
    KnowledgeBaseEmpty
}

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

/// <summary>Modelo temporário de apresentação da Home; nenhum destes dados é persistido pelo projetor.</summary>
public sealed record ManualGuidanceAssessment(
    ManualGuidanceAssessmentStatus Status,
    string StatusText,
    bool KnowledgeBaseIsEmpty,
    IReadOnlyList<ManualGuidanceFinding> Findings);

public sealed record ManualGuidanceFinding(
    string RunReferenceText,
    string FindingHeading,
    string EvidenceText,
    string ProviderText,
    ManualGuidanceFindingStatus Status,
    string StatusText,
    IReadOnlyList<ManualGuidanceRecommendation> Recommendations,
    IReadOnlyList<ManualGuidanceIncompleteCandidate> IncompleteCandidates);

public sealed record ManualGuidanceRecommendation(
    string ManualOnlyStatusText,
    string RuleIdentityText,
    string Title,
    string Domain,
    string MatchStrengthText,
    string MatchExplanation,
    string Explanation,
    string ApplicabilityText,
    string DeclaredApplicability,
    string DeclaredPackageSourceText,
    string PackageVersionText,
    string PackageSha256Text,
    string DiagnosticAction,
    string CorrectiveAction,
    string Risk,
    string RequiredPrivilege,
    string ElevationText,
    string Backup,
    string Rollback,
    string SourceLimitation);

public sealed record ManualGuidanceIncompleteCandidate(
    string RuleIdentityText,
    string Title,
    string MatchStrengthText,
    string ApplicabilityText,
    string Reason,
    string DeclaredPackageSourceText,
    string PackageVersionText,
    string PackageSha256Text);

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
        var projectedFindings = findings.Select((finding, index) => ProjectFinding(
            finding,
            index + 1,
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
        int index,
        string runReference,
        bool knowledgeBaseIsEmpty,
        ComputerInventory inventory,
        IReadOnlyDictionary<(string RuleId, int RuleVersion), KnowledgeRuleProvenance> packageByRule)
    {
        var safeFinding = DiagnosticPrivacyRedactor.RedactReport(
            new DiagnosticReport([finding.Finding], finding.Finding.Timestamp, finding.Finding.Timestamp, TimeSpan.Zero, null), inventory)!.Results[0];
        var provider = DiagnosticSourceMetadata.NormalizeProvider(safeFinding.SourceMetadata?.Provider);
        var candidates = finding.Matches.Select(match => ProjectRecommendation(match, inventory, packageByRule)).ToArray();
        var incomplete = finding.IncompleteCandidates.Select(candidate => ProjectIncompleteCandidate(candidate, inventory, packageByRule)).ToArray();
        var status = knowledgeBaseIsEmpty
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
            ManualGuidanceFindingStatus.KnowledgeBaseEmpty => "Não avaliado: a base de conhecimento está vazia.",
            ManualGuidanceFindingStatus.MultipleCandidates => $"{candidates.Length} regras candidatas; todas estão listadas para comparação humana, sem escolha automática.",
            ManualGuidanceFindingStatus.SingleCandidate => "Uma regra candidata ManualOnly; match observacional, não causal.",
            ManualGuidanceFindingStatus.Incomplete => candidates.Length > 0
                ? $"Evidência insuficiente/incompleta para {incomplete.Length} candidato(s); orientações completas continuam listadas separadamente."
                : "Evidência insuficiente/incompleta: não foi possível confirmar os requisitos de um ou mais candidatos.",
            _ => "Nenhuma regra ManualOnly correspondeu a este achado; isso não prova que um problema inexiste."
        };
        var findingHeading = $"Achado {index}: {Safe(safeFinding.Title, inventory)} · scanner {Safe(safeFinding.ScannerName, inventory)} · categoria {Safe(safeFinding.Category, inventory)}";
        var evidence = string.IsNullOrWhiteSpace(safeFinding.Evidence)
            ? "Evidência redigida: indisponível neste resultado."
            : "Evidência redigida: " + Safe(safeFinding.Evidence, inventory);
        var providerText = provider is null
            ? "Provider estruturado: ausente ou não reconhecido; nenhum valor livre foi exibido."
            : $"Provider estruturado (allowlist): {provider}";
        return new ManualGuidanceFinding(
            runReference,
            findingHeading,
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
        IReadOnlyDictionary<(string RuleId, int RuleVersion), KnowledgeRuleProvenance> packageByRule)
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
        return new ManualGuidanceRecommendation(
            "ManualOnly / não executada",
            $"{Safe(rule.Id, inventory)} · v{rule.Version}",
            Safe(rule.Title, inventory),
            Safe(rule.Domain, inventory),
            $"Força do match: {confidence}. Não é probabilidade causal nem previsão de eficácia.",
            Safe(match.Recommendation.ConfidenceExplanation, inventory),
            Safe(match.Recommendation.Explanation, inventory),
            applicability,
            sourceText,
            PackageSourceText(provenance, inventory),
            PackageVersionText(provenance),
            PackageHashText(provenance),
            Safe(procedure.DiagnosticAction, inventory),
            Safe(procedure.CorrectiveAction, inventory),
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
            PackageVersionText(provenance),
            PackageHashText(provenance));
    }

    private static string PackageSourceText(KnowledgeRuleProvenance? provenance, ComputerInventory inventory) =>
        $"Fonte declarada (não autenticada): {(provenance is null ? "não disponível" : Safe(provenance.DeclaredSource, inventory))}";

    private static string PackageVersionText(KnowledgeRuleProvenance? provenance) =>
        $"Versão do pacote (não autenticada): {provenance?.PackageVersion ?? "não disponível"}";

    private static string PackageHashText(KnowledgeRuleProvenance? provenance) =>
        $"SHA-256 do pacote (não autentica autoria nem conteúdo): {provenance?.Sha256 ?? "não disponível"}";

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
