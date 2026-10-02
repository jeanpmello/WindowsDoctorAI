using System.Text.RegularExpressions;

namespace WindowsDoctorAI.Domain;

/// <summary>Gravidade atribuída a uma observação efetivamente coletada.</summary>
public enum DiagnosticSeverity
{
    Information,
    Warning,
    Critical
}

/// <summary>Estado que distingue achados de verificações sem dados observáveis.</summary>
public enum DiagnosticStatus
{
    Finding,
    Healthy,
    Unavailable,
    NotVerified
}

/// <summary>Origem estruturada allowlist; nunca contém nomes de providers arbitrários.</summary>
public sealed record DiagnosticSourceMetadata(string Provider)
{
    public const string WindowsUpdateClientProvider = "WindowsUpdateClient";

    /// <summary>Normaliza somente nomes conhecidos obtidos do campo estruturado do Event Log.</summary>
    public static DiagnosticSourceMetadata? FromEventProvider(string? provider)
    {
        var normalized = NormalizeProvider(provider);
        return normalized is null ? null : new DiagnosticSourceMetadata(normalized);
    }

    /// <summary>Retorna o identificador canônico para providers explicitamente permitidos.</summary>
    public static string? NormalizeProvider(string? provider) =>
        string.Equals(provider, WindowsUpdateClientProvider, StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "Microsoft-Windows-WindowsUpdateClient", StringComparison.OrdinalIgnoreCase)
            ? WindowsUpdateClientProvider
            : null;
}

/// <summary>Prova minimizada do canal e HRESULT observados em um evento da execução atual.</summary>
public sealed record WindowsUpdateEventEvidence(string LogName, string ErrorCode)
{
    public const string OperationalChannel = "Microsoft-Windows-WindowsUpdateClient/Operational";
    public const string CbsStoreCorruptionHresult = "0x800F0831";

    public bool IsExactCbsStoreCorruptionEvent =>
        string.Equals(LogName, OperationalChannel, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ErrorCode, CbsStoreCorruptionHresult, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Marcadores CBS da Microsoft aceitos para diagnóstico de pacote ausente/resolução falha.</summary>
public enum CbsEvidenceType
{
    ManifestMissing,
    FailedToResolvePackage
}

/// <summary>Únicos dados CBS dinâmicos que podem acompanhar o resultado: tipo do marcador e package identity validada.</summary>
public sealed record CbsPackageEvidence(CbsEvidenceType Type, string PackageIdentity);

/// <summary>Valida package identities CBS por formato estrito, sem aceitar caminhos ou texto livre.</summary>
public static class CbsPackageIdentityValidator
{
    private static readonly Regex IdentityPattern = new(
        @"\A(?:Package_[0-9]{1,6}_for_KB[0-9]{6,8}|Microsoft-Windows-[A-Za-z0-9][A-Za-z0-9.-]{0,80}-Package)~[A-Fa-f0-9]{16}~(?:amd64|arm64|x86|wow64|msil|neutral|none)~(?:[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})?)?~[0-9]{1,5}(?:\.[0-9]{1,5}){3}\z",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    public static bool IsValid(string? packageIdentity) =>
        packageIdentity is { Length: <= 240 } && IdentityPattern.IsMatch(packageIdentity);
}

/// <summary>Formato comum e serializável entregue por todos os scanners e plugins.</summary>
public sealed record DiagnosticResult(
    string ScannerName,
    string Category,
    DiagnosticSeverity Severity,
    DiagnosticStatus Status,
    string Title,
    string Description,
    string Recommendation,
    string Evidence,
    TimeSpan Duration,
    DateTimeOffset Timestamp)
{
    /// <summary>Metadado de origem estruturado, normalizado e independente do texto do evento.</summary>
    public DiagnosticSourceMetadata? SourceMetadata { get; init; }

    /// <summary>Evidência CBS minimizada; texto bruto, caminho e conteúdo do evento não são armazenados aqui.</summary>
    public CbsPackageEvidence? CbsEvidence { get; init; }

    /// <summary>Canal/HRESULT tipados e minimizados do evento Windows Update desta execução.</summary>
    public WindowsUpdateEventEvidence? WindowsUpdateEventEvidence { get; init; }
}

/// <summary>Resumo da cobertura real por categoria, sem tratar indisponibilidade como saúde.</summary>
public sealed record DiagnosticCategorySummary(
    string Category,
    int VerifiedChecks,
    int Findings,
    int CriticalProblems,
    int Warnings,
    int UnavailableChecks,
    int NotVerifiedChecks);

/// <summary>Conjunto consolidado de resultados de uma execução do Diagnostic Engine.</summary>
public sealed record DiagnosticReport(
    IReadOnlyList<DiagnosticResult> Results,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    TimeSpan Duration,
    HealthScore? HealthScore)
{
    public int CriticalProblems => Results.Count(result => result.Status == DiagnosticStatus.Finding && result.Severity == DiagnosticSeverity.Critical);

    public int Warnings => Results.Count(result => result.Status == DiagnosticStatus.Finding && result.Severity == DiagnosticSeverity.Warning);

    public int VerifiedChecks => Results.Count(result => result.Status is DiagnosticStatus.Healthy or DiagnosticStatus.Finding);

    public int UnavailableChecks => Results.Count(result => result.Status == DiagnosticStatus.Unavailable);

    public int NotVerifiedChecks => Results.Count(result => result.Status == DiagnosticStatus.NotVerified);

    public IReadOnlyList<DiagnosticCategorySummary> Categories => Results
        .GroupBy(result => result.Category, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
        .Select(group => new DiagnosticCategorySummary(
            group.Key,
            group.Count(result => result.Status is DiagnosticStatus.Healthy or DiagnosticStatus.Finding),
            group.Count(result => result.Status == DiagnosticStatus.Finding),
            group.Count(result => result.Status == DiagnosticStatus.Finding && result.Severity == DiagnosticSeverity.Critical),
            group.Count(result => result.Status == DiagnosticStatus.Finding && result.Severity == DiagnosticSeverity.Warning),
            group.Count(result => result.Status == DiagnosticStatus.Unavailable),
            group.Count(result => result.Status == DiagnosticStatus.NotVerified)))
        .ToArray();
}
