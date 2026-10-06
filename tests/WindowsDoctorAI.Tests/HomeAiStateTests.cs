using Microsoft.Extensions.Logging.Abstractions;
using WindowsDoctorAI.AI;
using WindowsDoctorAI.App;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class HomeAiStateTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class TestHistory : IDiagnosticRunRepository
    {
        public DiagnosticRun? LatestRun { get; set; }

        public Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken = default)
        {
            LatestRun = run;
            return Task.CompletedTask;
        }

        public Task<DiagnosticRun?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromResult(LatestRun);
        public Task<int> DeleteCompletedBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class TestKnowledgeRepository : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeRule>>(Array.Empty<KnowledgeRule>());

        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestSettingsRepository : IUserSettingsRepository
    {
        public Task<UserSettings> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserSettings { SaveDiagnosticHistory = true });

        public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestInventoryScanner : IComputerInventoryScanner
    {
        public Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ComputerInventory { OperatingSystem = new OperatingSystemDetails { Name = "Windows 11" } });
    }

    private sealed class GatedDiagnosticEngine : IDiagnosticEngine
    {
        private readonly TaskCompletionSource<DiagnosticReport> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default) => _completion.Task;
        public void Complete(DiagnosticReport report) => _completion.TrySetResult(report);
    }

    private sealed class BlockingAiProvider : IDiagnosticAiProvider
    {
        public TaskCompletionSource<bool> ProbeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ProbeToken { get; private set; }
        public int ProbeCount { get; private set; }
        public string Name => "test";

        public async Task<AiProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            ProbeCount++;
            ProbeToken = cancellationToken;
            ProbeStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new AiProviderAvailability(true, "ready", "test-model");
        }

        public Task<AiAnalysisResult> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiAnalysisResult(AiAnalysisStatus.Completed, "ok", "resposta", "test-model"));
    }

    private sealed class NoCbsLogPicker : ICbsLogFilePicker
    {
        public Task<Stream?> PickCbsLogAsync(CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
    }

    [Fact]
    public async Task Loading_a_different_diagnostic_cancels_and_discards_the_old_ai_operation()
    {
        var history = new TestHistory { LatestRun = CreateRun() };
        var provider = new BlockingAiProvider();
        var viewModel = CreateViewModel(history, new CompletedDiagnosticEngine(CreateReport()), provider);
        await viewModel.LoadLatestAsync();

        var analysis = viewModel.AnalyzeWithAiCommand.ExecuteAsync(null);
        await provider.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        history.LatestRun = CreateRun();

        await viewModel.LoadLatestAsync();
        await analysis.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(provider.ProbeToken.IsCancellationRequested);
        Assert.False(viewModel.IsAnalyzingWithAi);
        Assert.Contains("Pronto.", viewModel.AiStatusText);
        Assert.True(viewModel.CanAnalyzeWithAi);
    }

    [Fact]
    public async Task Scanning_cancels_ai_and_prevents_starting_an_analysis()
    {
        var history = new TestHistory { LatestRun = CreateRun() };
        var engine = new GatedDiagnosticEngine();
        var provider = new BlockingAiProvider();
        var viewModel = CreateViewModel(history, engine, provider);
        await viewModel.LoadLatestAsync();

        var analysis = viewModel.AnalyzeWithAiCommand.ExecuteAsync(null);
        await provider.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var diagnostic = viewModel.StartDiagnosticCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsScanning);
        Assert.False(viewModel.CanAnalyzeWithAi);
        Assert.False(viewModel.AnalyzeWithAiCommand.CanExecute(null));
        Assert.True(provider.ProbeToken.IsCancellationRequested);
        Assert.Equal(1, provider.ProbeCount);

        engine.Complete(CreateReport());
        await diagnostic.WaitAsync(TimeSpan.FromSeconds(2));
        await analysis.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(viewModel.IsScanning);
        Assert.True(viewModel.AnalyzeWithAiCommand.CanExecute(null));
    }

    private static HomeViewModel CreateViewModel(TestHistory history, IDiagnosticEngine engine, IDiagnosticAiProvider provider)
    {
        var knowledge = new TestKnowledgeRepository();
        var useCase = new RunComputerInventoryDiagnosticUseCase(
            new TestInventoryScanner(), engine, history, new TestSettingsRepository(),
            NullLogger<RunComputerInventoryDiagnosticUseCase>.Instance);
        var assessment = new DiagnosticAssessmentService(
            knowledge, new RecommendationEngine(), new RootCauseAnalyzer(), new WindowsDoctorAI.Reporting.HtmlDiagnosticReportFormatter());

        return new HomeViewModel(
            useCase, history, knowledge, new KnowledgeJsonImporter(knowledge), assessment,
            new CbsLogImportService(new NoCbsLogPicker(), new CbsLogMarkerClassifier()),
            new UnsupportedBackupSetCatalogSource(), NullLogger<HomeViewModel>.Instance, aiProvider: provider);
    }

    private static DiagnosticRun CreateRun() => new(
        Guid.NewGuid(), FixedNow, FixedNow, TimeSpan.Zero, new ComputerInventory(), CreateReport());

    private static DiagnosticReport CreateReport() => new(
        Array.Empty<DiagnosticResult>(), FixedNow, FixedNow, TimeSpan.Zero, new HealthScore(75));

    private sealed class CompletedDiagnosticEngine(DiagnosticReport report) : IDiagnosticEngine
    {
        public Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(report);
    }
}
