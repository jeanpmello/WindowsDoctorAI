using System.Text.RegularExpressions;

namespace WindowsDoctorAI.Networking;

/// <summary>Remove segredos comuns de saídas de CLI antes de persistir ou enviar evidências ao modelo.</summary>
public sealed class NetworkEvidenceRedactor
{
    private static readonly Regex KeyValueSecret = new(
        @"(?im)^(\s*(?:password|passwd|passphrase|secret|community|token|api[-_ ]?key|authorization)\s*[:=]\s*).*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BearerToken = new(
        @"(?i)(Bearer\s+)[^\s]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PrivateKey = new(
        @"(?s)-----BEGIN [^-]*PRIVATE KEY-----.*?-----END [^-]*PRIVATE KEY-----",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public NetworkInspectionResult Redact(NetworkInspectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result with
        {
            Evidence = result.Evidence
                .Select(evidence => evidence with { Content = Redact(evidence.Content) })
                .ToArray()
        };
    }

    public string Redact(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var redacted = PrivateKey.Replace(content, "[REDACTED PRIVATE KEY]");
        redacted = KeyValueSecret.Replace(redacted, "$1[REDACTED]");
        return BearerToken.Replace(redacted, "$1[REDACTED]");
    }
}
