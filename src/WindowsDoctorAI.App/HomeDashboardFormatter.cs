using System.Globalization;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.App;

internal sealed class HomeDashboardFormatter
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] DashboardCategories = ["Sistema", "Drivers", "Hardware", "Rede", "Segurança"];

    public HomeDashboardReportDisplay CreateReportDisplay(DiagnosticReport? report, TimeSpan overallDuration, ComputerInventory? inventory = null)
    {
        var diagnosticDurationText = FormatDuration(overallDuration);
        report = DiagnosticPrivacyRedactor.RedactReport(report, inventory);
        if (report is null)
        {
            return new HomeDashboardReportDisplay(
                "Não calculado",
                "Este registro não contém verificações do Diagnostic Engine. A pontuação é heurística e não representa a saúde global do computador.",
                "—",
                "—",
                diagnosticDurationText,
                "Diagnóstico de rede e segurança não incluído; os scanners do Milestone 2 cobrem sistema, drivers e hardware.",
                "Sem resultados estruturados neste registro. O inventário foi preservado.");
        }

        return new HomeDashboardReportDisplay(
            report.HealthScore is { } score ? score.Value.ToString(CultureInfo.InvariantCulture) : "Não calculado",
            report.HealthScore is null
                ? "Nenhuma verificação foi confirmada; itens indisponíveis ou não verificados não contam. A pontuação é heurística e não representa a saúde global do computador."
                : $"Pontuação heurística baseada em {report.VerifiedChecks} verificação(ões) observada(s); {report.UnavailableChecks + report.NotVerifiedChecks} indisponível(is)/não verificad(a/as) não contam.",
            report.CriticalProblems.ToString(CultureInfo.InvariantCulture),
            report.Warnings.ToString(CultureInfo.InvariantCulture),
            diagnosticDurationText,
            FormatCategories(report),
            DiagnosticDisplayFormatter.FormatFindings(report, inventory));
    }

    public HomeDashboardInventoryDisplay CreateInventoryDisplay(ComputerInventory inventory)
    {
        inventory = DiagnosticPrivacyRedactor.RedactInventory(inventory);
        return new HomeDashboardInventoryDisplay(
            Text(inventory.ComputerName),
            $"{Text(inventory.Manufacturer)} · {Text(inventory.Model)}",
            Text(inventory.SerialNumber),
            $"{Text(inventory.OperatingSystem.Name)} · versão {Text(inventory.OperatingSystem.Version)} · build {Text(inventory.OperatingSystem.Build)}",
            $"{Text(inventory.Processor.Name)} · {Text(inventory.Processor.Cores)} núcleos / {Text(inventory.Processor.LogicalProcessors)} processadores lógicos",
            inventory.InstalledMemoryBytes is ulong bytes ? $"{bytes / 1_073_741_824d:N1} GB" : "Indisponível",
            inventory.GraphicsAdapters.Count == 0 ? "Indisponível" : string.Join(Environment.NewLine, inventory.GraphicsAdapters.Select(item => item.MemoryBytes is ulong gpuBytes
                ? $"{item.Name} · {gpuBytes / 1_073_741_824d:N1} GB"
                : item.Name)),
            BuildDisksSummary(inventory),
            $"{Text(inventory.Bios.Manufacturer)} · {Text(inventory.Bios.Version)} · série {Text(inventory.Bios.SerialNumber)}",
            Text(inventory.FirmwareType),
            inventory.Tpm.IsPresent switch
            {
                true => $"Presente · versão {Text(inventory.Tpm.SpecificationVersion)} · fabricante {Text(inventory.Tpm.Manufacturer)} · habilitado {BoolText(inventory.Tpm.IsEnabled)} · ativado {BoolText(inventory.Tpm.IsActivated)}",
                false => "Não detectado",
                _ => "Indisponível"
            },
            BoolText(inventory.SecureBootEnabled),
            $"{Text(inventory.UserName)} · domínio/grupo {Text(inventory.Domain)}",
            inventory.OperatingSystem.Uptime is TimeSpan span ? $"{span.Days}d {span.Hours}h {span.Minutes}min" : "Indisponível",
            Join(inventory.IPv4Addresses),
            Join(inventory.IPv6Addresses),
            inventory.NetworkAdapters.Count == 0 ? "Indisponível" : string.Join(Environment.NewLine,
                inventory.NetworkAdapters.Select(adapter => $"{adapter.Name} · {adapter.Description} · {adapter.Status}")));
    }

    private static string BuildDisksSummary(ComputerInventory inventory)
    {
        var physicalDisks = inventory.PhysicalDisks.Select(disk =>
        {
            var size = disk.SizeBytes is ulong diskBytes ? $"{diskBytes / 1_073_741_824d:N0} GB" : "capacidade indisponível";
            return $"{Text(disk.Model)} · {size} · {Text(disk.MediaType)} · {Text(disk.InterfaceType)}";
        });

        var volumes = inventory.Disks.Select(disk =>
        {
            var capacity = disk.CapacityBytes is ulong total ? $"{total / 1_073_741_824d:N0} GB" : "capacidade indisponível";
            var free = disk.FreeBytes is ulong available ? $"{available / 1_073_741_824d:N0} GB livres" : "espaço livre indisponível";
            return $"{disk.Name} {Text(disk.Label)} · {capacity} · {free} · {Text(disk.FileSystem)}";
        });

        var diskSummaries = physicalDisks.Concat(volumes).ToArray();
        return diskSummaries.Length == 0 ? "Indisponível" : string.Join(Environment.NewLine, diskSummaries);
    }

    private static string FormatCategories(DiagnosticReport report)
    {
        var actual = report.Categories.ToDictionary(category => category.Category, StringComparer.OrdinalIgnoreCase);
        var lines = DashboardCategories.Select(category =>
        {
            if (!actual.TryGetValue(category, out var summary)) return $"{category}: não verificada neste milestone";
            return $"{category}: {summary.VerifiedChecks} verificada(s), {summary.Findings} achado(s), {summary.UnavailableChecks + summary.NotVerifiedChecks} sem confirmação";
        }).ToList();

        lines.AddRange(report.Categories.Where(summary => !DashboardCategories.Contains(summary.Category, StringComparer.OrdinalIgnoreCase))
            .Select(summary => $"{summary.Category}: {summary.VerifiedChecks} verificada(s), {summary.Findings} achado(s), {summary.UnavailableChecks + summary.NotVerifiedChecks} sem confirmação"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Text<T>(T? value) => value?.ToString() is { Length: > 0 } text ? text : "Indisponível";
    private static string BoolText(bool? value) => value switch { true => "Ativado", false => "Desativado", _ => "Indisponível" };
    private static string Join(IReadOnlyList<string> addresses) => addresses.Count == 0 ? "Nenhum endereço encontrado" : string.Join(" · ", addresses);
    private static string FormatDuration(TimeSpan duration) => duration.TotalSeconds < 1 ? "menos de 1 segundo" : $"{duration.TotalSeconds.ToString("N1", BrazilianCulture)} s";
}

internal sealed record HomeDashboardReportDisplay(
    string HealthScore,
    string HealthScoreDescription,
    string CriticalProblemsText,
    string WarningsText,
    string DiagnosticDurationText,
    string CategoriesSummary,
    string FindingsSummary);

internal sealed record HomeDashboardInventoryDisplay(
    string ComputerName,
    string ManufacturerModel,
    string SerialNumber,
    string OperatingSystem,
    string Processor,
    string Memory,
    string Graphics,
    string Disks,
    string Bios,
    string Firmware,
    string Tpm,
    string SecureBoot,
    string UserAndDomain,
    string Uptime,
    string IPv4,
    string IPv6,
    string NetworkAdapters);
