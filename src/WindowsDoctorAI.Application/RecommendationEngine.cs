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
            if (rule.OsTarget is { } osTarget && !MatchesOperatingSystemTarget(osTarget, inventory)) continue;
            var matches = new List<(DiagnosticResult Result, string Indicator, bool ExactCode)>();
            foreach (var finding in findings)
            {
                var searchable = string.Join("\n", finding.Title, finding.Description, finding.Evidence);
                if (rule.Match is { } strictMatch)
                {
                    var sourceProvider = finding.SourceMetadata is { } sourceMetadata
                        ? DiagnosticSourceMetadata.NormalizeProvider(sourceMetadata.Provider)
                        : null;
                    var requiredEvidenceTypes = strictMatch.RequiredEvidenceTypes ?? Array.Empty<string>();
                    if (!strictMatch.ScannerNames.Contains(finding.ScannerName, StringComparer.OrdinalIgnoreCase)
                        || !strictMatch.RequiredContextTerms.All(term => ContainsPhrase(searchable, term))
                        || !strictMatch.RequiredSourceProviders.All(provider => string.Equals(sourceProvider, provider, StringComparison.OrdinalIgnoreCase))
                        || requiredEvidenceTypes.Count != 0
                        || !ContainsToken(searchable, strictMatch.ExactErrorCode))
                        continue;
                    matches.Add((finding, strictMatch.ExactErrorCode, true));
                    continue;
                }

                var errorCode = rule.ErrorCodes.FirstOrDefault(code => ContainsToken(searchable, code));
                if (!string.IsNullOrWhiteSpace(errorCode))
                {
                    matches.Add((finding, errorCode, true));
                    continue;
                }

                var symptom = rule.Symptoms.FirstOrDefault(term => ContainsPhrase(searchable, term));
                if (!string.IsNullOrWhiteSpace(symptom))
                    matches.Add((finding, symptom, false));
            }

            if (matches.Count == 0) continue;
            var evidence = matches.Select(match => new RecommendationEvidence(
                match.Result.ScannerName,
                match.Result.Category,
                match.Result.Title,
                match.Result.Evidence,
                match.Result.Timestamp,
                match.Indicator)).ToArray();
            var exactMatches = matches.Where(match => match.ExactCode).ToArray();
            var distinctScanners = exactMatches.Select(match => match.Result.ScannerName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var confidence = exactMatches.Length > 0
                ? distinctScanners >= 2 ? MatchConfidence.High : MatchConfidence.Moderate
                : MatchConfidence.Low;
            var confidenceExplanation = confidence switch
            {
                MatchConfidence.High => "O mesmo código literal apareceu em resultados de pelo menos dois scanners distintos; isso mede força do match, não causalidade nem eficácia da solução.",
                MatchConfidence.Moderate => "Um código literal da regra foi encontrado em um achado; a referência declarada não foi verificada automaticamente.",
                _ => "A correspondência depende apenas de texto de sintoma; trate como pista fraca e confirme manualmente."
            };
            var matchedText = string.Join(", ", matches.Select(match => $"{match.Indicator} ({match.Result.ScannerName})").Distinct(StringComparer.OrdinalIgnoreCase));
            recommendations.Add(new DiagnosticRecommendation(
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
                rule.Procedure) { OsTarget = rule.OsTarget });
        }

        return recommendations
            .OrderByDescending(item => item.Impact)
            .ThenByDescending(item => item.Confidence)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool IsDisabledCbsCodeRule(KnowledgeRule rule) =>
        string.Equals(rule.Match?.ExactErrorCode, "0x800F0831", StringComparison.OrdinalIgnoreCase)
        || rule.ErrorCodes.Any(code => string.Equals(code, "0x800F0831", StringComparison.OrdinalIgnoreCase))
        || rule.Symptoms.Any(symptom => ContainsToken(symptom, "0x800F0831"));

    private static bool MatchesOperatingSystemTarget(KnowledgeOperatingSystemTarget target, ComputerInventory? inventory)
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
            return false;

        var family = productType switch
        {
            OperatingSystemProductType.Workstation => KnowledgeOperatingSystemFamily.WindowsClient,
            OperatingSystemProductType.DomainController or OperatingSystemProductType.Server => KnowledgeOperatingSystemFamily.WindowsServer,
            _ => (KnowledgeOperatingSystemFamily?)null
        };
        return family is { } knownFamily
            && target.Families.Contains(knownFamily)
            && (!target.MinimumBuild.HasValue || build >= target.MinimumBuild.Value)
            && (!target.MaximumBuild.HasValue || build <= target.MaximumBuild.Value);
    }

    private static bool ContainsToken(string text, string token)
    {
        var pattern = $"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool ContainsPhrase(string text, string phrase) =>
        !string.IsNullOrWhiteSpace(phrase) && text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
