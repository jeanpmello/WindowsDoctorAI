using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Repair;

namespace WindowsDoctorAI.App;

/// <summary>Apresenta resumo real do engine e preserva os campos de inventário do Milestone 1.</summary>
internal partial class HomeViewModel(
    RunComputerInventoryDiagnosticUseCase runDiagnostic,
    IDiagnosticRunRepository history,
    IKnowledgeRepository knowledgeRepository,
    KnowledgeJsonImporter knowledgeImporter,
    DiagnosticAssessmentService assessmentService,
    CbsLogImportService cbsLogImportService,
    IBackupSetCatalogSource backupSetCatalogSource,
    ILogger<HomeViewModel> logger,
    RepairProposalBuilder? repairProposalBuilder = null,
    RepairEngine? repairEngine = null) : ObservableObject
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] DashboardCategories = ["Sistema", "Drivers", "Hardware", "Rede", "Segurança"];

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isLoadingHistory;
    [ObservableProperty] private string _statusMessage = "Inicie um diagnóstico para coletar o inventário e executar as verificações locais.";
    [ObservableProperty] private string _lastDiagnosticText = "Nenhum diagnóstico anterior carregado.";
    [ObservableProperty] private string _healthScore = "Não calculado";
    [ObservableProperty] private string _healthScoreDescription = "A pontuação é heurística e não representa a saúde global do computador; só aparece após evidência observável.";
    [ObservableProperty] private string _criticalProblemsText = "—";
    [ObservableProperty] private string _warningsText = "—";
    [ObservableProperty] private string _diagnosticDurationText = "—";
    [ObservableProperty] private string _categoriesSummary = "As categorias serão preenchidas após a execução.";
    [ObservableProperty] private string _findingsSummary = "Nenhum resultado carregado nesta sessão.";
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
    [ObservableProperty] private bool _isLoadingBackupSetCatalog;
    [ObservableProperty] private string _backupSetCatalogStatusText = "Catálogo não consultado. Selecione Atualizar para listar metadados temporários.";
    [ObservableProperty] private IReadOnlyList<BackupSetCatalogDisplayItem> _backupSetCatalogEntries = Array.Empty<BackupSetCatalogDisplayItem>();
    [ObservableProperty] private IReadOnlyList<RepairProposalDisplayItem> _repairProposals = Array.Empty<RepairProposalDisplayItem>();
    [ObservableProperty] private string _repairProposalStatus = "Nenhum plano tipado disponível nesta sessão.";
    [ObservableProperty] private string _manualGuidanceStatus = "Nenhuma execução diagnóstica carregada para avaliar orientações manuais.";
    [ObservableProperty] private IReadOnlyList<ManualGuidanceFinding> _manualGuidanceFindings = Array.Empty<ManualGuidanceFinding>();

    private string? _pendingPackageJson;
    private DiagnosticRun? _currentRun;
    private bool _displayedRunIsPrevious;
    private CancellationTokenSource? _backupSetCatalogCancellation;

    private bool CanRefreshBackupSetCatalog() => !IsLoadingBackupSetCatalog;
    private bool CanCancelBackupSetCatalog() => IsLoadingBackupSetCatalog;

    partial void OnIsLoadingBackupSetCatalogChanged(bool value)
    {
        RefreshBackupSetCatalogCommand.NotifyCanExecuteChanged();
        CancelBackupSetCatalogCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsScanningChanged(bool value)
    {
        StartDiagnosticCommand.NotifyCanExecuteChanged();
        ConsentToRepairPlanCommand.NotifyCanExecuteChanged();
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

    [RelayCommand(CanExecute = nameof(CanRefreshBackupSetCatalog))]
    private async Task RefreshBackupSetCatalogAsync()
    {
        using var cancellationSource = new CancellationTokenSource();
        _backupSetCatalogCancellation = cancellationSource;
        IsLoadingBackupSetCatalog = true;
        BackupSetCatalogEntries = Array.Empty<BackupSetCatalogDisplayItem>();
        BackupSetCatalogStatusText = "Consultando o catálogo local somente de leitura...";
        try
        {
            var catalog = await backupSetCatalogSource.GetCatalogAsync(cancellationSource.Token).ConfigureAwait(true);
            if (catalog is null || catalog.BackupSets is null)
            {
                BackupSetCatalogStatusText = "Resposta inválida; nenhum metadado foi exibido.";
                return;
            }

            switch (catalog.Status)
            {
                case BackupSetCatalogStatus.Available when IsSafeCatalog(catalog.BackupSets):
                    BackupSetCatalogEntries = catalog.BackupSets
                        .OrderByDescending(entry => entry.BackupTimeUtc)
                        .Select(ToCatalogDisplayItem)
                        .ToArray();
                    BackupSetCatalogStatusText = $"{BackupSetCatalogEntries.Count} versão(ões) listada(s), não verificadas. Nenhum arquivo foi lido e nenhuma restauração foi iniciada.";
                    break;
                case BackupSetCatalogStatus.NoBackupSets when catalog.BackupSets.Count == 0:
                    BackupSetCatalogStatusText = "Nenhuma versão retornada por esta consulta. Isso não exclui outras fontes de backup.";
                    break;
                case BackupSetCatalogStatus.ModuleUnavailable:
                    BackupSetCatalogStatusText = "Windows Server Backup não está disponível neste ambiente.";
                    break;
                case BackupSetCatalogStatus.AccessDenied:
                    BackupSetCatalogStatusText = "A consulta foi negada para a conta atual. Nenhuma elevação foi solicitada.";
                    break;
                case BackupSetCatalogStatus.TooManyBackupSets:
                    BackupSetCatalogStatusText = $"O catálogo excede o limite de {BackupSetCatalogLimits.MaximumBackupSets} versões; a lista foi descartada.";
                    break;
                case BackupSetCatalogStatus.TimedOut:
                    BackupSetCatalogStatusText = "A consulta expirou e foi encerrada; tente novamente quando desejar.";
                    break;
                case BackupSetCatalogStatus.InvalidResponse:
                    BackupSetCatalogStatusText = "Resposta inválida ou fora dos limites; nenhum metadado foi exibido.";
                    break;
                default:
                    BackupSetCatalogStatusText = "Catálogo indisponível; a consulta não forneceu metadados utilizáveis.";
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            BackupSetCatalogEntries = Array.Empty<BackupSetCatalogDisplayItem>();
            BackupSetCatalogStatusText = "Consulta cancelada; nenhum metadado foi mantido.";
        }
        catch (Exception)
        {
            BackupSetCatalogEntries = Array.Empty<BackupSetCatalogDisplayItem>();
            BackupSetCatalogStatusText = "Não foi possível consultar o catálogo; detalhes internos foram omitidos.";
        }
        finally
        {
            _backupSetCatalogCancellation = null;
            IsLoadingBackupSetCatalog = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelBackupSetCatalog))]
    private void CancelBackupSetCatalog() => _backupSetCatalogCancellation?.Cancel();

    private static bool IsSafeCatalog(IReadOnlyList<BackupSetCatalogEntry> entries)
    {
        if (entries.Count is < 1 or > BackupSetCatalogLimits.MaximumBackupSets)
            return false;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        return entries.All(entry => entry is not null &&
            !string.IsNullOrWhiteSpace(entry.VersionId) &&
            entry.VersionId.Length <= BackupSetCatalogLimits.MaximumVersionIdCharacters &&
            entry.VersionId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.') &&
            seenIds.Add(entry.VersionId) &&
            entry.BackupTimeUtc.Offset == TimeSpan.Zero &&
            Enum.IsDefined(entry.BackupType) &&
            (entry.VolumeCount is null or >= 0 and <= BackupSetCatalogLimits.MaximumVolumeCount));
    }

    private static BackupSetCatalogDisplayItem ToCatalogDisplayItem(BackupSetCatalogEntry entry) => new(
        entry.VersionId,
        entry.BackupTimeUtc.UtcDateTime.ToString("dd/MM/yyyy HH:mm:ss 'UTC'", BrazilianCulture),
        entry.BackupType switch
        {
            BackupSetType.Full => "Completo",
            BackupSetType.Incremental => "Incremental",
            BackupSetType.Differential => "Diferencial",
            _ => "Outro/indeterminado"
        },
        entry.VolumeCount is int count ? $"{count} volume(s)" : "Quantidade de volumes não informada",
        "Não verificado");

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
            await RefreshRepairProposalsAsync(_currentRun);
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

    private async Task RefreshManualGuidanceAsync(DiagnosticRun? run, CancellationToken cancellationToken = default)
    {
        ManualGuidanceFindings = Array.Empty<ManualGuidanceFinding>();
        if (run is null)
        {
            ManualGuidanceStatus = "Nenhuma execução diagnóstica carregada para avaliar orientações manuais.";
            return;
        }

        try
        {
            var assessment = await assessmentService.CreateManualGuidanceAssessmentAsync(run, cancellationToken).ConfigureAwait(true);
            ManualGuidanceStatus = assessment.StatusText;
            ManualGuidanceFindings = assessment.Findings;
        }
        catch (Exception)
        {
            ManualGuidanceFindings = Array.Empty<ManualGuidanceFinding>();
            ManualGuidanceStatus = "Avaliação incompleta: não foi possível verificar regras ou evidência; nenhuma conclusão sobre a condição do sistema é possível.";
            logger.LogWarning("A avaliação temporária de orientações manuais falhou; detalhes omitidos por privacidade.");
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
            StatusMessage = _displayedRunIsPrevious
                ? "Relatório do diagnóstico anterior preparado. A tentativa mais recente falhou; este relatório não inclui a tentativa malsucedida."
                : "Relatório HTML preparado localmente. Escolha onde salvar o arquivo.";
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
        StatusMessage = _displayedRunIsPrevious
            ? "Relatório do diagnóstico anterior salvo localmente. A tentativa mais recente falhou; verifique evidências antes de compartilhá-lo."
            : "Relatório HTML salvo localmente no destino escolhido. Verifique evidências antes de compartilhá-lo.";

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
        RepairProposals = Array.Empty<RepairProposalDisplayItem>();
        RepairProposalStatus = "Planos anteriores invalidados; aguardando um novo diagnóstico.";
        ManualGuidanceFindings = Array.Empty<ManualGuidanceFinding>();
        ManualGuidanceStatus = "Aguardando nova avaliação de orientações manuais; dados da execução anterior foram removidos desta seção.";
        ResetCbsLogAnalysis("A observação CBS é independente do diagnóstico e não será associada a evento algum.");
        UpdateCbsLogCommandState();
        _displayedRunIsPrevious = _currentRun is not null;
        IsScanning = true;
        StatusMessage = "Coletando inventário e verificações locais somente de leitura. Nenhuma correção será aplicada.";
        try
        {
            var outcome = await runDiagnostic.ExecuteAsync();
            _currentRun = outcome.Run;
            _displayedRunIsPrevious = false;
            await RefreshManualGuidanceAsync(outcome.Run);
            await RefreshRepairProposalsAsync(outcome.Run);
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
            LastDiagnosticText = _displayedRunIsPrevious && _currentRun is not null
                ? $"Nova execução falhou; dados exibidos são do diagnóstico anterior concluído às {_currentRun.CompletedAtUtc.ToLocalTime():G}."
                : "A tentativa mais recente falhou; nenhum diagnóstico concluído está carregado nesta sessão.";
            ManualGuidanceFindings = Array.Empty<ManualGuidanceFinding>();
            ManualGuidanceStatus = "Avaliação incompleta: a nova execução diagnóstica falhou. Nenhuma conclusão sobre a condição do computador foi produzida.";
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
        IsLoadingHistory = true;
        StatusMessage = "Carregando histórico local...";
        LastDiagnosticText = "Carregando histórico local...";
        FindingsSummary = "Carregando resultados do histórico local...";
        ManualGuidanceFindings = Array.Empty<ManualGuidanceFinding>();
        ManualGuidanceStatus = "Carregando orientações manuais da execução local...";
        RepairProposals = Array.Empty<RepairProposalDisplayItem>();
        RepairProposalStatus = "Carregando evidência atual; planos antigos estão indisponíveis.";
        try
        {
            ResetCbsLogAnalysis("Selecione manualmente um CBS.log para uma observação offline e isolada.");
            UpdateCbsLogCommandState();
            await RefreshKnowledgeBaseStatusAsync(cancellationToken);
            var latest = await history.GetLatestAsync(cancellationToken);
            if (latest is null)
            {
                _currentRun = null;
                _displayedRunIsPrevious = false;
                ManualGuidanceStatus = "Nenhuma execução diagnóstica carregada para avaliar orientações manuais.";
                RepairProposalStatus = "Nenhum plano disponível: não há uma execução diagnóstica atual salva para revalidar.";
                LastDiagnosticText = "Nenhum diagnóstico anterior encontrado no histórico local.";
                FindingsSummary = "Nenhum resultado anterior está disponível no histórico local.";
                StatusMessage = "Nenhum diagnóstico anterior encontrado no histórico local. Execute um diagnóstico para ver resultados.";
                return;
            }
            _currentRun = latest;
            _displayedRunIsPrevious = false;
            await RefreshManualGuidanceAsync(latest, cancellationToken);
            await RefreshRepairProposalsAsync(latest, cancellationToken);
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
            LastDiagnosticText = "Histórico local indisponível.";
            FindingsSummary = "Não foi possível carregar resultados do histórico local.";
            ManualGuidanceFindings = Array.Empty<ManualGuidanceFinding>();
            ManualGuidanceStatus = "Avaliação incompleta: o histórico local está indisponível; nenhuma conclusão sobre a condição do sistema é possível.";
            StatusMessage = DiagnosticPrivacyMessages.HistoryLoadFailure(exception);
        }
        finally
        {
            IsLoadingHistory = false;
        }
    }

    private void DisplayReport(DiagnosticReport? report, TimeSpan overallDuration, ComputerInventory? inventory = null)
    {
        DiagnosticDurationText = FormatDuration(overallDuration);
        report = DiagnosticPrivacyRedactor.RedactReport(report, inventory);
        if (report is null)
        {
            HealthScore = "Não calculado";
            HealthScoreDescription = "Este registro não contém verificações do Diagnostic Engine. A pontuação é heurística e não representa a saúde global do computador.";
            CriticalProblemsText = "—";
            WarningsText = "—";
            CategoriesSummary = "Diagnóstico de rede e segurança não incluído; os scanners do Milestone 2 cobrem sistema, drivers e hardware.";
            FindingsSummary = "Sem resultados estruturados neste registro. O inventário foi preservado.";
            return;
        }

        HealthScore = report.HealthScore is { } score ? score.Value.ToString(CultureInfo.InvariantCulture) : "Não calculado";
        HealthScoreDescription = report.HealthScore is null
            ? "Nenhuma verificação foi confirmada; itens indisponíveis ou não verificados não contam. A pontuação é heurística e não representa a saúde global do computador."
            : $"Pontuação heurística baseada em {report.VerifiedChecks} verificação(ões) observada(s); {report.UnavailableChecks + report.NotVerifiedChecks} indisponível(is)/não verificada(s). Não representa a saúde global do computador.";
        CriticalProblemsText = report.CriticalProblems.ToString(CultureInfo.InvariantCulture);
        WarningsText = report.Warnings.ToString(CultureInfo.InvariantCulture);
        CategoriesSummary = FormatCategories(report);
        FindingsSummary = DiagnosticDisplayFormatter.FormatFindings(report, inventory);
    }

    private async Task RefreshRepairProposalsAsync(DiagnosticRun? run, CancellationToken cancellationToken = default)
    {
        RepairProposals = Array.Empty<RepairProposalDisplayItem>();
        if (run?.Report is null || repairProposalBuilder is null || repairEngine is null)
        {
            RepairProposalStatus = "Nenhum plano tipado disponível. Procedimentos manuais não são convertidos em propostas.";
            return;
        }

        try
        {
            var latest = await history.GetLatestAsync(cancellationToken).ConfigureAwait(true);
            if (latest?.Id != run.Id)
            {
                RepairProposalStatus = "Planos indisponíveis: a execução atual não está salva para revalidar a evidência. Ative o histórico se desejar esse recurso.";
                return;
            }

            var rules = await knowledgeRepository.GetLatestRulesAsync(cancellationToken).ConfigureAwait(true);
            var registeredIds = repairEngine.GetProposals().Select(proposal => proposal.Id).ToHashSet(StringComparer.Ordinal);
            var build = repairProposalBuilder.Build(run, rules);
            if (build.EvidenceChangedDuringBuild)
            {
                RepairProposalStatus = "Snapshot de evidência invalidado por novo diagnóstico/importação; execute um novo diagnóstico antes de consentir.";
                return;
            }
            if (build.DiagnosticRunNotCurrent)
            {
                RepairProposalStatus = "O histórico carregado não é um diagnóstico atual desta sessão; execute novo diagnóstico antes de consentir.";
                return;
            }
            var proposals = build.Proposals
                .Where(proposal => registeredIds.Contains(proposal.Id))
                .Select(RepairProposalDisplayItem.From)
                .ToArray();
            RepairProposals = proposals;
            RepairProposalStatus = proposals.Length == 0
                ? build.AmbiguousFindingIdentities.Count > 0
                    ? $"{build.AmbiguousFindingIdentities.Count} identidade(s) de finding ambígua(s); as propostas correspondentes foram bloqueadas. Nenhum plano duplicado é acionável."
                    : "Nenhum plano tipado está allowlistado para estes achados. Procedimentos ManualOnly continuam manuais; nenhum comando será sugerido ou executado."
                : "Planos simulados allowlistados. Revise todos os detalhes; somente o botão explícito emite consentimento one-shot. Nenhum reparo real está habilitado nesta versão.";
        }
        catch (Exception)
        {
            RepairProposals = Array.Empty<RepairProposalDisplayItem>();
            RepairProposalStatus = "Não foi possível revalidar propostas; nenhuma ação está disponível.";
            logger.LogWarning("A avaliação local de propostas foi bloqueada; detalhes omitidos por privacidade.");
        }
    }

    private bool CanConsentToRepairPlan(RepairProposalDisplayItem? item) =>
        !IsScanning && item is not null && repairEngine is not null && repairProposalBuilder is not null
        && _currentRun?.Id == item.Proposal.DiagnosticRunId
        && RepairProposals.Any(candidate => string.Equals(candidate.Proposal.Id, item.Proposal.Id, StringComparison.Ordinal));

    [RelayCommand(CanExecute = nameof(CanConsentToRepairPlan))]
    private async Task ConsentToRepairPlanAsync(RepairProposalDisplayItem? item)
    {
        if (!CanConsentToRepairPlan(item) || item is null || _currentRun is null
            || repairEngine is null || repairProposalBuilder is null)
            return;

        try
        {
            var rules = await knowledgeRepository.GetLatestRulesAsync().ConfigureAwait(true);
            var latest = await history.GetLatestAsync().ConfigureAwait(true);
            var build = latest?.Id == _currentRun.Id
                ? repairProposalBuilder.Build(_currentRun, rules)
                : null;
            var candidates = build?.Proposals.Where(proposal =>
                    string.Equals(proposal.Id, item.Proposal.Id, StringComparison.Ordinal)
                    && string.Equals(proposal.FindingIdentity, item.Proposal.FindingIdentity, StringComparison.Ordinal)
                    && proposal.RuleVersion == item.Proposal.RuleVersion
                    && proposal.EvidenceGeneration == item.Proposal.EvidenceGeneration
                    && string.Equals(proposal.EvidenceFingerprint, item.Proposal.EvidenceFingerprint, StringComparison.Ordinal))
                .Take(2).ToArray() ?? Array.Empty<RepairProposal>();
            var current = build is { EvidenceChangedDuringBuild: false } && candidates.Length == 1
                ? candidates[0]
                : null;
            if (current is null)
            {
                RepairProposalStatus = build?.AmbiguousFindingIdentities.Contains(item.Proposal.FindingIdentity, StringComparer.Ordinal) == true
                    ? "Finding ambíguo; consentimento não emitido. Nenhuma proposta duplicada é acionável."
                    : build?.DiagnosticRunNotCurrent == true
                        ? "O diagnóstico não é atual nesta sessão; consentimento não emitido. Execute novo diagnóstico."
                        : "Plano ou evidência obsoletos; consentimento não emitido. Execute novamente o diagnóstico.";
                await RefreshRepairProposalsAsync(_currentRun).ConfigureAwait(true);
                return;
            }

            // O consentimento só é criado após este clique e está vinculado ao fingerprint do plano revalidado.
            var consent = RepairConsent.Confirm(current);
            var result = await repairEngine.ExecuteAsync(current.Id, consent).ConfigureAwait(true);
            RepairProposalStatus = result.Status switch
            {
                RepairExecutionStatus.Succeeded => "Simulação concluída e pós-condições verificadas; nenhum reparo de sistema foi executado.",
                RepairExecutionStatus.Inconclusive => "Tentativa inconclusiva; pós-condições não foram verificadas. Nenhum rollback automático será tentado.",
                RepairExecutionStatus.Declined when result.Details.Contains("quarentena", StringComparison.OrdinalIgnoreCase)
                    => "Este finding já alcançou Started em tentativa anterior e permanece em quarentena; execute novo diagnóstico. Nenhuma reconciliação automática foi presumida.",
                RepairExecutionStatus.Declined => "Tentativa bloqueada ou consentimento já utilizado; nenhuma ação adicional foi iniciada.",
                RepairExecutionStatus.Cancelled => "Tentativa cancelada; se já havia começado, o estado pode ser parcial ou inconclusivo. Nenhum rollback automático será tentado.",
                _ => "Tentativa registrada sem execução de reparo de sistema. Consulte a auditoria local."
            };
            RepairProposals = Array.Empty<RepairProposalDisplayItem>();
        }
        catch (Exception)
        {
            RepairProposalStatus = "Não foi possível registrar o resultado. Se a tentativa alcançou Started, trate-a como parcial/inconclusiva: não há reconciliação automática e este plano fica bloqueado até novo diagnóstico.";
            RepairProposals = Array.Empty<RepairProposalDisplayItem>();
            logger.LogWarning("O fluxo de consentimento de proposta falhou; detalhes omitidos por privacidade.");
        }
        finally
        {
            ConsentToRepairPlanCommand.NotifyCanExecuteChanged();
        }
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

internal sealed record BackupSetCatalogDisplayItem(
    string VersionId,
    string BackupTimeText,
    string BackupTypeText,
    string VolumeCountText,
    string VerificationText);

internal sealed record RepairProposalDisplayItem(
    RepairProposal Proposal,
    string RiskText,
    string SourceText,
    string PreconditionsText,
    string PostconditionsText,
    string RollbackSupportText,
    string RollbackPreconditionsText,
    string RollbackPostconditionsText,
    string FingerprintText)
{
    public static RepairProposalDisplayItem From(RepairProposal proposal) => new(
        proposal,
        proposal.Risk switch
        {
            RepairRiskLevel.Low => "Baixo (simulação)",
            RepairRiskLevel.Moderate => "Moderado (simulação)",
            RepairRiskLevel.High => "Alto (simulação)",
            _ => "Desconhecido — bloqueado"
        },
        $"DiagnosticRunId: {proposal.DiagnosticRunId:D}\nGeração de evidência: {proposal.EvidenceGeneration}\nFinding: {proposal.FindingIdentity}\nRegra: {proposal.RuleId} v{proposal.RuleVersion}",
        string.Join(Environment.NewLine, proposal.StructuredPreconditions.Select(condition => "• " + condition.DisplayText)),
        string.Join(Environment.NewLine, proposal.StructuredPostconditions.Select(condition => "• " + condition.DisplayText)),
        proposal.SupportsRollback ? "Sim; ação separada e consentimento próprio." : "Não",
        string.Join(Environment.NewLine, proposal.StructuredRollbackPreconditions.Select(condition => "• " + condition.DisplayText)),
        string.Join(Environment.NewLine, proposal.StructuredRollbackPostconditions.Select(condition => "• " + condition.DisplayText)),
        "SHA-256 do plano: " + RepairConsent.FingerprintFor(proposal));
}
