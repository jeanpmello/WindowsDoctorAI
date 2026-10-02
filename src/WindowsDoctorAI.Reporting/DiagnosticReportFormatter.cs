using System.Globalization;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Reporting;

/// <summary>Formata um relatório local com score, cobertura, achados, evidências e medições dos scanners.</summary>
public sealed class DiagnosticReportFormatter
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");

    public string Format(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            "Windows Doctor AI — Relatório de diagnóstico",
            $"Início (UTC): {report.StartedAtUtc:O}",
            $"Conclusão (UTC): {report.CompletedAtUtc:O}",
            $"Duração: {Duration(report.Duration)}",
            $"Health Score: {(report.HealthScore is { } score ? score.Value.ToString(BrazilianCulture) : "não calculado — nenhuma verificação confirmada")}",
            $"Verificações confirmadas: {report.VerifiedChecks}",
            $"Indisponíveis: {report.UnavailableChecks}; não verificadas: {report.NotVerifiedChecks}",
            $"Problemas críticos: {report.CriticalProblems}; avisos: {report.Warnings}",
            string.Empty,
            "Cobertura por categoria:"
        };

        lines.AddRange(report.Categories.Select(category =>
            $"- {category.Category}: {category.VerifiedChecks} confirmada(s), {category.Findings} achado(s), {category.UnavailableChecks} indisponível(is), {category.NotVerifiedChecks} não verificada(s)."));
        if (report.Categories.Count == 0) lines.Add("- Nenhuma categoria foi verificada.");
        lines.Add(string.Empty);
        lines.Add("Resultados:");

        if (report.Results.Count == 0)
        {
            lines.Add("Nenhum scanner retornou resultados.");
        }
        else
        {
            foreach (var result in report.Results)
            {
                lines.Add(string.Empty);
                lines.Add($"[{Severity(result.Severity)} / {Status(result.Status)}] {result.Title}");
                lines.Add($"Scanner: {result.ScannerName} · Categoria: {result.Category}");
                lines.Add($"Descrição: {result.Description}");
                lines.Add($"Recomendação: {result.Recommendation}");
                lines.Add($"Evidência: {result.Evidence}");
                lines.Add($"Tempo: {Duration(result.Duration)} · Horário (UTC): {result.Timestamp:O}");
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string Severity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Critical => "Crítico",
        DiagnosticSeverity.Warning => "Aviso",
        _ => "Informativo"
    };

    private static string Status(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Finding => "achado",
        DiagnosticStatus.Healthy => "verificado sem achado",
        DiagnosticStatus.Unavailable => "indisponível",
        _ => "não verificado"
    };

    private static string Duration(TimeSpan duration) => duration.TotalSeconds.ToString("N2", BrazilianCulture) + " s";
}
