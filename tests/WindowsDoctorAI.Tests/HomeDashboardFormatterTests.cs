using WindowsDoctorAI.Application;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class HomeDashboardFormatterTests
{
    [Fact]
    public void FormatWithoutReportDoesNotInventHealthScore()
    {
        var display = HomeDashboardFormatter.Format(null, TimeSpan.Zero);

        Assert.Equal("Não calculado", display.HealthScore);
        Assert.Contains("não representa a saúde global do computador", display.HealthScoreDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não contém verificações do Diagnostic Engine", display.HealthScoreDescription, StringComparison.Ordinal);
        Assert.Equal("Não coletado", display.ComputerName);
        Assert.Equal("Sem resultados estruturados neste registro. O inventário foi preservado.", display.FindingsSummary);
    }

    [Fact]
    public void FormatPrioritizesConfirmedCriticalFindingsAndShowsCoverageGapsSeparately()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var report = new DiagnosticReport(
        [
            Finding(DiagnosticSeverity.Critical, DiagnosticStatus.Finding, "Falha crítica"),
            Finding(DiagnosticSeverity.Information, DiagnosticStatus.Healthy, "Verificação confirmada"),
            Finding(DiagnosticSeverity.Information, DiagnosticStatus.Unavailable, "Fonte indisponível"),
            Finding(DiagnosticSeverity.Information, DiagnosticStatus.NotVerified, "Fonte não verificada")
        ], now, now, TimeSpan.FromSeconds(2), new HealthScore(75));

        var display = HomeDashboardFormatter.Format(report, TimeSpan.FromSeconds(2));

        Assert.Contains("Prioridade alta", display.DiagnosticBrief);
        Assert.Contains("Confirmadas: 2", display.CoverageSummary);
        Assert.Contains("Indisponíveis: 1", display.CoverageSummary);
        Assert.Contains("Não verificadas: 1", display.CoverageSummary);
        Assert.Contains("Revise primeiro as evidências críticas", display.NextStepSummary);
    }

    private static DiagnosticResult Finding(DiagnosticSeverity severity, DiagnosticStatus status, string title) => new(
        "Scanner", "Sistema", severity, status, title, "Descrição de teste", string.Empty,
        string.Empty, TimeSpan.Zero, DateTimeOffset.UtcNow);
}
