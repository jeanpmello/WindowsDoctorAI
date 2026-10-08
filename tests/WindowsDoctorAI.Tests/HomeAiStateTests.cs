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

    private sealed class TestKnowledgeRepository(IReadOnlyList<KnowledgeRule>? rules = null) : IKnowledgeRepository
    {
        public TaskCompletionSource<bool>? ProvenanceReadStarted { get; set; }
        public TaskCompletionSource<bool>? ProvenanceReadGate { get; set; }

        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(rules ?? (IReadOnlyList<KnowledgeRule>)Array.Empty<KnowledgeRule>());

        public async Task<IReadOnlyList<KnowledgeRuleProvenance>> GetLatestRuleProvenanceAsync(CancellationToken cancellationToken = default)
        {
            if (ProvenanceReadGate is { } gate)
            {
                ProvenanceReadStarted?.TrySetResult(true);
                await gate.Task.WaitAsync(cancellationToken);
            }

            return Array.Empty<KnowledgeRuleProvenance>();
        }

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

    private sealed class CapturingAiProvider : IDiagnosticAiProvider
    {
        public AiAnalysisRequest? LastRequest { get; private set; }
        public int AvailabilityCount { get; private set; }
        public int AnalysisCount { get; private set; }
        public string Name => "capture";

        public Task<AiProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            AvailabilityCount++;
            return Task.FromResult(new AiProviderAvailability(true, "ready", "test-model"));
        }

        public Task<AiAnalysisResult> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            AnalysisCount++;
            LastRequest = request;
            return Task.FromResult(new AiAnalysisResult(AiAnalysisStatus.Completed, "ok", "resposta", "test-model"));
        }
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

    [Fact]
    public async Task Preview_and_Analyze_use_the_same_manual_guidance_prompt_snapshot()
    {
        var now = FixedNow;
        var finding = new DiagnosticResult(
            "Windows Update", "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
            "Falha observada 0xABCD1234", "Evidência sintética 0xABCD1234", "Revisão manual",
            "Evento 0xABCD1234", TimeSpan.Zero, now);
        var run = new DiagnosticRun(
            Guid.NewGuid(), now, now, TimeSpan.Zero, new ComputerInventory(),
            new DiagnosticReport([finding], now, now, TimeSpan.Zero, new HealthScore(75)));
        var rule = new KnowledgeRule(
            "fixture.preview", 3, "Fixture", "Regra de prévia", KnowledgeImpact.Moderate,
            ["0xABCD1234"], [], [], ["Orientação declarada; revisão humana."],
            [new KnowledgeReference("Manual", "https://docs.example.test/preview")],
            "Aplicabilidade declarada; não verificada automaticamente.",
            Procedure: new KnowledgeProcedure(
                "Revise o achado.", "Decida manualmente se a orientação se aplica.",
                "Conta padrão", false, "Risco declarado moderado", "Confira backup",
                "Rollback não declarado", "Fonte não verificada", false, true, true));
        var history = new TestHistory { LatestRun = run };
        var provider = new CapturingAiProvider();
        var viewModel = CreateViewModel(history, new CompletedDiagnosticEngine(run.Report!), provider, [rule]);

        await viewModel.LoadLatestAsync();
        var preview = viewModel.AiPromptPreview;
        Assert.Contains("fixture.preview · v3", preview);
        Assert.Contains("https://docs.example.test/preview", preview);

        await viewModel.AnalyzeWithAiCommand.ExecuteAsync(null);

        var sent = Assert.IsType<AiAnalysisRequest>(provider.LastRequest);
        var expectedPreview = $"[Instruções ao modelo]\n{sent.SystemPrompt}\n\n[Dados do diagnóstico e orientações ManualOnly (identificadores conhecidos redigidos; fontes não autenticadas)]\n{sent.UserPrompt}";
        Assert.Equal(expectedPreview, preview);
        Assert.Contains("fixture.preview · v3", sent.UserPrompt);
        Assert.Contains("Recebidas: 1; incluídas: 1; omitidas: 0", sent.UserPrompt);
    }

    [Fact]
    public async Task Local_ai_test_runs_without_sending_diagnostic_data()
    {
        var run = CreateFindingRun(Guid.NewGuid(), "0xDEADBEEF");
        var history = new TestHistory { LatestRun = run };
        var provider = new CapturingAiProvider();
        var viewModel = CreateViewModel(history, new CompletedDiagnosticEngine(run.Report!), provider);
        await viewModel.LoadLatestAsync();

        Assert.True(viewModel.TestAiCommand.CanExecute(null));
        await viewModel.TestAiCommand.ExecuteAsync(null);

        Assert.Equal(1, provider.AvailabilityCount);
        Assert.Equal(1, provider.AnalysisCount);
        Assert.DoesNotContain("0xDEADBEEF", provider.LastRequest!.UserPrompt);
        Assert.Contains("IA local pronta", provider.LastRequest.UserPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("resposta", viewModel.AiAnswerText);
        Assert.Contains("nenhum dado do diagnóstico", viewModel.AiStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.AnalyzeWithAiCommand.CanExecute(null));
    }

    [Fact]
    public async Task Local_ai_can_be_tested_before_running_a_diagnostic()
    {
        var history = new TestHistory();
        var provider = new CapturingAiProvider();
        var viewModel = CreateViewModel(history, new CompletedDiagnosticEngine(CreateReport()), provider);
        await viewModel.LoadLatestAsync();

        Assert.False(viewModel.CanAnalyzeWithAi);
        Assert.True(viewModel.TestAiCommand.CanExecute(null));
        await viewModel.TestAiCommand.ExecuteAsync(null);

        Assert.Equal(1, provider.AnalysisCount);
        Assert.Contains("IA local pronta", provider.LastRequest!.UserPrompt);
        Assert.False(viewModel.AnalyzeWithAiCommand.CanExecute(null));
    }

    [Fact]
    public async Task Loading_history_invalidates_ai_until_the_new_run_guidance_projection_is_complete()
    {
        var oldRun = CreateFindingRun(Guid.NewGuid(), "0x11112222");
        var newRun = CreateFindingRun(Guid.NewGuid(), "0x33334444");
        var oldRule = CreateRule("fixture.old", "0x11112222");
        var newRule = CreateRule("fixture.new", "0x33334444");
        var history = new TestHistory { LatestRun = oldRun };
        var knowledge = new TestKnowledgeRepository([oldRule, newRule]);
        var provider = new CapturingAiProvider();
        var viewModel = CreateViewModel(
            history,
            new CompletedDiagnosticEngine(oldRun.Report!),
            provider,
            knowledgeRepository: knowledge);

        await viewModel.LoadLatestAsync();
        Assert.Contains("fixture.old · v1", viewModel.AiPromptPreview);
        Assert.True(viewModel.AnalyzeWithAiCommand.CanExecute(null));

        var provenanceReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvenanceRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        knowledge.ProvenanceReadStarted = provenanceReadStarted;
        knowledge.ProvenanceReadGate = releaseProvenanceRead;
        history.LatestRun = newRun;

        var loading = viewModel.LoadLatestAsync();
        await provenanceReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsLoadingHistory);
        Assert.Empty(viewModel.AiPromptPreview);
        Assert.False(viewModel.CanAnalyzeWithAi);
        Assert.False(viewModel.AnalyzeWithAiCommand.CanExecute(null));
        await viewModel.AnalyzeWithAiCommand.ExecuteAsync(null);
        Assert.Equal(0, provider.AvailabilityCount);
        Assert.Equal(0, provider.AnalysisCount);
        Assert.Null(provider.LastRequest);

        releaseProvenanceRead.TrySetResult(true);
        await loading.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(viewModel.IsLoadingHistory);
        Assert.Contains("fixture.new · v1", viewModel.AiPromptPreview);
        Assert.DoesNotContain("fixture.old · v1", viewModel.AiPromptPreview);
        Assert.True(viewModel.AnalyzeWithAiCommand.CanExecute(null));
        await viewModel.AnalyzeWithAiCommand.ExecuteAsync(null);

        var sent = Assert.IsType<AiAnalysisRequest>(provider.LastRequest);
        Assert.Contains("fixture.new · v1", sent.UserPrompt);
        Assert.DoesNotContain("fixture.old · v1", sent.UserPrompt);
        Assert.Equal(1, provider.AvailabilityCount);
        Assert.Equal(1, provider.AnalysisCount);
    }

    private static HomeViewModel CreateViewModel(
        TestHistory history,
        IDiagnosticEngine engine,
        IDiagnosticAiProvider provider,
        IReadOnlyList<KnowledgeRule>? rules = null,
        TestKnowledgeRepository? knowledgeRepository = null)
    {
        var knowledge = knowledgeRepository ?? new TestKnowledgeRepository(rules);
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

    private static DiagnosticRun CreateFindingRun(Guid id, string errorCode)
    {
        var finding = new DiagnosticResult(
            "Windows Update", "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
            $"Falha observada {errorCode}", "Evidência sintética", "Revisão manual",
            $"Evento {errorCode}", TimeSpan.Zero, FixedNow);
        var report = new DiagnosticReport([finding], FixedNow, FixedNow, TimeSpan.Zero, new HealthScore(75));
        return new DiagnosticRun(id, FixedNow, FixedNow, TimeSpan.Zero, new ComputerInventory(), report);
    }

    private static KnowledgeRule CreateRule(string id, string errorCode) => new(
        id, 1, "Fixture", id, KnowledgeImpact.Moderate,
        [errorCode], [], [], ["Orientação declarada; revisão humana."],
        [new KnowledgeReference("Manual", $"https://docs.example.test/{id}")],
        "Aplicabilidade declarada; não verificada automaticamente.",
        Procedure: new KnowledgeProcedure(
            "Revise o achado.", "Decida manualmente se a orientação se aplica.",
            "Conta padrão", false, "Risco moderado", "Confira backup",
            "Rollback não declarado", "Fonte não verificada", false, true, true));

    private static DiagnosticReport CreateReport() => new(
        Array.Empty<DiagnosticResult>(), FixedNow, FixedNow, TimeSpan.Zero, new HealthScore(75));

    private sealed class CompletedDiagnosticEngine(DiagnosticReport report) : IDiagnosticEngine
    {
        public Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(report);
    }
}
