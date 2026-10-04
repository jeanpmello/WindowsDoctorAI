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
            finding.SourceMetadata?.Provider);
    }

    public static string Create(RecommendationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return Create(evidence.ScannerName, evidence.Category, evidence.Title, evidence.Evidence,
            evidence.SourceProvider);
    }

    private static string Create(string scanner, string category, string title, string evidence, string? provider)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Scanner = DiagnosticPrivacyRedactor.RedactText(scanner).Trim(),
            Category = DiagnosticPrivacyRedactor.RedactText(category).Trim(),
            Title = DiagnosticPrivacyRedactor.RedactText(title).Trim(),
            Evidence = DiagnosticPrivacyRedactor.RedactText(evidence).Trim(),
            Provider = DiagnosticSourceMetadata.NormalizeProvider(provider)
        });
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}
