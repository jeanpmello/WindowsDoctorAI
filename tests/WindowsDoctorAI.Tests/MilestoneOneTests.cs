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
    public async Task ExecuteSavesRunAndUsesDemonstrationScoreWhenHistoryEnabled()
    {
        var inventory = new ComputerInventory { ComputerName = "TEST-PC" };
        var history = new FakeHistory();
        var useCase = CreateUseCase(inventory, new UserSettings(), history);

        var outcome = await useCase.ExecuteAsync();

        Assert.True(outcome.HistorySaved);
        Assert.Null(outcome.PersistenceWarning);
        Assert.Same(inventory, outcome.Run.Inventory);
        Assert.Equal(95, outcome.Run.HealthScore.Value);
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
    public async Task ExecutePreservesCollectedResultWhenHistoryWriteFails()
    {
        var history = new FakeHistory { ThrowOnSave = true };
        var useCase = CreateUseCase(new ComputerInventory { ComputerName = "SURVIVES" }, new UserSettings(), history);

        var outcome = await useCase.ExecuteAsync();

        Assert.Equal("SURVIVES", outcome.Run.Inventory.ComputerName);
        Assert.False(outcome.HistorySaved);
        Assert.NotNull(outcome.PersistenceWarning);
    }

    private static RunComputerInventoryDiagnosticUseCase CreateUseCase(ComputerInventory inventory, UserSettings settings, FakeHistory history) =>
        new(new FakeScanner(inventory), history, new FakeSettings(settings), NullLogger<RunComputerInventoryDiagnosticUseCase>.Instance);

    private sealed class FakeScanner(ComputerInventory inventory) : IComputerInventoryScanner
    {
        public Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult(inventory);
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
    }
}

public sealed class SqliteRepositoryTests
{
    [Fact]
    public async Task RepositoriesPersistAndRestoreSettingsAndInventoryUsingSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var history = new SqliteDiagnosticRunRepository(context);
        var settings = new SqliteUserSettingsRepository(context);

        Assert.True((await settings.GetAsync()).SaveDiagnosticHistory);
        await settings.SaveAsync(new UserSettings { SaveDiagnosticHistory = false });
        Assert.False((await settings.GetAsync()).SaveDiagnosticHistory);

        var now = DateTimeOffset.UtcNow;
        var run = new DiagnosticRun(Guid.NewGuid(), now.AddSeconds(-2), now, TimeSpan.FromSeconds(2),
            new ComputerInventory
            {
                ComputerName = "SQLITE-PC",
                IPv4Addresses = ["192.0.2.10"],
                PhysicalDisks = [new PhysicalDisk("\\\\.\\PHYSICALDRIVE0", "Samsung 990 Pro", 1_000_000_000_000, "SSD", "NVMe")]
            }, new HealthScore(95));
        await history.SaveAsync(run);

        var restored = await history.GetLatestAsync();
        Assert.NotNull(restored);
        Assert.Equal(run.Id, restored.Id);
        Assert.Equal("SQLITE-PC", restored.Inventory.ComputerName);
        Assert.Equal("192.0.2.10", Assert.Single(restored.Inventory.IPv4Addresses));
        Assert.Equal("Samsung 990 Pro", Assert.Single(restored.Inventory.PhysicalDisks).Model);
        Assert.Equal(95, restored.HealthScore.Value);
    }
}
