using System.Globalization;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

/// <summary>Produz o estado textual exibido no dashboard do Home a partir de um relatório e inventário já redigidos.</summary>
public static class HomeDashboardFormatter
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] DashboardCategories = ["Sistema", "Drivers", "Hardware", "Rede", "Segurança"];

    public static HomeDashboardDisplayState Format(DiagnosticReport? report, TimeSpan overallDuration, ComputerInventory? inventory = null)
    {
        inventory = inventory is null ? null : DiagnosticPrivacyRedactor.RedactInventory(inventory);
        report = report is null ? null : DiagnosticPrivacyRedactor.RedactReport(report, inventory);

        var diagnosticDurationText = FormatDuration(overallDuration);

        if (report is null)
        {
            return new HomeDashboardDisplayState(
                ComputerName: inventory is null ? "Não coletado" : Text(inventory.ComputerName),
                ManufacturerModel: inventory is null ? "Não coletado" : $"{Text(inventory.Manufacturer)} · {Text(inventory.Model)}",
                SerialNumber: inventory is null ? "Não coletado" : Text(inventory.SerialNumber),
                OperatingSystem: inventory is null ? "Não coletado" : $"{Text(inventory.OperatingSystem.Name)} · versão {Text(inventory.OperatingSystem.Version)} · build {Text(inventory.OperatingSystem.Build)}",
                Processor: inventory is null ? "Não coletado" : $"{Text(inventory.Processor.Name)} · {Text(inventory.Processor.Cores)} núcleos / {Text(inventory.Processor.LogicalProcessors)} processadores lógicos",
                Memory: inventory is null ? "Não coletado" : (inventory.InstalledMemoryBytes is ulong missingReportBytes ? $"{missingReportBytes / 1_073_741_824d:N1} GB" : "Indisponível"),
                Graphics: inventory is null ? "Não coletado" : (inventory.GraphicsAdapters.Count == 0 ? "Indisponível" : string.Join(Environment.NewLine, inventory.GraphicsAdapters.Select(item => item.MemoryBytes is ulong gpuBytes ? $"{item.Name} · {gpuBytes / 1_073_741_824d:N1} GB" : item.Name))),
                Disks: inventory is null ? "Não coletado" : BuildDiskSummary(inventory),
                Bios: inventory is null ? "Não coletado" : $"{Text(inventory.Bios.Manufacturer)} · {Text(inventory.Bios.Version)} · série {Text(inventory.Bios.SerialNumber)}",
                Firmware: inventory is null ? "Não coletado" : Text(inventory.FirmwareType),
                Tpm: inventory is null ? "Não coletado" : (inventory.Tpm.IsPresent switch { true => $"Presente · versão {Text(inventory.Tpm.SpecificationVersion)} · fabricante {Text(inventory.Tpm.Manufacturer)} · habilitado {BoolText(inventory.Tpm.IsEnabled)} · ativado {BoolText(inventory.Tpm.IsActivated)}", false => "Não detectado", _ => "Indisponível" }),
                SecureBoot: inventory is null ? "Não coletado" : BoolText(inventory.SecureBootEnabled),
                UserAndDomain: inventory is null ? "Não coletado" : $"{Text(inventory.UserName)} · domínio/grupo {Text(inventory.Domain)}",
                Uptime: inventory is null ? "Não coletado" : (inventory.OperatingSystem.Uptime is TimeSpan missingReportUptime ? $"{missingReportUptime.Days}d {missingReportUptime.Hours}h {missingReportUptime.Minutes}min" : "Indisponível"),
                IPv4: inventory is null ? "Nenhum endereço encontrado" : Join(inventory.IPv4Addresses),
                IPv6: inventory is null ? "Nenhum endereço encontrado" : Join(inventory.IPv6Addresses),
                NetworkAdapters: inventory is null ? "Não coletado" : (inventory.NetworkAdapters.Count == 0 ? "Indisponível" : string.Join(Environment.NewLine, inventory.NetworkAdapters.Select(adapter => $"{adapter.Name} · {adapter.Description} · {adapter.Status}"))),
                HealthScore: "Não calculado",
                HealthScoreDescription: "Este registro não contém verificações do Diagnostic Engine. A pontuação é heurística e não representa a saúde global do computador.",
                CriticalProblemsText: "—",
                WarningsText: "—",
                DiagnosticBrief: "Execute uma verificação para receber uma leitura inicial deste computador.",
                CoverageSummary: "Cobertura ainda não calculada.",
                NextStepSummary: "Inicie o diagnóstico local; a coleta é somente de leitura.",
                CategoriesSummary: "Diagnóstico de rede e segurança não incluído; os scanners do Milestone 2 cobrem sistema, drivers e hardware.",
                FindingsSummary: "Sem resultados estruturados neste registro. O inventário foi preservado.",
                DiagnosticDurationText: diagnosticDurationText);
        }

        var healthScore = report.HealthScore is { } score ? score.Value.ToString(CultureInfo.InvariantCulture) : "Não calculado";
        var healthScoreDescription = report.HealthScore is null
            ? "Nenhuma verificação elegível para a pontuação foi confirmada. Eventos do Visualizador de Eventos continuam nos resultados, mas não calculam a nota. A pontuação é heurística e não representa a saúde global do computador."
            : $"Pontuação heurística por tipo distinto de achado dos demais scanners (scanner, categoria e título). Avisos do Visualizador de Eventos continuam nos detalhes, mas não alteram a nota por si sós, pois registros isolados não confirmam um problema ativo. Há {report.VerifiedChecks} verificação(ões) confirmada(s), {report.UnavailableChecks + report.NotVerifiedChecks} não confirmada(s) e {report.Results.Count} resultado(s). Não representa a saúde global do computador.";
        var unconfirmedChecks = report.UnavailableChecks + report.NotVerifiedChecks;
        var diagnosticBrief = report.VerifiedChecks == 0
            ? "Resultado inconclusivo: nenhuma verificação foi confirmada."
            : report.CriticalProblems > 0
                ? $"Prioridade alta: {report.CriticalProblems} achado(s) crítico(s) precisam de revisão."
                : report.Warnings > 0
                    ? $"Atenção: {report.Warnings} aviso(s) foram encontrados nas verificações confirmadas."
                    : "Nenhum achado crítico ou aviso ativo nas verificações confirmadas.";
        var coverageSummary = $"Confirmadas: {report.VerifiedChecks} · Indisponíveis: {report.UnavailableChecks} · Não verificadas: {report.NotVerifiedChecks}";
        var nextStepSummary = report.CriticalProblems > 0
            ? "Revise primeiro as evidências críticas e as orientações manuais correspondentes; nenhuma correção é automática."
            : unconfirmedChecks > 0
                ? "Revise os itens sem confirmação. Eles não indicam saúde; tente novamente ou verifique as permissões da fonte."
                : report.Warnings > 0
                    ? "Revise os avisos e compare as evidências antes de alterar o sistema."
                    : "Consulte as evidências por categoria. Resultado sem alertas não substitui uma revisão completa do sistema.";

        return new HomeDashboardDisplayState(
            ComputerName: inventory is null ? "Não coletado" : Text(inventory.ComputerName),
            ManufacturerModel: inventory is null ? "Não coletado" : $"{Text(inventory.Manufacturer)} · {Text(inventory.Model)}",
            SerialNumber: inventory is null ? "Não coletado" : Text(inventory.SerialNumber),
            OperatingSystem: inventory is null ? "Não coletado" : $"{Text(inventory.OperatingSystem.Name)} · versão {Text(inventory.OperatingSystem.Version)} · build {Text(inventory.OperatingSystem.Build)}",
            Processor: inventory is null ? "Não coletado" : $"{Text(inventory.Processor.Name)} · {Text(inventory.Processor.Cores)} núcleos / {Text(inventory.Processor.LogicalProcessors)} processadores lógicos",
            Memory: inventory is null ? "Não coletado" : (inventory.InstalledMemoryBytes is ulong bytes ? $"{bytes / 1_073_741_824d:N1} GB" : "Indisponível"),
            Graphics: inventory is null ? "Não coletado" : (inventory.GraphicsAdapters.Count == 0 ? "Indisponível" : string.Join(Environment.NewLine, inventory.GraphicsAdapters.Select(item => item.MemoryBytes is ulong gpuBytes ? $"{item.Name} · {gpuBytes / 1_073_741_824d:N1} GB" : item.Name))),
            Disks: inventory is null ? "Não coletado" : BuildDiskSummary(inventory),
            Bios: inventory is null ? "Não coletado" : $"{Text(inventory.Bios.Manufacturer)} · {Text(inventory.Bios.Version)} · série {Text(inventory.Bios.SerialNumber)}",
            Firmware: inventory is null ? "Não coletado" : Text(inventory.FirmwareType),
            Tpm: inventory is null ? "Não coletado" : (inventory.Tpm.IsPresent switch { true => $"Presente · versão {Text(inventory.Tpm.SpecificationVersion)} · fabricante {Text(inventory.Tpm.Manufacturer)} · habilitado {BoolText(inventory.Tpm.IsEnabled)} · ativado {BoolText(inventory.Tpm.IsActivated)}", false => "Não detectado", _ => "Indisponível" }),
            SecureBoot: inventory is null ? "Não coletado" : BoolText(inventory.SecureBootEnabled),
            UserAndDomain: inventory is null ? "Não coletado" : $"{Text(inventory.UserName)} · domínio/grupo {Text(inventory.Domain)}",
            Uptime: inventory is null ? "Não coletado" : (inventory.OperatingSystem.Uptime is TimeSpan span ? $"{span.Days}d {span.Hours}h {span.Minutes}min" : "Indisponível"),
            IPv4: inventory is null ? "Nenhum endereço encontrado" : Join(inventory.IPv4Addresses),
            IPv6: inventory is null ? "Nenhum endereço encontrado" : Join(inventory.IPv6Addresses),
            NetworkAdapters: inventory is null ? "Não coletado" : (inventory.NetworkAdapters.Count == 0 ? "Indisponível" : string.Join(Environment.NewLine, inventory.NetworkAdapters.Select(adapter => $"{adapter.Name} · {adapter.Description} · {adapter.Status}"))),
            HealthScore: healthScore,
            HealthScoreDescription: healthScoreDescription,
            CriticalProblemsText: report.CriticalProblems.ToString(CultureInfo.InvariantCulture),
            WarningsText: report.Warnings.ToString(CultureInfo.InvariantCulture),
            DiagnosticBrief: diagnosticBrief,
            CoverageSummary: coverageSummary,
            NextStepSummary: nextStepSummary,
            CategoriesSummary: FormatCategories(report),
            FindingsSummary: DiagnosticDisplayFormatter.FormatFindings(report, inventory),
            DiagnosticDurationText: diagnosticDurationText);
    }

    private static string BuildDiskSummary(ComputerInventory inventory)
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

public sealed record HomeDashboardDisplayState(
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
    string NetworkAdapters,
    string HealthScore,
    string HealthScoreDescription,
    string CriticalProblemsText,
    string WarningsText,
    string DiagnosticBrief,
    string CoverageSummary,
    string NextStepSummary,
    string CategoriesSummary,
    string FindingsSummary,
    string DiagnosticDurationText);

