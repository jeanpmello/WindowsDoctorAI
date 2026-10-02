using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.App;

/// <summary>Apresenta resumo real do engine e preserva os campos de inventário do Milestone 1.</summary>
public partial class HomeViewModel(
    RunComputerInventoryDiagnosticUseCase runDiagnostic,
    IDiagnosticRunRepository history,
    ILogger<HomeViewModel> logger) : ObservableObject
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] DashboardCategories = ["Sistema", "Drivers", "Hardware", "Rede", "Segurança"];

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusMessage = "Inicie um diagnóstico para coletar o inventário e executar as verificações locais.";
    [ObservableProperty] private string _lastDiagnosticText = "Nenhum diagnóstico registrado nesta sessão.";
    [ObservableProperty] private string _healthScore = "Não calculado";
    [ObservableProperty] private string _healthScoreDescription = "O score aparece quando pelo menos uma verificação produz evidência observável.";
    [ObservableProperty] private string _criticalProblemsText = "—";
    [ObservableProperty] private string _warningsText = "—";
    [ObservableProperty] private string _diagnosticDurationText = "—";
    [ObservableProperty] private string _categoriesSummary = "As categorias serão preenchidas após a execução.";
    [ObservableProperty] private string _findingsSummary = "Nenhuma execução nesta sessão.";
    [ObservableProperty] private string _computerName = "Não coletado";
    [ObservableProperty] private string _manufacturerModel = "Não coletado";
    [ObservableProperty] private string _serialNumber = "Não coletado";
    [ObservableProperty] private string _operatingSystem = "Não coletado";
    [ObservableProperty] private string _processor = "Não coletado";
    [ObservableProperty] private string _memory = "Não coletado";
    [ObservableProperty] private string _graphics = "Não coletado";
    [ObservableProperty] private string _disks = "Não coletado";
    [ObservableProperty] private string _bios = "Não coletado";
    [ObservableProperty] private string _firmware = "Não coletado";
    [ObservableProperty] private string _tpm = "Não coletado";
    [ObservableProperty] private string _secureBoot = "Não coletado";
    [ObservableProperty] private string _userAndDomain = "Não coletado";
    [ObservableProperty] private string _uptime = "Não coletado";
    [ObservableProperty] private string _iPv4 = "Não coletado";
    [ObservableProperty] private string _iPv6 = "Não coletado";
    [ObservableProperty] private string _networkAdapters = "Não coletado";

    partial void OnIsScanningChanged(bool value) => StartDiagnosticCommand.NotifyCanExecuteChanged();

    private bool CanStartDiagnostic() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanStartDiagnostic))]
    private async Task StartDiagnosticAsync()
    {
        IsScanning = true;
        StatusMessage = "Coletando inventário e verificações locais somente de leitura. Nenhuma correção será aplicada.";
        try
        {
            var outcome = await runDiagnostic.ExecuteAsync();
            DisplayInventory(outcome.Run.Inventory);
            DisplayReport(outcome.Run.Report, outcome.Run.Duration);
            LastDiagnosticText = $"Concluído às {outcome.Run.CompletedAtUtc.ToLocalTime():G}";
            StatusMessage = outcome.PersistenceWarning ?? (outcome.HistorySaved
                ? "Diagnóstico concluído e salvo no histórico local."
                : "Diagnóstico concluído. O histórico está desativado nas Configurações.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "A execução do diagnóstico falhou.");
            StatusMessage = $"Não foi possível concluir o diagnóstico: {exception.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task LoadLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var latest = await history.GetLatestAsync(cancellationToken);
            if (latest is null) return;
            DisplayInventory(latest.Inventory);
            DisplayReport(latest.Report, latest.Duration);
            LastDiagnosticText = $"Concluído às {latest.CompletedAtUtc.ToLocalTime():G}";
            StatusMessage = latest.Report is null
                ? "Inventário legado do Milestone 1 carregado. Este registro não contém resultados do Diagnostic Engine; score não calculado."
                : "Último diagnóstico carregado do histórico local.";
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "O último diagnóstico não pôde ser carregado do histórico local.");
            StatusMessage = "Não foi possível carregar o histórico local. Você ainda pode iniciar um novo diagnóstico.";
        }
    }

    private void DisplayReport(DiagnosticReport? report, TimeSpan overallDuration)
    {
        DiagnosticDurationText = FormatDuration(overallDuration);
        if (report is null)
        {
            HealthScore = "Não calculado";
            HealthScoreDescription = "Este registro não contém verificações do Diagnostic Engine.";
            CriticalProblemsText = "—";
            WarningsText = "—";
            CategoriesSummary = "Diagnóstico de rede e segurança não incluído; os scanners do Milestone 2 cobrem sistema, drivers e hardware.";
            FindingsSummary = "Sem resultados estruturados neste registro. O inventário foi preservado.";
            return;
        }

        HealthScore = report.HealthScore is { } score ? score.Value.ToString(CultureInfo.InvariantCulture) : "Não calculado";
        HealthScoreDescription = report.HealthScore is null
            ? "Nenhuma verificação foi confirmada; itens indisponíveis ou não verificados não contam como saúde."
            : $"Baseado em {report.VerifiedChecks} verificação(ões) observada(s); {report.UnavailableChecks + report.NotVerifiedChecks} indisponível(is)/não verificada(s).";
        CriticalProblemsText = report.CriticalProblems.ToString(CultureInfo.InvariantCulture);
        WarningsText = report.Warnings.ToString(CultureInfo.InvariantCulture);
        CategoriesSummary = FormatCategories(report);
        FindingsSummary = FormatFindings(report);
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

    private static string FormatFindings(DiagnosticReport report)
    {
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
        var remainder = findings.Length > displayLimit ? $"{Environment.NewLine}Exibindo {displayLimit} de {findings.Length} achados. O relatório contém a lista completa." : string.Empty;
        return string.Join(Environment.NewLine + Environment.NewLine, lines) + remainder;
    }

    private void DisplayInventory(ComputerInventory inventory)
    {
        ComputerName = Text(inventory.ComputerName);
        ManufacturerModel = $"{Text(inventory.Manufacturer)} · {Text(inventory.Model)}";
        SerialNumber = Text(inventory.SerialNumber);
        OperatingSystem = $"{Text(inventory.OperatingSystem.Name)} · versão {Text(inventory.OperatingSystem.Version)} · build {Text(inventory.OperatingSystem.Build)}";
        Processor = $"{Text(inventory.Processor.Name)} · {Text(inventory.Processor.Cores)} núcleos / {Text(inventory.Processor.LogicalProcessors)} processadores lógicos";
        Memory = inventory.InstalledMemoryBytes is ulong bytes ? $"{bytes / 1_073_741_824d:N1} GB" : "Indisponível";
        Graphics = inventory.GraphicsAdapters.Count == 0 ? "Indisponível" : string.Join(Environment.NewLine, inventory.GraphicsAdapters.Select(item => item.MemoryBytes is ulong gpuBytes
            ? $"{item.Name} · {gpuBytes / 1_073_741_824d:N1} GB"
            : item.Name));
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
        Disks = diskSummaries.Length == 0 ? "Indisponível" : string.Join(Environment.NewLine, diskSummaries);
        Bios = $"{Text(inventory.Bios.Manufacturer)} · {Text(inventory.Bios.Version)} · série {Text(inventory.Bios.SerialNumber)}";
        Firmware = Text(inventory.FirmwareType);
        Tpm = inventory.Tpm.IsPresent switch
        {
            true => $"Presente · versão {Text(inventory.Tpm.SpecificationVersion)} · fabricante {Text(inventory.Tpm.Manufacturer)} · habilitado {BoolText(inventory.Tpm.IsEnabled)} · ativado {BoolText(inventory.Tpm.IsActivated)}",
            false => "Não detectado",
            _ => "Indisponível"
        };
        SecureBoot = BoolText(inventory.SecureBootEnabled);
        UserAndDomain = $"{Text(inventory.UserName)} · domínio/grupo {Text(inventory.Domain)}";
        Uptime = inventory.OperatingSystem.Uptime is TimeSpan span ? $"{span.Days}d {span.Hours}h {span.Minutes}min" : "Indisponível";
        IPv4 = Join(inventory.IPv4Addresses);
        IPv6 = Join(inventory.IPv6Addresses);
        NetworkAdapters = inventory.NetworkAdapters.Count == 0 ? "Indisponível" : string.Join(Environment.NewLine,
            inventory.NetworkAdapters.Select(adapter => $"{adapter.Name} · {adapter.Description} · {adapter.Status}"));
    }

    private static string SeverityText(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Critical => "Crítico",
        DiagnosticSeverity.Warning => "Aviso",
        _ => "Informativo"
    };

    private static string Text<T>(T? value) => value?.ToString() is { Length: > 0 } text ? text : "Indisponível";
    private static string BoolText(bool? value) => value switch { true => "Ativado", false => "Desativado", _ => "Indisponível" };
    private static string Join(IReadOnlyList<string> addresses) => addresses.Count == 0 ? "Nenhum endereço encontrado" : string.Join(" · ", addresses);
    private static string FormatDuration(TimeSpan duration) => duration.TotalSeconds < 1 ? "menos de 1 segundo" : $"{duration.TotalSeconds.ToString("N1", BrazilianCulture)} s";
}
