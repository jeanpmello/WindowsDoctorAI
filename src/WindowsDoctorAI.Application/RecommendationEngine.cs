using System.Text.RegularExpressions;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

/// <summary>Associa achados a regras textuais importadas; não inventa regras nem causa raiz.</summary>
public sealed class RecommendationEngine
{
    public IReadOnlyList<DiagnosticRecommendation> Recommend(
        DiagnosticReport report,
        IEnumerable<KnowledgeRule> rules)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(rules);

        var findings = report.Results.Where(result => result.Status == DiagnosticStatus.Finding).ToArray();
        var recommendations = new List<DiagnosticRecommendation>();
        foreach (var rule in rules)
        {
            var matches = new List<(DiagnosticResult Result, string Indicator, bool ExactCode)>();
            foreach (var finding in findings)
            {
                var searchable = string.Join("\n", finding.Title, finding.Description, finding.Evidence);
                if (rule.Match is { } strictMatch)
                {
                    var sourceProvider = finding.SourceMetadata is { } sourceMetadata
                        ? DiagnosticSourceMetadata.NormalizeProvider(sourceMetadata.Provider)
                        : null;
                    var currentRunEventMatches = !string.Equals(
                            strictMatch.ExactErrorCode,
                            WindowsUpdateEventEvidence.CbsStoreCorruptionHresult,
                            StringComparison.OrdinalIgnoreCase)
                        || finding.WindowsUpdateEventEvidence is { IsExactCbsStoreCorruptionEvent: true };
                    var requiredEvidenceTypes = strictMatch.RequiredEvidenceTypes ?? Array.Empty<string>();
                    var evidenceTypeMatches = requiredEvidenceTypes.Count == 0
                        || finding.CbsEvidence is { } cbsEvidence
                        && Enum.IsDefined(cbsEvidence.Type)
                        && CbsPackageIdentityValidator.IsValid(cbsEvidence.PackageIdentity)
                        && requiredEvidenceTypes.Contains(cbsEvidence.Type.ToString(), StringComparer.Ordinal)
                        && string.Equals(finding.Evidence,
                            $"CBS marker={cbsEvidence.Type}; package identity={cbsEvidence.PackageIdentity}",
                            StringComparison.Ordinal);
                    if (!strictMatch.ScannerNames.Contains(finding.ScannerName, StringComparer.OrdinalIgnoreCase)
                        || !strictMatch.RequiredContextTerms.All(term => ContainsPhrase(searchable, term))
                        || !strictMatch.RequiredSourceProviders.All(provider => string.Equals(sourceProvider, provider, StringComparison.OrdinalIgnoreCase))
                        || !currentRunEventMatches
                        || !evidenceTypeMatches
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
                $"Correspondência textual com a regra {rule.Id} v{rule.Version}: {matchedText}. A associação não confirma causa nem garante que uma solução funcione.",
                rule.Causes,
                rule.Solutions,
                evidence,
                rule.References,
                rule.Applicability,
                rule.RequiredEvidence,
                rule.Procedure));
        }

        return recommendations
            .OrderByDescending(item => item.Impact)
            .ThenByDescending(item => item.Confidence)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool ContainsToken(string text, string token)
    {
        var pattern = $"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool ContainsPhrase(string text, string phrase) =>
        !string.IsNullOrWhiteSpace(phrase) && text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
