using Microsoft.Extensions.Logging.Abstractions;
using WindowsDoctorAI.App;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class HomeHistoryStateTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadLatestDistinguishesPendingHistoryReadFromNoStoredRun()
    {
        var history = new FakeHistory(deferLatestRead: true);
        var settings = new FakeUserSettingsRepository(new UserSettings());
        var viewModel = CreateViewModel(history, settings);

        var loadTask = viewModel.LoadLatestAsync();

        Assert.True(viewModel.IsLoadingHistory);
        Assert.Equal("Carregando histórico local...", viewModel.StatusMessage);
        Assert.Equal("Carregando histórico local...", viewModel.LastDiagnosticText);
        Assert.Equal("Carregando resultados do histórico local...", viewModel.FindingsSummary);

        history.CompleteLatestRead(null);
        await loadTask;

        Assert.False(viewModel.IsLoadingHistory);
        Assert.Equal("Nenhum diagnóstico anterior encontrado no histórico local.", viewModel.LastDiagnosticText);
        Assert.Equal("Nenhum resultado anterior está disponível no histórico local.", viewModel.FindingsSummary);
        Assert.Contains("Execute um diagnóstico", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, settings.GetCallCount);
        Assert.Equal(0, settings.SaveCallCount);
        Assert.Equal(0, history.SaveCallCount);
    }

    [Fact]
    public async Task StartingDiagnosticWithDefaultPreferencesDoesNotEnableOrSaveHistory()
    {
        var preferences = new UserSettings();
        var settings = new FakeUserSettingsRepository(preferences);
        var history = new FakeHistory();
        var viewModel = CreateViewModel(history, settings);

        await viewModel.StartDiagnosticCommand.ExecuteAsync(null);

        Assert.False(preferences.SaveDiagnosticHistory);
        Assert.False(settings.Settings.SaveDiagnosticHistory);
        Assert.Equal(1, settings.GetCallCount);
        Assert.Equal(0, settings.SaveCallCount);
        Assert.Equal(0, history.SaveCallCount);
        Assert.Contains("histórico está desativado nas Configurações", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("heurística", viewModel.HealthScoreDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não representa a saúde global", viewModel.HealthScoreDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CalculatedScoreIsDescribedAsHeuristicRatherThanGlobalHealth()
    {
        var history = new FakeHistory();
        var settings = new FakeUserSettingsRepository(new UserSettings());
        var report = new DiagnosticReport(
            [new DiagnosticResult("Scanner de teste", "Sistema", DiagnosticSeverity.Information, DiagnosticStatus.Healthy,
                "Verificação de teste", "Sem observações", "Nenhuma ação necessária", "Estado observado", TimeSpan.Zero, FixedNow)],
            FixedNow,
            FixedNow,
            TimeSpan.Zero,
            new HealthScore(95));
        var viewModel = CreateViewModel(history, settings, report);

        await viewModel.StartDiagnosticCommand.ExecuteAsync(null);

        Assert.Equal("95", viewModel.HealthScore);
        Assert.Contains("Pontuação heurística", viewModel.HealthScoreDescription, StringComparison.Ordinal);
        Assert.Contains("não representa a saúde global do computador", viewModel.HealthScoreDescription, StringComparison.OrdinalIgnoreCase);
    }

    private static HomeViewModel CreateViewModel(
        FakeHistory history,
        FakeUserSettingsRepository settings,
        DiagnosticReport? report = null)
    {
        var knowledge = new FakeKnowledgeRepository();
        var runDiagnostic = new RunComputerInventoryDiagnosticUseCase(
            new FakeInventoryScanner(),
            new FakeDiagnosticEngine(report ?? new DiagnosticReport(Array.Empty<DiagnosticResult>(), FixedNow, FixedNow, TimeSpan.Zero, null)),
            history,
            settings,
            NullLogger<RunComputerInventoryDiagnosticUseCase>.Instance);
        var assessment = new DiagnosticAssessmentService(
            knowledge,
            new RecommendationEngine(),
            new RootCauseAnalyzer(),
            new HtmlDiagnosticReportFormatter());

        return new HomeViewModel(
            runDiagnostic,
            history,
            knowledge,
            new KnowledgeJsonImporter(knowledge),
            assessment,
            new CbsLogImportService(new NoCbsLogPicker(), new CbsLogMarkerClassifier()),
            new UnsupportedBackupSetCatalogSource(),
            NullLogger<HomeViewModel>.Instance);
    }

    private sealed class FakeHistory(bool deferLatestRead = false) : IDiagnosticRunRepository
    {
        private readonly TaskCompletionSource<DiagnosticRun?> _latestRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SaveCallCount { get; private set; }
        public int GetLatestCallCount { get; private set; }
        public DiagnosticRun? SavedRun { get; private set; }

        public Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            SavedRun = run;
            return Task.CompletedTask;
        }

        public Task<DiagnosticRun?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            GetLatestCallCount++;
            return deferLatestRead ? _latestRead.Task : Task.FromResult<DiagnosticRun?>(null);
        }

        public void CompleteLatestRead(DiagnosticRun? run) => _latestRead.SetResult(run);

        public Task<int> DeleteCompletedBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeUserSettingsRepository(UserSettings settings) : IUserSettingsRepository
    {
        public UserSettings Settings { get; private set; } = settings;
        public int GetCallCount { get; private set; }
        public int SaveCallCount { get; private set; }

        public Task<UserSettings> GetAsync(CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult(Settings);
        }

        public Task SaveAsync(UserSettings updated, CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            Settings = updated;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryScanner : IComputerInventoryScanner
    {
        public Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ComputerInventory());
    }

    private sealed class FakeDiagnosticEngine(DiagnosticReport report) : IDiagnosticEngine
    {
        public Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(report);
    }

    private sealed class FakeKnowledgeRepository : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeRule>>(Array.Empty<KnowledgeRule>());

        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoCbsLogPicker : ICbsLogFilePicker
    {
        public Task<Stream?> PickCbsLogAsync(CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
    }
}
