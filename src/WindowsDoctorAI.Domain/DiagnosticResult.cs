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
    DateTimeOffset Timestamp);

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
