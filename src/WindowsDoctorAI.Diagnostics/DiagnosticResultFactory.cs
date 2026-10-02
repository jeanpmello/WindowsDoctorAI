using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

internal static class DiagnosticResultFactory
{
    internal static DiagnosticResult Create(
        string scanner,
        string category,
        DiagnosticSeverity severity,
        DiagnosticStatus status,
        string title,
        string description,
        string recommendation,
        string evidence) => new(
            scanner,
            category,
            severity,
            status,
            title,
            description,
            recommendation,
            evidence,
            TimeSpan.Zero,
            DateTimeOffset.UtcNow);

    internal static DiagnosticResult Unavailable(string scanner, string category, string check, string? reason) => Create(
        scanner,
        category,
        DiagnosticSeverity.Information,
        DiagnosticStatus.Unavailable,
        $"{check}: indisponível",
        string.IsNullOrWhiteSpace(reason) ? "A fonte não forneceu dados verificáveis para esta checagem." : reason,
        "Verifique se esta API está disponível e se a conta possui permissão de leitura; a checagem não foi considerada saudável.",
        "Nenhuma evidência positiva ou negativa foi coletada.");

    internal static DiagnosticResult NotVerified(string scanner, string category, string check, string explanation, string evidence) => Create(
        scanner,
        category,
        DiagnosticSeverity.Information,
        DiagnosticStatus.NotVerified,
        $"{check}: não verificado",
        explanation,
        "Consulte uma fonte compatível ou execute a checagem em Windows com acesso ao recurso.",
        evidence);

    internal static DiagnosticResult Healthy(string scanner, string category, string check, string description, string evidence) => Create(
        scanner,
        category,
        DiagnosticSeverity.Information,
        DiagnosticStatus.Healthy,
        check,
        description,
        "Nenhuma ação de reparo foi executada; mantenha o acompanhamento normal.",
        evidence);
}
