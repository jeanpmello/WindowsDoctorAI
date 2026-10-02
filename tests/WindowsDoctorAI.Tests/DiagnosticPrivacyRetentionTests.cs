using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Database;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class DiagnosticPrivacyRetentionTests
{
    private static readonly DateTimeOffset FixedNow = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RedactionRemovesKnownInventoryIdentifiersAndRawEventMessageButKeepsSupportCodes()
    {
        const string privateEmail = "alice@example.test";
        const string privatePassword = "event-secret-should-not-survive";
        var rawMessage = $"Falha de conta {privateEmail}; password={privatePassword}; IP 192.0.2.44; HRESULT 0x80073712.";
        var source = BuildRun(FixedNow, new ComputerInventory
        {
            ComputerName = "ALICE-WORKSTATION",
            SerialNumber = "SERIAL-PRIVATE-001",
            UserName = "alice",
            Domain = "corp.example.test",
            IPv4Addresses = ["192.0.2.44"],
            IPv6Addresses = ["2001:db8::44"],
            Bios = new BiosDetails(SerialNumber: "BIOS-SERIAL-PRIVATE"),
            NetworkAdapters = [new NetworkAdapterDetails("Wi-Fi", "Network adapter", "Up", ["192.0.2.44"], ["2001:db8::44"])]
        }, new DiagnosticResult(
            "Event Viewer", "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
            "Warning · Provider · evento 7001", rawMessage, "Investigue o evento.",
            "Log=System; ID=7001; nível=Warning; data=2025-01-01.", TimeSpan.Zero, FixedNow));

        var redacted = DiagnosticPrivacyRedactor.Redact(source);
        var serialized = System.Text.Json.JsonSerializer.Serialize(redacted, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var result = Assert.Single(redacted.Report!.Results);

        Assert.Equal("[redigido]", redacted.Inventory.ComputerName);
        Assert.Equal("[redigido]", redacted.Inventory.SerialNumber);
        Assert.Equal("[redigido]", redacted.Inventory.UserName);
        Assert.Equal("[redigido]", redacted.Inventory.Domain);
        Assert.Equal("[redigido]", redacted.Inventory.Bios.SerialNumber);
        Assert.Empty(redacted.Inventory.IPv4Addresses);
        Assert.Empty(redacted.Inventory.IPv6Addresses);
        Assert.Empty(Assert.Single(redacted.Inventory.NetworkAdapters).IPv4Addresses);
        Assert.DoesNotContain(privateEmail, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(privatePassword, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("192.0.2.44", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("2001:db8::44", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("ALICE-WORKSTATION", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SERIAL-PRIVATE-001", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("BIOS-SERIAL-PRIVATE", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(rawMessage, result.Description, StringComparison.Ordinal);
        Assert.Contains("0x80073712", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ID=7001", result.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HtmlExportOfLegacyEventRunRedactsRawValuesAndKeepsErrorEvidence()
    {
        const string secret = "private-event-value-7721";
        const string email = "tech.user@example.test";
        var rawMessage = $"Event contains {secret}, account {email}, and HRESULT 0x80073712.";
        var run = BuildRun(FixedNow, new ComputerInventory { ComputerName = "PRIVATE-HOST" },
            new DiagnosticResult("Event Viewer", "Sistema", DiagnosticSeverity.Critical, DiagnosticStatus.Finding,
                "Error · Service Control Manager · evento 7001", rawMessage,
                "Investigue a origem e o contexto do evento antes de agir.", "Log=System; ID=7001; código observado.",
                TimeSpan.Zero, FixedNow));
        var assessment = new DiagnosticAssessmentService(
            new EmptyKnowledgeRepository(), new EmptyRepairAuditLog(), new RecommendationEngine(),
            new RootCauseAnalyzer(), new HtmlDiagnosticReportFormatter());

        var html = await assessment.CreateHtmlReportAsync(run);

        Assert.Contains("0x80073712", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("7001", html, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, html, StringComparison.Ordinal);
        Assert.DoesNotContain(email, html, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-HOST", html, StringComparison.Ordinal);
        Assert.DoesNotContain(rawMessage, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqliteSaveRedactsNewPayloadAndSettingsDefaultRetentionIsDisabled()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var settingsRepository = new SqliteUserSettingsRepository(context);
        var defaults = await settingsRepository.GetAsync();
        Assert.True(defaults.SaveDiagnosticHistory);
        Assert.Equal(0, defaults.DiagnosticRetentionDays);

        var run = BuildRun(FixedNow, new ComputerInventory
        {
            ComputerName = "PRIVATE-PC",
            UserName = "private-user",
            IPv4Addresses = ["198.51.100.27"]
        }, new DiagnosticResult("Windows Update", "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
            "Evento de falha do Windows Update (ID 20)", "Credencial=secret-value; HRESULT 0x80073712",
            "Nenhuma correção aplicada.", "ID 20; código 0x80073712.", TimeSpan.Zero, FixedNow));
        var repository = new SqliteDiagnosticRunRepository(context);
        await repository.SaveAsync(run);
        var payload = await context.DiagnosticRuns.Select(item => item.PayloadJson).SingleAsync();

        Assert.DoesNotContain("PRIVATE-PC", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("198.51.100.27", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", payload, StringComparison.Ordinal);
        Assert.Contains("0x80073712", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfiguredRetentionDeletesOnlyRecordsStrictlyOlderThanCutoffAndDisabledDefaultPurgesNothing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new SqliteDiagnosticRunRepository(context);
        await repository.SaveAsync(BuildRun(FixedNow.AddDays(-31), new ComputerInventory()));
        await repository.SaveAsync(BuildRun(FixedNow.AddDays(-30), new ComputerInventory()));
        await repository.SaveAsync(BuildRun(FixedNow.AddDays(-5), new ComputerInventory()));
        var maintenance = new DiagnosticHistoryMaintenanceService(repository);

        var disabledDeleted = await maintenance.PurgeExpiredAsync(new UserSettings(), FixedNow);
        var enabledDeleted = await maintenance.PurgeExpiredAsync(new UserSettings { DiagnosticRetentionDays = 30 }, FixedNow);

        Assert.Equal(0, disabledDeleted);
        Assert.Equal(1, enabledDeleted);
        Assert.Equal(2, await context.DiagnosticRuns.CountAsync());
        var latest = await repository.GetLatestAsync();
        Assert.NotNull(latest);
        Assert.Equal(FixedNow.AddDays(-5), latest.CompletedAtUtc);
    }

    [Fact]
    public async Task CancelingClearKeepsRowsAndConfirmingDeletesOnlyDiagnosticRuns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new SqliteDiagnosticRunRepository(context);
        await repository.SaveAsync(BuildRun(FixedNow.AddMinutes(-2), new ComputerInventory()));
        await repository.SaveAsync(BuildRun(FixedNow.AddMinutes(-1), new ComputerInventory()));
        context.RepairHistory.Add(new RepairHistoryEntity
        {
            Id = Guid.NewGuid(), ProposalId = "fixture", Title = "Reparo fixture", Details = "Preservar",
            StartedAtUnixMilliseconds = FixedNow.ToUnixTimeMilliseconds(), CompletedAtUnixMilliseconds = FixedNow.ToUnixTimeMilliseconds()
        });
        await context.SaveChangesAsync();
        var maintenance = new DiagnosticHistoryMaintenanceService(repository);
        var confirmCalls = 0;

        var canceled = await maintenance.ClearAllAsync(_ =>
        {
            confirmCalls++;
            return Task.FromResult(false);
        });
        var confirmed = await maintenance.ClearAllAsync(_ =>
        {
            confirmCalls++;
            return Task.FromResult(true);
        });

        Assert.True(canceled.WasCanceled);
        Assert.Equal(0, canceled.DeletedRecords);
        Assert.Equal(2, confirmed.DeletedRecords);
        Assert.False(confirmed.WasCanceled);
        Assert.Equal(2, confirmCalls);
        Assert.Equal(0, await context.DiagnosticRuns.CountAsync());
        Assert.Equal(1, await context.RepairHistory.CountAsync());
    }

    [Fact]
    public async Task FailedTransactionalDeleteLeavesAllDiagnosticRowsIntact()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new SqliteDiagnosticRunRepository(context);
        await repository.SaveAsync(BuildRun(FixedNow.AddMinutes(-2), new ComputerInventory()));
        await repository.SaveAsync(BuildRun(FixedNow.AddMinutes(-1), new ComputerInventory()));
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "BlockDiagnosticRunDelete" BEFORE DELETE ON "DiagnosticRuns"
            BEGIN SELECT RAISE(ABORT, 'fixture delete failure'); END;
            """);

        await Assert.ThrowsAsync<SqliteException>(() => repository.DeleteAllAsync());

        Assert.Equal(2, await context.DiagnosticRuns.CountAsync());
    }

    [Fact]
    public async Task MigrationFromSchemaV2PreservesExistingRowsAndUsesDisabledRetentionDefault()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var legacyPayload = "{\"legacy\":\"keep-this-existing-record\"}";
        var legacyId = Guid.NewGuid();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "DiagnosticRuns" ("Id" TEXT NOT NULL PRIMARY KEY, "CompletedAtUnixMilliseconds" INTEGER NOT NULL, "PayloadJson" TEXT NOT NULL);
                INSERT INTO "DiagnosticRuns" VALUES ($id, $completed, $payload);
                CREATE TABLE "UserSettings" ("Id" INTEGER NOT NULL PRIMARY KEY, "SaveDiagnosticHistory" INTEGER NOT NULL);
                INSERT INTO "UserSettings" VALUES (1, 0);
                PRAGMA user_version = 2;
                """;
            command.Parameters.AddWithValue("$id", legacyId.ToString("N"));
            command.Parameters.AddWithValue("$completed", FixedNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$payload", legacyPayload);
            await command.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);

        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);

        Assert.Equal(1, await context.DiagnosticRuns.CountAsync());
        Assert.Equal(legacyPayload, await context.DiagnosticRuns.Select(run => run.PayloadJson).SingleAsync());
        var settings = await new SqliteUserSettingsRepository(context).GetAsync();
        Assert.False(settings.SaveDiagnosticHistory);
        Assert.Equal(0, settings.DiagnosticRetentionDays);
        await using var verify = connection.CreateCommand();
        verify.CommandText = "PRAGMA user_version;";
        Assert.Equal(3, Convert.ToInt32(await verify.ExecuteScalarAsync()));
    }

    private static DiagnosticRun BuildRun(DateTimeOffset completedAt, ComputerInventory inventory, DiagnosticResult? result = null)
    {
        var report = result is null
            ? null
            : new DiagnosticReport([result], completedAt.AddSeconds(-1), completedAt, TimeSpan.FromSeconds(1), new HealthScore(80));
        return new DiagnosticRun(Guid.NewGuid(), completedAt.AddSeconds(-1), completedAt, TimeSpan.FromSeconds(1), inventory, report);
    }

    private sealed class EmptyKnowledgeRepository : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeRule>>([]);
        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyRepairAuditLog : IRepairAuditLog
    {
        public Task SaveAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<RepairHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepairHistoryRecord>>([]);
    }
}
