using System.Security.Cryptography;
using System.Text.Json;

namespace WindowsDoctorAI.Domain;

/// <summary>Identidade estável de um achado; usa campos estruturados redigidos e não depende da posição no relatório.</summary>
public static class DiagnosticFindingIdentity
{
    public static string Create(DiagnosticResult finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return Create(finding.ScannerName, finding.Category, finding.Title, finding.Evidence,
            finding.SourceMetadata?.Provider, EventOccurrenceTimestamp(finding.ScannerName, finding.Timestamp));
    }

    public static string Create(RecommendationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return Create(evidence.ScannerName, evidence.Category, evidence.Title, evidence.Evidence,
            evidence.SourceProvider, EventOccurrenceTimestamp(evidence.ScannerName, evidence.TimestampUtc));
    }

    private static string Create(
        string scanner,
        string category,
        string title,
        string evidence,
        string? provider,
        DateTimeOffset? occurrenceTimestampUtc)
    {
        var safeScanner = DiagnosticPrivacyRedactor.RedactText(scanner).Trim();
        var safeCategory = DiagnosticPrivacyRedactor.RedactText(category).Trim();
        var safeTitle = DiagnosticPrivacyRedactor.RedactText(title).Trim();
        var safeEvidence = DiagnosticPrivacyRedactor.RedactText(evidence).Trim();
        var safeProvider = DiagnosticSourceMetadata.NormalizeProvider(provider);
        var payload = occurrenceTimestampUtc is null
            ? JsonSerializer.SerializeToUtf8Bytes(new
            {
                Scanner = safeScanner,
                Category = safeCategory,
                Title = safeTitle,
                Evidence = safeEvidence,
                Provider = safeProvider
            })
            : JsonSerializer.SerializeToUtf8Bytes(new
            {
                Scanner = safeScanner,
                Category = safeCategory,
                Title = safeTitle,
                Evidence = safeEvidence,
                Provider = safeProvider,
                OccurrenceTimestampUtc = occurrenceTimestampUtc.Value
            });
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private static DateTimeOffset? EventOccurrenceTimestamp(string scanner, DateTimeOffset timestamp) =>
        string.Equals(scanner, "Event Viewer", StringComparison.OrdinalIgnoreCase)
            ? timestamp.ToUniversalTime()
            : null;
}
