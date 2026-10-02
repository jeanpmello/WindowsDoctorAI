using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

/// <summary>Produz texto para a UI a partir de uma cópia redigida do relatório.</summary>
public static class DiagnosticDisplayFormatter
{
    public static string FormatFindings(DiagnosticReport report, ComputerInventory? inventory = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        report = DiagnosticPrivacyRedactor.RedactReport(report, inventory)!;
        var findings = report.Results.Where(result => result.Status == DiagnosticStatus.Finding)
            .OrderByDescending(result => result.Severity)
            .ThenByDescending(result => result.Timestamp)
            .ToArray();
        if (findings.Length == 0)
        {
            return report.VerifiedChecks == 0
                ? "Nenhum achado confirmado; as verificações ficaram indisponíveis ou não verificadas, então não há score."
                : "Nenhum problema crítico ou aviso foi encontrado nas verificações confirmadas.";
        }

        const int displayLimit = 10;
        var lines = findings.Take(displayLimit).Select(result =>
            $"[{SeverityText(result.Severity)}] {result.ScannerName} — {result.Title}\n{result.Description}\nRecomendação: {result.Recommendation}\nEvidência: {result.Evidence}");
        var remainder = findings.Length > displayLimit
            ? $"{Environment.NewLine}Exibindo {displayLimit} de {findings.Length} achados. O relatório contém a lista completa."
            : string.Empty;
        return string.Join(Environment.NewLine + Environment.NewLine, lines) + remainder;
    }

    private static string SeverityText(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Critical => "Crítico",
        DiagnosticSeverity.Warning => "Aviso",
        _ => "Informativo"
    };
}
