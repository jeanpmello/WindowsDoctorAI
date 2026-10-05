using System.Text.RegularExpressions;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

/// <summary>Associa achados a regras textuais importadas; não inventa regras nem causa raiz.</summary>
public sealed class RecommendationEngine
{
    public IReadOnlyList<DiagnosticRecommendation> Recommend(
        DiagnosticReport report,
        IEnumerable<KnowledgeRule> rules,
        ComputerInventory? inventory = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(rules);

        var findings = report.Results.Where(result => result.Status == DiagnosticStatus.Finding).ToArray();
        var recommendations = new List<DiagnosticRecommendation>();
        foreach (var rule in rules)
        {
            if (IsDisabledCbsCodeRule(rule)) continue;
            var matches = new List<FindingMatch>();
            foreach (var finding in findings)
            {
                var evaluation = Evaluate(rule, finding, inventory);
                if (evaluation.Status == MatchEvaluationStatus.Matched)
                    matches.Add(new FindingMatch(finding, evaluation.Indicator!, evaluation.ExactCode));
            }

            if (matches.Count > 0)
                recommendations.Add(CreateRecommendation(rule, matches, inventory));
        }

        return Sort(recommendations);
    }

    /// <summary>Produz avaliação em memória por achado e expõe ManualOnly somente quando o match está completo.</summary>
    public IReadOnlyList<ManualRecommendationFindingAssessment> AssessManualFindings(
        DiagnosticReport report,
        IEnumerable<KnowledgeRule> rules,
        ComputerInventory? inventory = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(rules);
        var manualRules = rules
            .Where(rule => rule.Procedure is { ManualOnly: true })
            .Where(rule => !IsDisabledCbsCodeRule(rule))
            .ToArray();

        return report.Results
            .Where(result => result.Status == DiagnosticStatus.Finding)
            .Select(finding =>
            {
                var matches = new List<ManualRecommendationRuleMatch>();
                var incomplete = new List<ManualRecommendationIncompleteCandidate>();
                foreach (var rule in manualRules)
                {
                    var evaluation = Evaluate(rule, finding, inventory);
                    if (evaluation.Status == MatchEvaluationStatus.Matched)
                    {
                        var recommendation = CreateRecommendation(
                            rule,
                            [new FindingMatch(finding, evaluation.Indicator!, evaluation.ExactCode)],
                            inventory);
                        matches.Add(new ManualRecommendationRuleMatch(rule, recommendation));
                    }
                    else if (evaluation.Status == MatchEvaluationStatus.Incomplete)
                    {
                        incomplete.Add(new ManualRecommendationIncompleteCandidate(
                            rule,
                            evaluation.Reason,
                            evaluation.StructuredApplicabilityVerified));
                    }
                }

                return new ManualRecommendationFindingAssessment(finding, matches, incomplete);
            })
            .ToArray();
    }

    private static RuleMatchEvaluation Evaluate(KnowledgeRule rule, DiagnosticResult finding, ComputerInventory? inventory)
    {
        if (IsDisabledCbsCodeRule(rule)) return RuleMatchEvaluation.NoMatch;
        var searchable = string.Join("\n", finding.Title, finding.Description, finding.Evidence);
        string? indicator;
        var exactCode = false;
        var incompleteReasons = new List<string>();

        if (rule.Match is { } strictMatch)
        {
            if (strictMatch.ScannerNames is null
                || !strictMatch.ScannerNames.Contains(finding.ScannerName, StringComparer.OrdinalIgnoreCase)
                || !(strictMatch.RequiredContextTerms ?? Array.Empty<string>()).All(term => ContainsPhrase(searchable, term))
                || !ContainsToken(searchable, strictMatch.ExactErrorCode))
                return RuleMatchEvaluation.NoMatch;

            indicator = strictMatch.ExactErrorCode;
            exactCode = true;
            var requiredProviders = strictMatch.RequiredSourceProviders ?? Array.Empty<string>();
            if (requiredProviders.Count > 0)
            {
                var sourceProvider = finding.SourceMetadata is { } sourceMetadata
                    ? DiagnosticSourceMetadata.NormalizeProvider(sourceMetadata.Provider)
                    : null;
                if (sourceProvider is null)
                    incompleteReasons.Add("Evidência insuficiente/incompleta: a regra exige provider estruturado, mas o achado não contém um provider reconhecido.");
                else if (!requiredProviders.Contains(sourceProvider, StringComparer.OrdinalIgnoreCase))
                    return RuleMatchEvaluation.NoMatch;
            }

            var requiredEvidenceTypes = strictMatch.RequiredEvidenceTypes ?? Array.Empty<string>();
            if (requiredEvidenceTypes.Count > 0)
            {
                var requiredNames = string.Join(", ", requiredEvidenceTypes);
                incompleteReasons.Add($"Evidência insuficiente/incompleta: tipos estruturados exigidos ({requiredNames}) não estão disponíveis no modelo deste achado. Marcadores CBS isolados não são associados a este resultado.");
            }
        }
        else
        {
            indicator = rule.ErrorCodes.FirstOrDefault(code => ContainsToken(searchable, code));
            if (!string.IsNullOrWhiteSpace(indicator))
            {
                exactCode = true;
            }
            else
            {
                indicator = rule.Symptoms.FirstOrDefault(term => ContainsPhrase(searchable, term));
                if (string.IsNullOrWhiteSpace(indicator)) return RuleMatchEvaluation.NoMatch;
            }
        }

        var structuredApplicabilityVerified = false;
        if (rule.OsTarget is { } target)
        {
            var targetResult = EvaluateOperatingSystemTarget(target, inventory);
            if (targetResult.Status == TargetEvaluationStatus.NotApplicable)
                return RuleMatchEvaluation.NoMatch;
            if (targetResult.Status == TargetEvaluationStatus.Incomplete)
                incompleteReasons.Add(targetResult.Reason);
            else
                structuredApplicabilityVerified = true;
        }

        return incompleteReasons.Count > 0
            ? RuleMatchEvaluation.Incomplete(string.Join(" ", incompleteReasons), structuredApplicabilityVerified)
            : RuleMatchEvaluation.Matched(indicator!, exactCode, structuredApplicabilityVerified);
    }

    private static DiagnosticRecommendation CreateRecommendation(
        KnowledgeRule rule,
        IReadOnlyList<FindingMatch> matches,
        ComputerInventory? inventory)
    {
        var evidence = matches.Select(match => new RecommendationEvidence(
            match.Result.ScannerName,
            match.Result.Category,
            match.Result.Title,
            match.Result.Evidence,
            match.Result.Timestamp,
            match.Indicator) { SourceProvider = DiagnosticSourceMetadata.NormalizeProvider(match.Result.SourceMetadata?.Provider) }).ToArray();
        var exactMatches = matches.Where(match => match.ExactCode).ToArray();
        var distinctScanners = exactMatches.Select(match => match.Result.ScannerName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var confidence = exactMatches.Length > 0
            ? distinctScanners >= 2 ? MatchConfidence.High : MatchConfidence.Moderate
            : MatchConfidence.Low;
        var confidenceExplanation = confidence switch
        {
            MatchConfidence.High => "O mesmo código literal apareceu em resultados de pelo menos dois scanners distintos; isso mede força de match, não causalidade nem eficácia da solução.",
            MatchConfidence.Moderate => "Um código literal da regra foi encontrado em um achado; a referência declarada não foi verificada automaticamente.",
            _ => "A correspondência depende apenas de texto de sintoma; trate como pista fraca e confirme manualmente."
        };
        var matchedText = string.Join(", ", matches.Select(match => $"{match.Indicator} ({match.Result.ScannerName})").Distinct(StringComparer.OrdinalIgnoreCase));
        return new DiagnosticRecommendation(
            rule.Id,
            rule.Version,
            rule.Title,
            rule.Domain,
            rule.Impact,
            confidence,
            confidenceExplanation,
            $"Correspondência textual com a regra {rule.Id} v{rule.Version}: {matchedText}. "
            + (rule.OsTarget is null
                ? "Regra legada: a aplicabilidade textual não foi verificada automaticamente. "
                : "O alvo OS+build estruturado foi verificado com o inventário local para elegibilidade; isso não confirma causalidade. ")
            + "A associação não confirma causa nem garante que uma solução funcione.",
            rule.Causes,
            rule.Solutions,
            evidence,
            rule.References,
            rule.Applicability,
            rule.RequiredEvidence,
            rule.Procedure) { OsTarget = rule.OsTarget };
    }

    private static IReadOnlyList<DiagnosticRecommendation> Sort(IEnumerable<DiagnosticRecommendation> recommendations) => recommendations
        .OrderByDescending(item => item.Impact)
        .ThenByDescending(item => item.Confidence)
        .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    private static bool IsDisabledCbsCodeRule(KnowledgeRule rule) =>
        string.Equals(rule.Match?.ExactErrorCode, "0x800F0831", StringComparison.OrdinalIgnoreCase)
        || rule.ErrorCodes.Any(code => string.Equals(code, "0x800F0831", StringComparison.OrdinalIgnoreCase))
        || rule.Symptoms.Any(symptom => ContainsToken(symptom, "0x800F0831"));

    private static TargetEvaluation EvaluateOperatingSystemTarget(KnowledgeOperatingSystemTarget target, ComputerInventory? inventory)
    {
        if (inventory?.OperatingSystem is not { } operatingSystem
            || operatingSystem.ProductType is not { } productType
            || !Enum.IsDefined(productType)
            || !OperatingSystemBuildNumber.TryParseCanonicalPositive(operatingSystem.Build, out var build)
            || target.Families is null
            || target.Families.Count is 0 or > 2
            || target.Families.Any(family => !Enum.IsDefined(family))
            || target.Families.Distinct().Count() != target.Families.Count
            || target.MinimumBuild is <= 0
            || target.MaximumBuild is <= 0
            || target.MinimumBuild.HasValue && target.MaximumBuild.HasValue && target.MinimumBuild > target.MaximumBuild)
            return TargetEvaluation.Incomplete("Aplicabilidade estruturada não verificada: família do Windows ou build válido não está disponível no inventário.");

        var family = productType switch
        {
            OperatingSystemProductType.Workstation => KnowledgeOperatingSystemFamily.WindowsClient,
            OperatingSystemProductType.DomainController or OperatingSystemProductType.Server => KnowledgeOperatingSystemFamily.WindowsServer,
            _ => (KnowledgeOperatingSystemFamily?)null
        };
        if (family is not { } knownFamily)
            return TargetEvaluation.Incomplete("Aplicabilidade estruturada não verificada: o tipo de produto do Windows não é reconhecido.");

        var applicable = target.Families.Contains(knownFamily)
            && (!target.MinimumBuild.HasValue || build >= target.MinimumBuild.Value)
            && (!target.MaximumBuild.HasValue || build <= target.MaximumBuild.Value);
        return applicable ? TargetEvaluation.Verified : TargetEvaluation.NotApplicable;
    }

    private static bool ContainsToken(string? text, string? token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token)) return false;
        var pattern = $"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool ContainsPhrase(string? text, string? phrase) =>
        !string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(phrase)
        && text.Contains(phrase, StringComparison.OrdinalIgnoreCase);

    private sealed record FindingMatch(DiagnosticResult Result, string Indicator, bool ExactCode);

    private sealed record RuleMatchEvaluation(
        MatchEvaluationStatus Status,
        string? Indicator,
        bool ExactCode,
        bool StructuredApplicabilityVerified,
        string Reason)
    {
        public static RuleMatchEvaluation NoMatch { get; } = new(MatchEvaluationStatus.NoMatch, null, false, false, string.Empty);
        public static RuleMatchEvaluation Matched(string indicator, bool exactCode, bool applicabilityVerified) =>
            new(MatchEvaluationStatus.Matched, indicator, exactCode, applicabilityVerified, string.Empty);
        public static RuleMatchEvaluation Incomplete(string reason, bool applicabilityVerified = false) =>
            new(MatchEvaluationStatus.Incomplete, null, false, applicabilityVerified, reason);
    }

    private sealed record TargetEvaluation(TargetEvaluationStatus Status, string Reason)
    {
        public static TargetEvaluation Verified { get; } = new(TargetEvaluationStatus.Verified, string.Empty);
        public static TargetEvaluation NotApplicable { get; } = new(TargetEvaluationStatus.NotApplicable, string.Empty);
        public static TargetEvaluation Incomplete(string reason) => new(TargetEvaluationStatus.Incomplete, reason);
    }

    private enum MatchEvaluationStatus { NoMatch, Matched, Incomplete }
    private enum TargetEvaluationStatus { NotApplicable, Verified, Incomplete }
}
