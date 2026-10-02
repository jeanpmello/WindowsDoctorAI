using System.Text.RegularExpressions;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

/// <summary>Encontra identificadores literalmente compartilhados sem afirmar que sejam causa raiz.</summary>
public sealed class RootCauseAnalyzer
{
    private static readonly Regex SharedIdentifier = new(
        @"(?<![A-Za-z0-9])(?:KB\d{6,8}|0x[0-9a-f]{8}|(?:event(?:\s+id)?|evento(?:\s+id)?|id)\s*[:#=]?\s*\d{1,8}|(?:PCI|USB|ACPI|ROOT)\\[A-Z0-9_&\\-]{3,80})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex ObservedServiceName = new(
        @"\bNome=([A-Za-z_][A-Za-z0-9_.-]{2,39})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public RootCauseAnalysis Analyze(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var findings = report.Results.Where(result => result.Status == DiagnosticStatus.Finding).ToArray();
        var evidenceByIdentifier = new Dictionary<string, List<DiagnosticResult>>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in findings)
        {
            var searchable = SearchableText(result);
            foreach (Match match in SharedIdentifier.Matches(searchable))
                AddEvidence(evidenceByIdentifier, match.Value.Trim(), result);
        }

        // Serviço é tratado como identificador apenas quando um scanner o rotula como Nome=.
        // O mesmo nome precisa aparecer literalmente em outra fonte observada.
        var serviceNames = findings
            .Where(result => result.ScannerName.Contains("Services", StringComparison.OrdinalIgnoreCase)
                || result.Category.Contains("servi", StringComparison.OrdinalIgnoreCase))
            .SelectMany(result => ObservedServiceName.Matches(SearchableText(result)).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var serviceName in serviceNames)
        {
            var identifier = $"service:{serviceName}";
            foreach (var result in findings)
                if (ContainsToken(SearchableText(result), serviceName))
                    AddEvidence(evidenceByIdentifier, identifier, result);
        }

        var correlations = evidenceByIdentifier
            .Where(pair => pair.Value.Select(result => result.ScannerName).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            .Select(pair =>
            {
                var items = pair.Value.OrderBy(result => result.Timestamp).ToArray();
                var evidence = items.Select(result => new RootCauseEvidence(
                    result.ScannerName, result.Category, result.Title, result.Evidence, result.Timestamp)).ToArray();
                return new CorrelationObservation(
                    pair.Key,
                    MatchConfidence.Moderate,
                    $"O identificador literal {pair.Key} aparece em {items.Length} achados de fontes distintas. A repetição é uma associação observada, não evidência de causa ou sequência temporal.",
                    evidence);
            })
            .OrderBy(item => item.SharedIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary = correlations.Length == 0
            ? "Não foi encontrada uma associação verificável por identificador compartilhado entre fontes. Com os dados deste diagnóstico, a causa raiz permanece indeterminada."
            : $"Foram observadas {correlations.Length} associação(ões) por identificador compartilhado. Os dados não provam causalidade nem uma ordem de eventos; a causa raiz permanece indeterminada.";
        return new RootCauseAnalysis(summary, correlations, CauseDetermined: false);
    }

    private static string SearchableText(DiagnosticResult result) =>
        string.Join("\n", result.Title, result.Description, result.Evidence);

    private static bool ContainsToken(string text, string token)
    {
        var pattern = $"(?<![A-Za-z0-9]){Regex.Escape(token)}(?![A-Za-z0-9])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static void AddEvidence(IDictionary<string, List<DiagnosticResult>> target, string identifier, DiagnosticResult result)
    {
        if (!target.TryGetValue(identifier, out var items))
            target[identifier] = items = [];
        if (!items.Contains(result)) items.Add(result);
    }
}
