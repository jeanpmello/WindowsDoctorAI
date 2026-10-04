using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsDoctorAI.App;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class BackupSetCatalogUiTests
{
    private const string PrivateOpaqueVersion = "version-private-fixture-2026_10_04";

    [Fact]
    public async Task CatalogLoadsOnlyOnRequestAndStaysOutOfHistoryHtmlAndLogs()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var run = new DiagnosticRun(Guid.NewGuid(), now, now, TimeSpan.Zero,
            new ComputerInventory(), new DiagnosticReport([], now, now, TimeSpan.Zero, null));
        var history = new FakeHistory(run);
        var knowledge = new FakeKnowledgeRepository();
        var emptyReport = new DiagnosticReport([], now, now, TimeSpan.Zero, null);
        var runUseCase = new RunComputerInventoryDiagnosticUseCase(
            new FakeInventoryScanner(), new FakeDiagnosticEngine(emptyReport), history,
            new FakeSettingsRepository(), NullLogger<RunComputerInventoryDiagnosticUseCase>.Instance);
        var assessment = new DiagnosticAssessmentService(
            knowledge, new RecommendationEngine(), new RootCauseAnalyzer(), new HtmlDiagnosticReportFormatter());
        var catalogSource = new FakeCatalogSource(new BackupSetCatalogResult(
            BackupSetCatalogStatus.Available,
            [new BackupSetCatalogEntry(PrivateOpaqueVersion, now, BackupSetType.Full, 2)]));
        var logger = new CapturingLogger<HomeViewModel>();
        var viewModel = new HomeViewModel(
            runUseCase, history, knowledge, new KnowledgeJsonImporter(knowledge), assessment,
            new CbsLogImportService(new NoCbsLogPicker(), new CbsLogMarkerClassifier()),
            catalogSource, logger);

        await viewModel.LoadLatestAsync();
        Assert.Equal(0, catalogSource.CallCount);
        Assert.Same(run, history.LatestRun);

        await viewModel.RefreshBackupSetCatalogCommand.ExecuteAsync(null);
        Assert.Equal(1, catalogSource.CallCount);
        var item = Assert.Single(viewModel.BackupSetCatalogEntries);
        Assert.Equal(PrivateOpaqueVersion, item.VersionId);
        Assert.Equal("Completo", item.BackupTypeText);
        Assert.Equal("2 volume(s)", item.VolumeCountText);
        Assert.Equal("Não verificado", item.VerificationText);
        Assert.Contains("não verificadas", viewModel.BackupSetCatalogStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host", item.VersionId, StringComparison.OrdinalIgnoreCase);

        var html = await viewModel.CreateCurrentHtmlReportAsync();
        var runJson = System.Text.Json.JsonSerializer.Serialize(history.LatestRun);
        var visibleCatalogText = string.Join('\n', viewModel.BackupSetCatalogStatusText,
            string.Join('\n', viewModel.BackupSetCatalogEntries.Select(entry =>
                string.Join(' ', entry.VersionId, entry.BackupTimeText, entry.BackupTypeText, entry.VolumeCountText, entry.VerificationText))));

        Assert.NotNull(html);
        Assert.DoesNotContain(PrivateOpaqueVersion, runJson, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateOpaqueVersion, html, StringComparison.Ordinal);
        Assert.Contains(PrivateOpaqueVersion, visibleCatalogText, StringComparison.Ordinal);
        Assert.Equal(0, history.SaveCallCount);
        Assert.DoesNotContain(PrivateOpaqueVersion, string.Join('\n', logger.Messages), StringComparison.Ordinal);
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task TooManyInvalidOrCancelledCatalogsAreNotPartiallyDisplayed()
    {
        var now = DateTimeOffset.UtcNow;
        var fake = new FakeCatalogSource(new BackupSetCatalogResult(
            BackupSetCatalogStatus.TooManyBackupSets, Array.Empty<BackupSetCatalogEntry>()));
        var viewModel = CreateViewModel(fake, new FakeHistory(null));
        await viewModel.RefreshBackupSetCatalogCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.BackupSetCatalogEntries);
        Assert.Contains("limite", viewModel.BackupSetCatalogStatusText, StringComparison.OrdinalIgnoreCase);

        fake.Result = new BackupSetCatalogResult(BackupSetCatalogStatus.Available,
            [new BackupSetCatalogEntry("unsafe\\host", now, BackupSetType.Full)]);
        await viewModel.RefreshBackupSetCatalogCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.BackupSetCatalogEntries);
        Assert.Contains("indisponível", viewModel.BackupSetCatalogStatusText, StringComparison.OrdinalIgnoreCase);

        fake.BlockUntilCancelled = true;
        var pendingRefresh = viewModel.RefreshBackupSetCatalogCommand.ExecuteAsync(null);
        await Task.Yield();
        viewModel.CancelBackupSetCatalogCommand.Execute(null);
        await pendingRefresh;
        Assert.Empty(viewModel.BackupSetCatalogEntries);
        Assert.Contains("cancelada", viewModel.BackupSetCatalogStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsLoadingBackupSetCatalog);
    }

    private static HomeViewModel CreateViewModel(FakeCatalogSource source, FakeHistory history)
    {
        var now = DateTimeOffset.UtcNow;
        var knowledge = new FakeKnowledgeRepository();
        var report = new DiagnosticReport([], now, now, TimeSpan.Zero, null);
        var runUseCase = new RunComputerInventoryDiagnosticUseCase(
            new FakeInventoryScanner(), new FakeDiagnosticEngine(report), history,
            new FakeSettingsRepository(), NullLogger<RunComputerInventoryDiagnosticUseCase>.Instance);
        var assessment = new DiagnosticAssessmentService(
            knowledge, new RecommendationEngine(), new RootCauseAnalyzer(), new HtmlDiagnosticReportFormatter());
        return new HomeViewModel(runUseCase, history, knowledge, new KnowledgeJsonImporter(knowledge), assessment,
            new CbsLogImportService(new NoCbsLogPicker(), new CbsLogMarkerClassifier()), source,
            NullLogger<HomeViewModel>.Instance);
    }

    private sealed class FakeCatalogSource(BackupSetCatalogResult result) : IBackupSetCatalogSource
    {
        public BackupSetCatalogResult Result { get; set; } = result;
        public bool BlockUntilCancelled { get; set; }
        public int CallCount { get; private set; }
        public async Task<BackupSetCatalogResult> GetCatalogAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (BlockUntilCancelled)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            return Result;
        }
    }

    private sealed class FakeInventoryScanner : IComputerInventoryScanner
    {
        public Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ComputerInventory());
    }

    private sealed class FakeDiagnosticEngine(DiagnosticReport report) : IDiagnosticEngine
    {
        public Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(report);
    }

    private sealed class FakeHistory(DiagnosticRun? latest) : IDiagnosticRunRepository
    {
        public DiagnosticRun? LatestRun { get; } = latest;
        public int SaveCallCount { get; private set; }
        public Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            return Task.CompletedTask;
        }
        public Task<DiagnosticRun?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromResult(LatestRun);
        public Task<int> DeleteCompletedBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeSettingsRepository : IUserSettingsRepository
    {
        public Task<UserSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new UserSettings { SaveDiagnosticHistory = false });
        public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeKnowledgeRepository : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<KnowledgeRule>>([]);
        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoCbsLogPicker : ICbsLogFilePicker
    {
        public Task<Stream?> PickCbsLogAsync(CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
