using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Database;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class HealthScoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(95)]
    [InlineData(100)]
    public void ScoreAcceptsValuesWithinRange(int value) => Assert.Equal(value, new HealthScore(value).Value);

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ScoreRejectsValuesOutsideRange(int value) => Assert.Throws<ArgumentOutOfRangeException>(() => new HealthScore(value));
}

public sealed class ComputerInventoryScannerTests
{
    [Fact]
    public async Task ScanAsyncReturnsSnapshotFromInjectedLocalSource()
    {
        var expected = new ComputerInventory { ComputerName = "TEST-PC", Manufacturer = "Example" };
        var source = new FakeDataSource(expected);
        var scanner = new ComputerInventoryScanner(source);

        var actual = await scanner.ScanAsync();

        Assert.Same(expected, actual);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task ScanAsyncHonorsCancellationBeforeReading()
    {
        var source = new FakeDataSource(new ComputerInventory());
        var scanner = new ComputerInventoryScanner(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(cancellation.Token));
        Assert.Equal(0, source.CallCount);
    }

    private sealed class FakeDataSource(ComputerInventory inventory) : IComputerInventoryDataSource
    {
        public int CallCount { get; private set; }
        public Task<ComputerInventory> CollectAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(inventory);
        }
    }
}

public sealed class RunComputerInventoryDiagnosticUseCaseTests
{
    [Fact]
    public async Task ExecuteSavesRunAndUsesScoreFromObservedChecksWhenHistoryEnabled()
    {
        var inventory = new ComputerInventory { ComputerName = "TEST-PC" };
        var history = new FakeHistory();
        var useCase = CreateUseCase(inventory, new UserSettings { SaveDiagnosticHistory = true }, history);

        var outcome = await useCase.ExecuteAsync();

        Assert.True(outcome.HistorySaved);
        Assert.Null(outcome.PersistenceWarning);
        Assert.Same(inventory, outcome.Run.Inventory);
        Assert.Equal(100, outcome.Run.Report!.HealthScore!.Value.Value);
        Assert.Same(outcome.Run, history.SavedRun);
    }

    [Fact]
    public async Task ExecuteDoesNotWriteHistoryWhenUserDisabledIt()
    {
        var history = new FakeHistory();
        var useCase = CreateUseCase(new ComputerInventory(), new UserSettings { SaveDiagnosticHistory = false }, history);

        var outcome = await useCase.ExecuteAsync();

        Assert.False(outcome.HistorySaved);
        Assert.Null(outcome.PersistenceWarning);
        Assert.Null(history.SavedRun);
    }

    [Fact]
    public async Task ExecuteDoesNotWriteHistoryWithDefaultPreferences()
    {
        var history = new FakeHistory();
        var useCase = CreateUseCase(new ComputerInventory(), new UserSettings(), history);

        var outcome = await useCase.ExecuteAsync();

        Assert.False(outcome.HistorySaved);
        Assert.Null(outcome.PersistenceWarning);
        Assert.Null(history.SavedRun);
    }

    [Fact]
    public async Task ExecutePreservesCollectedResultWhenHistoryWriteFails()
    {
        var history = new FakeHistory { ThrowOnSave = true };
        var useCase = CreateUseCase(new ComputerInventory { ComputerName = "SURVIVES" }, new UserSettings { SaveDiagnosticHistory = true }, history);

        var outcome = await useCase.ExecuteAsync();

        Assert.Equal("SURVIVES", outcome.Run.Inventory.ComputerName);
        Assert.NotNull(outcome.Run.Report);
        Assert.False(outcome.HistorySaved);
        Assert.NotNull(outcome.PersistenceWarning);
    }

    private static RunComputerInventoryDiagnosticUseCase CreateUseCase(ComputerInventory inventory, UserSettings settings, FakeHistory history) =>
        new(new FakeScanner(inventory), new FakeEngine(), history, new FakeSettings(settings), NullLogger<RunComputerInventoryDiagnosticUseCase>.Instance);

    private sealed class FakeScanner(ComputerInventory inventory) : IComputerInventoryScanner
    {
        public Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult(inventory);
    }

    private sealed class FakeEngine : IDiagnosticEngine
    {
        public Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            IReadOnlyList<DiagnosticResult> results =
            [
                new DiagnosticResult("Test", "Sistema", DiagnosticSeverity.Information, DiagnosticStatus.Healthy,
                    "Coleta confirmada", "Observação de teste", "Nenhuma ação", "Fixture unitária", TimeSpan.Zero, now)
            ];
            return Task.FromResult(new DiagnosticReport(results, now, now, TimeSpan.Zero, new HealthScore(100)));
        }
    }

    private sealed class FakeSettings(UserSettings settings) : IUserSettingsRepository
    {
        public Task<UserSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
        public Task SaveAsync(UserSettings value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeHistory : IDiagnosticRunRepository
    {
        public DiagnosticRun? SavedRun { get; private set; }
        public bool ThrowOnSave { get; init; }
        public Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave) throw new IOException("Teste: arquivo indisponível.");
            SavedRun = run;
            return Task.CompletedTask;
        }
        public Task<DiagnosticRun?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromResult(SavedRun);
        public Task<int> DeleteCompletedBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedRun is { } run && run.CompletedAtUtc < cutoffUtc ? ClearSavedRun() : 0);
        public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(ClearSavedRun());

        private int ClearSavedRun()
        {
            if (SavedRun is null) return 0;
            SavedRun = null;
            return 1;
        }
    }
}

public sealed class SqliteRepositoryTests
{
    [Fact]
    public async Task RepositoriesPersistAndRestoreSettingsInventoryAndDiagnosticReportUsingSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var history = new SqliteDiagnosticRunRepository(context);
        var settings = new SqliteUserSettingsRepository(context);

        Assert.False((await settings.GetAsync()).SaveDiagnosticHistory);
        Assert.Equal(0, (await settings.GetAsync()).DiagnosticRetentionDays);
        await settings.SaveAsync(new UserSettings { SaveDiagnosticHistory = false, DiagnosticRetentionDays = 90 });
        var savedSettings = await settings.GetAsync();
        Assert.False(savedSettings.SaveDiagnosticHistory);
        Assert.Equal(90, savedSettings.DiagnosticRetentionDays);

        var now = DateTimeOffset.UtcNow;
        var result = new DiagnosticResult("Test", "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
            "Aviso de teste", "Descrição", "Recomendação", "Evidência sintética do teste", TimeSpan.FromMilliseconds(20), now);
        var report = new DiagnosticReport([result], now.AddSeconds(-2), now, TimeSpan.FromSeconds(2), new HealthScore(92));
        var run = new DiagnosticRun(Guid.NewGuid(), now.AddSeconds(-2), now, TimeSpan.FromSeconds(2),
            new ComputerInventory
            {
                ComputerName = "SQLITE-PC",
                IPv4Addresses = ["192.0.2.10"],
                PhysicalDisks = [new PhysicalDisk("\\\\.\\PHYSICALDRIVE0", "Samsung 990 Pro", 1_000_000_000_000, "SSD", "NVMe")]
            }, report);
        await history.SaveAsync(run);

        var restored = await history.GetLatestAsync();
        Assert.NotNull(restored);
        Assert.Equal(run.Id, restored.Id);
        Assert.Equal("[redigido]", restored.Inventory.ComputerName);
        Assert.Empty(restored.Inventory.IPv4Addresses);
        Assert.Equal("Samsung 990 Pro", Assert.Single(restored.Inventory.PhysicalDisks).Model);
        Assert.Equal(92, restored.Report!.HealthScore!.Value.Value);
        Assert.Equal("Evidência sintética do teste", Assert.Single(restored.Report.Results).Evidence);
    }
}
