using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.App;

/// <summary>Apresenta resumo real do engine e preserva os campos de inventário do Milestone 1.</summary>
internal partial class HomeViewModel(
    RunComputerInventoryDiagnosticUseCase runDiagnostic,
    IDiagnosticRunRepository history,
    IKnowledgeRepository knowledgeRepository,
    KnowledgeJsonImporter knowledgeImporter,
    DiagnosticAssessmentService assessmentService,
    CbsLogImportService cbsLogImportService,
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
    [ObservableProperty] private string _knowledgeBaseStatus = "A base de conhecimento ainda não foi verificada.";
    [ObservableProperty] private string _packageVersion = "—";
    [ObservableProperty] private string _packageSource = "—";
    [ObservableProperty] private string _packageRuleCount = "—";
    [ObservableProperty] private string _packageSha256 = "—";
    [ObservableProperty] private string _packageReviewStatus = "Selecione um pacote JSON para validar e revisar os metadados antes da importação.";
    [ObservableProperty] private bool _canImportKnowledgePackage;
    [ObservableProperty] private bool _canExportHtmlReport;
    [ObservableProperty] private bool _canAnalyzeCbsLog;
    [ObservableProperty] private bool _isAnalyzingCbsLog;
    [ObservableProperty] private string _cbsLogAnalysisStatus = "Selecione manualmente um CBS.log para uma observação offline e isolada.";
    [ObservableProperty] private string _cbsLogSource = string.Empty;
    [ObservableProperty] private string _cbsLogSignal = string.Empty;
    [ObservableProperty] private string _cbsLogDisclaimer = string.Empty;
    [ObservableProperty] private bool _isImportingPackage;
    [ObservableProperty] private bool _canSelectKnowledgePackage = true;

    private string? _pendingPackageJson;
    private DiagnosticRun? _currentRun;

    partial void OnIsScanningChanged(bool value)
    {
        StartDiagnosticCommand.NotifyCanExecuteChanged();
        UpdateImportCommandState();
        UpdateCbsLogCommandState();
    }

    partial void OnIsAnalyzingCbsLogChanged(bool value) => UpdateCbsLogCommandState();

    partial void OnIsImportingPackageChanged(bool value)
    {
        CanSelectKnowledgePackage = !value;
        UpdateImportCommandState();
    }

    private bool CanStartDiagnostic() => !IsScanning;
    private bool CanImportKnowledge() => _pendingPackageJson is not null && !IsImportingPackage && !IsScanning;

    private void UpdateImportCommandState()
    {
        CanImportKnowledgePackage = CanImportKnowledge();
        ImportKnowledgePackageCommand.NotifyCanExecuteChanged();
    }

    private void UpdateCbsLogCommandState()
    {
        CanAnalyzeCbsLog = !IsScanning
            && !IsAnalyzingCbsLog;
        AnalyzeCbsLogCommand.NotifyCanExecuteChanged();
    }

    private bool CanAnalyzeCurrentCbsLog() => CanAnalyzeCbsLog;

    private void ResetCbsLogAnalysis(string status)
    {
        CbsLogAnalysisStatus = status;
        CbsLogSource = string.Empty;
        CbsLogSignal = string.Empty;
        CbsLogDisclaimer = string.Empty;
    }

    public Task PreviewKnowledgePackageAsync(string json)
    {
        try
        {
            var preview = knowledgeImporter.Preview(json);
            _pendingPackageJson = json;
            PackageVersion = preview.Version;
            PackageSource = preview.Source;
            PackageRuleCount = preview.RuleCount.ToString(CultureInfo.InvariantCulture);
            PackageSha256 = preview.Sha256;
            PackageReviewStatus = "Pacote válido para revisão. A fonte/autoria não foi verificada; revise os metadados antes de importar.";
            StatusMessage = "Pacote validado sem gravação. A importação só ocorre quando você selecionar Importar.";
        }
        catch (Exception)
        {
            ShowPackagePreviewError("O pacote local foi recusado na validação.");
            logger.LogWarning("O pacote local de conhecimento foi recusado na prévia; detalhes omitidos por privacidade.");
        }

        UpdateImportCommandState();
        return Task.CompletedTask;
    }

    public void ShowPackagePreviewError(string message)
    {
        _pendingPackageJson = null;
        PackageVersion = "—";
        PackageSource = "—";
        PackageRuleCount = "—";
        PackageSha256 = "—";
        PackageReviewStatus = $"Pacote recusado antes da importação: {message}";
        UpdateImportCommandState();
    }

    [RelayCommand(CanExecute = nameof(CanImportKnowledge))]
    private async Task ImportKnowledgePackageAsync()
    {
        if (_pendingPackageJson is not { } json) return;
        IsImportingPackage = true;
        try
        {
            var imported = await knowledgeImporter.ImportAsync(json);
            _pendingPackageJson = null;
            await RefreshKnowledgeBaseStatusAsync();
            PackageReviewStatus = $"Importação concluída: {imported.ImportedRules} regra(s), versão {imported.Version}. Pacote e fonte continuam não verificados quanto à autoria.";
            StatusMessage = "Pacote importado localmente; a importação não autentica a autoria nem comprova as afirmações das regras.";
        }
        catch (Exception)
        {
            _pendingPackageJson = null;
            PackageReviewStatus = "Importação recusada sem gravação parcial; detalhes internos foram omitidos por privacidade.";
            StatusMessage = "O pacote foi recusado. Nenhuma importação parcial foi mantida.";
            logger.LogWarning("O pacote local de conhecimento foi recusado durante a importação; detalhes omitidos por privacidade.");
        }
        finally
        {
            IsImportingPackage = false;
            UpdateImportCommandState();
        }
    }

    private async Task RefreshKnowledgeBaseStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var rules = await knowledgeRepository.GetLatestRulesAsync(cancellationToken);
            KnowledgeBaseStatus = rules.Count == 0
                ? "Base de conhecimento vazia: nenhum pacote importado; não haverá recomendações baseadas em regras."
                : $"{rules.Count} regra(s) disponíveis localmente. Pacotes e fontes declaradas não são autenticados pelo aplicativo.";
        }
        catch (Exception)
        {
            logger.LogWarning("Não foi possível consultar a base de conhecimento local; detalhes omitidos por privacidade.");
            KnowledgeBaseStatus = "Não foi possível verificar o estado da base de conhecimento local.";
        }
    }

    public async Task<string?> CreateCurrentHtmlReportAsync(CancellationToken cancellationToken = default)
    {
        if (_currentRun is null)
        {
            StatusMessage = "Não há uma execução carregada para gerar o relatório.";
            return null;
        }

        try
        {
            var html = await assessmentService.CreateHtmlReportAsync(_currentRun, cancellationToken);
            StatusMessage = "Relatório HTML preparado localmente. Escolha onde salvar o arquivo.";
            return html;
        }
        catch (Exception)
        {
            logger.LogError("Não foi possível gerar o relatório HTML local; detalhes omitidos por privacidade.");
            StatusMessage = "Não foi possível gerar o relatório HTML local.";
            return null;
        }
    }

    public void ReportHtmlSaved(string path) =>
        StatusMessage = "Relatório HTML salvo localmente no destino escolhido. Verifique evidências antes de compartilhá-lo.";

    public void ReportHtmlSaveFailed(string message) =>
        StatusMessage = "Não foi possível salvar o relatório HTML no destino escolhido.";

    [RelayCommand(CanExecute = nameof(CanAnalyzeCurrentCbsLog))]
    private async Task AnalyzeCbsLogAsync()
    {
        IsAnalyzingCbsLog = true;
        ResetCbsLogAnalysis("Aguardando a seleção do arquivo; o conteúdo será analisado somente em memória.");
        try
        {
            var outcome = await cbsLogImportService.ImportAndAnalyzeAsync();
            switch (outcome.Status)
            {
                case CbsLogImportStatus.Cancelled:
                    ResetCbsLogAnalysis("Seleção cancelada; nenhum arquivo foi analisado.");
                    break;
                case CbsLogImportStatus.FileReadFailed:
                    ResetCbsLogAnalysis("O arquivo selecionado está ausente ou inacessível; nenhum conteúdo foi retido.");
                    break;
                case CbsLogImportStatus.FileTooLarge:
                    ResetCbsLogAnalysis($"O arquivo excede o limite de {CbsLogImportService.MaximumFileBytes / 1024 / 1024} MiB; nenhum conteúdo foi retido.");
                    break;
                case CbsLogImportStatus.UnsupportedEncoding:
                    ResetCbsLogAnalysis("Codificação inválida ou não suportada. Use UTF-8 (com ou sem BOM) ou UTF-16 com BOM.");
                    break;
                case CbsLogImportStatus.NoRecognizedMarker:
                    ResetCbsLogAnalysis("Nenhum marcador CBS reconhecido; nenhum resultado foi gerado.");
                    break;
                case CbsLogImportStatus.ObservationAvailable when outcome.MarkerTypes is { Count: > 0 } markerTypes:
                    CbsLogAnalysisStatus = "Observação CBS isolada exibida temporariamente; nenhum achado ou recomendação foi criado.";
                    CbsLogSource = "Origem: CBS.log selecionado manualmente (somente leitura)";
                    CbsLogSignal = "Tipos genéricos: " + string.Join(", ", markerTypes.Select(markerType => markerType switch
                    {
                        CbsMarkerType.ManifestMissing => "marcador de manifesto ausente",
                        CbsMarkerType.FailedToResolvePackage => "marcador de falha ao resolver pacote",
                        _ => "marcador CBS reconhecido"
                    }));
                    CbsLogDisclaimer = "não atribuída ao evento; não confirma causa; não acionável.";
                    break;
                default:
                    ResetCbsLogAnalysis("Não foi possível produzir um resultado CBS seguro.");
                    break;
            }
        }
        finally
        {
            IsAnalyzingCbsLog = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartDiagnostic))]
    private async Task StartDiagnosticAsync()
    {
        ResetCbsLogAnalysis("A observação CBS é independente do diagnóstico e não será associada a evento algum.");
        UpdateCbsLogCommandState();
        IsScanning = true;
        StatusMessage = "Coletando inventário e verificações locais somente de leitura. Nenhuma correção será aplicada.";
        try
        {
            var outcome = await runDiagnostic.ExecuteAsync();
            _currentRun = outcome.Run;
            UpdateCbsLogCommandState();
            CanExportHtmlReport = true;
            DisplayInventory(outcome.Run.Inventory);
            DisplayReport(outcome.Run.Report, outcome.Run.Duration, outcome.Run.Inventory);
            LastDiagnosticText = $"Concluído às {outcome.Run.CompletedAtUtc.ToLocalTime():G}";
            StatusMessage = outcome.PersistenceWarning ?? (outcome.HistorySaved
                ? "Diagnóstico concluído e salvo no histórico local."
                : "Diagnóstico concluído. O histórico está desativado nas Configurações.");
        }
        catch (Exception exception)
        {
            logger.LogError("A execução do diagnóstico falhou; detalhes omitidos por privacidade.");
            StatusMessage = DiagnosticPrivacyMessages.DiagnosticFailure(exception);
        }
        finally
        {
            IsScanning = false;
            UpdateCbsLogCommandState();
        }
    }

    public async Task LoadLatestAsync(CancellationToken cancellationToken = default)
    {
        ResetCbsLogAnalysis("Selecione manualmente um CBS.log para uma observação offline e isolada.");
        UpdateCbsLogCommandState();
        await RefreshKnowledgeBaseStatusAsync(cancellationToken);
        try
        {
            var latest = await history.GetLatestAsync(cancellationToken);
            if (latest is null) return;
            _currentRun = latest;
            CanExportHtmlReport = true;
            DisplayInventory(latest.Inventory);
            DisplayReport(latest.Report, latest.Duration, latest.Inventory);
            LastDiagnosticText = $"Concluído às {latest.CompletedAtUtc.ToLocalTime():G}";
            StatusMessage = latest.Report is null
                ? "Inventário legado do Milestone 1 carregado. Este registro não contém resultados do Diagnostic Engine; score não calculado."
                : "Último diagnóstico carregado do histórico local.";
        }
        catch (Exception exception)
        {
            logger.LogWarning("O último diagnóstico não pôde ser carregado do histórico local; detalhes omitidos por privacidade.");
            StatusMessage = DiagnosticPrivacyMessages.HistoryLoadFailure(exception);
        }
    }

    private void DisplayReport(DiagnosticReport? report, TimeSpan overallDuration, ComputerInventory? inventory = null)
    {
        DiagnosticDurationText = FormatDuration(overallDuration);
        report = DiagnosticPrivacyRedactor.RedactReport(report, inventory);
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
        FindingsSummary = DiagnosticDisplayFormatter.FormatFindings(report, inventory);
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

    private void DisplayInventory(ComputerInventory inventory)
    {
        inventory = DiagnosticPrivacyRedactor.RedactInventory(inventory);
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

    private static string Text<T>(T? value) => value?.ToString() is { Length: > 0 } text ? text : "Indisponível";
    private static string BoolText(bool? value) => value switch { true => "Ativado", false => "Desativado", _ => "Indisponível" };
    private static string Join(IReadOnlyList<string> addresses) => addresses.Count == 0 ? "Nenhum endereço encontrado" : string.Join(" · ", addresses);
    private static string FormatDuration(TimeSpan duration) => duration.TotalSeconds < 1 ? "menos de 1 segundo" : $"{duration.TotalSeconds.ToString("N1", BrazilianCulture)} s";
}
