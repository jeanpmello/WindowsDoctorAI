using WindowsDoctorAI.Application;

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
}
