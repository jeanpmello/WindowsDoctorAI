using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsDoctorAI.App;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Database;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class CbsLogImportUiTests
{
    private const string PackageIdentity = "Package_123_for_KB3192392~31bf3856ad364e35~amd64~~6.3.1.4";
    private const string PrivatePath = @"C:\Users\private.user\CBS.log";
    private const string PrivateHost = "CBS-PRIVATE-HOST-778";
    private const string PrivateToken = "raw-import-secret-778";
    private const string RequiredDisclaimer = "não atribuída ao evento; não confirma causa; não acionável";

    [Fact]
    public async Task OfflineMarkerIsExplicitlyUnattributedAndNeverAddedToHistoryHtmlOrLogs()
    {
        var now = DateTimeOffset.UtcNow;
        var report = new DiagnosticReport(Array.Empty<DiagnosticResult>(), now, now, TimeSpan.Zero, null);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var db = new WindowsDoctorDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var history = new SqliteDiagnosticRunRepository(db);
        var knowledge = new EmptyKnowledgeRepository();
        var runUseCase = new RunComputerInventoryDiagnosticUseCase(
            new TestInventoryScanner(), new TestDiagnosticEngine(report), history,
            new TestUserSettingsRepository(), NullLogger<RunComputerInventoryDiagnosticUseCase>.Instance);
        var assessment = new DiagnosticAssessmentService(
            knowledge, new RecommendationEngine(), new RootCauseAnalyzer(), new HtmlDiagnosticReportFormatter());
        var viewModelLogger = new CapturingLogger<HomeViewModel>();
        var cbsText = $"{now.ToLocalTime():yyyy-MM-dd HH:mm:ss}, Info CBS Store corruption, manifest missing for package: {PackageIdentity}\r\n"
            + $"Private {PrivatePath}; host={PrivateHost}; token={PrivateToken}\r\n";
        var picker = new TestCbsLogPicker(Encoding.UTF8.GetBytes(cbsText));
        var viewModel = new HomeViewModel(
            runUseCase, history, knowledge, new KnowledgeJsonImporter(knowledge), assessment,
            new CbsLogImportService(picker, new CbsLogMarkerClassifier()), viewModelLogger);

        await viewModel.StartDiagnosticCommand.ExecuteAsync(null);
        Assert.True(viewModel.CanAnalyzeCbsLog);
        Assert.Equal(1, await db.DiagnosticRuns.CountAsync());
        var persistedRunBeforeImport = await db.DiagnosticRuns.Select(row => row.PayloadJson).SingleAsync();

        await viewModel.AnalyzeCbsLogCommand.ExecuteAsync(null);
        var persistedRunAfterImport = await db.DiagnosticRuns.Select(row => row.PayloadJson).SingleAsync();
        var html = await viewModel.CreateCurrentHtmlReportAsync();
        var visible = string.Join('\n', viewModel.CbsLogAnalysisStatus, viewModel.CbsLogSource,
            viewModel.CbsLogSignal, viewModel.CbsLogDisclaimer);

        Assert.Equal(1, await db.DiagnosticRuns.CountAsync());
        Assert.Equal(persistedRunBeforeImport, persistedRunAfterImport);
        Assert.DoesNotContain(cbsText, persistedRunAfterImport, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePath, persistedRunAfterImport, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateHost, persistedRunAfterImport, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateToken, persistedRunAfterImport, StringComparison.Ordinal);
        Assert.Equal("Origem: CBS.log selecionado manualmente (somente leitura)", viewModel.CbsLogSource);
        Assert.Contains("manifesto ausente", viewModel.CbsLogSignal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(RequiredDisclaimer, viewModel.CbsLogDisclaimer, StringComparison.Ordinal);
        Assert.Contains("nenhum achado ou recomendação foi criado", viewModel.CbsLogAnalysisStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PackageIdentity, visible, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePath, visible, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateHost, visible, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateToken, visible, StringComparison.Ordinal);
        Assert.DoesNotContain("Store corruption, manifest missing", html, StringComparison.Ordinal);
        Assert.DoesNotContain(PackageIdentity, html, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePath, html, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateHost, html, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateToken, html, StringComparison.Ordinal);
        Assert.Empty(viewModelLogger.Messages);
    }

    private sealed class TestInventoryScanner : IComputerInventoryScanner
    {
        public Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ComputerInventory());
    }

    private sealed class TestDiagnosticEngine(DiagnosticReport report) : IDiagnosticEngine
    {
        public Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(report);
    }

    private sealed class TestUserSettingsRepository : IUserSettingsRepository
    {
        public Task<UserSettings> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserSettings { SaveDiagnosticHistory = true });
        public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyKnowledgeRepository : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeRule>>(Array.Empty<KnowledgeRule>());
        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestCbsLogPicker(byte[] bytes) : ICbsLogFilePicker
    {
        public Task<Stream?> PickCbsLogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => new EmptyScope();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class EmptyScope : IDisposable
    {
        public void Dispose() { }
    }
}
