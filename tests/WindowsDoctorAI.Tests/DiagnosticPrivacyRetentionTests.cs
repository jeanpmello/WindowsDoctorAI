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
            new EmptyKnowledgeRepository(), new RecommendationEngine(),
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
    public async Task CurrentAndLegacyPluginPayloadsAreRedactedInSqliteDisplayAndFullHtmlFlow()
    {
        const string host = "PRIVATE-HOST-726";
        const string user = "amy";
        const string email = "legacy.user@example.test";
        const string password = "unshared-password-726";
        const string token = "unshared-token-726";
        const string ipv4 = "198.51.100.27";
        const string ipv6 = "2001:db8::44";
        const string path = "C:\\Users\\private-user-726\\private\\evidence.log";
        const string uniqueId = "7b022f86-c5ea-428d-948f-98186d8af726";
        var inventory = new ComputerInventory { ComputerName = host, UserName = user, IPv4Addresses = [ipv4], IPv6Addresses = [ipv6] };
        var currentRun = BuildRun(FixedNow, inventory, BuildSensitivePluginResult(FixedNow, host, user, email, password, token, ipv4, ipv6, path, uniqueId));

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new SqliteDiagnosticRunRepository(context);
        await repository.SaveAsync(currentRun);

        var currentPayload = await context.DiagnosticRuns.Select(item => item.PayloadJson).SingleAsync();
        AssertPrivacyValuesAbsent(currentPayload, host, user, email, password, token, ipv4, ipv6, path, uniqueId);
        var currentLoaded = await repository.GetLatestAsync();
        Assert.NotNull(currentLoaded);
        var assessment = new DiagnosticAssessmentService(
            new EmptyKnowledgeRepository(), new RecommendationEngine(), new RootCauseAnalyzer(), new HtmlDiagnosticReportFormatter());
        var currentHtml = await assessment.CreateHtmlReportAsync(currentLoaded);
        var currentDisplay = DiagnosticDisplayFormatter.FormatFindings(currentLoaded.Report!, currentLoaded.Inventory);
        AssertPrivacyValuesAbsent(currentHtml, host, user, email, password, token, ipv4, ipv6, path, uniqueId);
        AssertPrivacyValuesAbsent(currentDisplay, host, user, email, password, token, ipv4, ipv6, path, uniqueId);
        Assert.Contains("0x80073712", currentHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", currentHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", currentHtml, StringComparison.Ordinal);

        var legacyRun = BuildRun(FixedNow.AddMinutes(1), inventory,
            BuildSensitivePluginResult(FixedNow.AddMinutes(1), host, user, email, password, token, ipv4, ipv6, path, uniqueId));
        var legacyPayload = System.Text.Json.JsonSerializer.Serialize(legacyRun,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        context.DiagnosticRuns.Add(new DiagnosticRunEntity
        {
            Id = legacyRun.Id,
            CompletedAtUnixMilliseconds = legacyRun.CompletedAtUtc.ToUnixTimeMilliseconds(),
            PayloadJson = legacyPayload
        });
        await context.SaveChangesAsync();

        var legacyLoaded = await repository.GetLatestAsync();
        Assert.NotNull(legacyLoaded);
        Assert.Contains(email, legacyLoaded.Report!.Results[0].ScannerName, StringComparison.Ordinal);
        var legacyHtml = await assessment.CreateHtmlReportAsync(legacyLoaded);
        var legacyDisplay = DiagnosticDisplayFormatter.FormatFindings(legacyLoaded.Report!, legacyLoaded.Inventory);
        AssertPrivacyValuesAbsent(legacyHtml, host, user, email, password, token, ipv4, ipv6, path, uniqueId);
        AssertPrivacyValuesAbsent(legacyDisplay, host, user, email, password, token, ipv4, ipv6, path, uniqueId);
        Assert.Contains("0x80073712", legacyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(legacyPayload, await context.DiagnosticRuns.Where(item => item.Id == legacyRun.Id).Select(item => item.PayloadJson).SingleAsync());
    }

    [Fact]
    public void DisplayFormatterRedactsCurrentEventViewerMessagesAndPluginFields()
    {
        const string privateMessage = "account=display.user@example.test; password=display-secret; C:\\Users\\display.user\\event.evtx; 0x80073712";
        var eventRun = BuildRun(FixedNow, new ComputerInventory { UserName = "display.user" },
            new DiagnosticResult("Event Viewer", "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
                "Warning · Provider · evento 7001", privateMessage, "Recomendação derivada do evento: token=display-token",
                "Log=System; ID=7001; mensagem=" + privateMessage, TimeSpan.Zero, FixedNow));

        var display = DiagnosticDisplayFormatter.FormatFindings(eventRun.Report!, eventRun.Inventory);

        Assert.DoesNotContain("display.user@example.test", display, StringComparison.Ordinal);
        Assert.DoesNotContain("display-secret", display, StringComparison.Ordinal);
        Assert.DoesNotContain("display-token", display, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users\\display.user", display, StringComparison.Ordinal);
        Assert.Contains("ID=7001", display, StringComparison.Ordinal);
        Assert.Contains("0x80073712", display, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticExceptionStatusIsGenericAndDoesNotExposePathsOrIdentifiers()
    {
        var exception = new IOException("Failed at C:\\Users\\private-user\\db.sqlite for 7b022f86-c5ea-428d-948f-98186d8af726");

        var status = DiagnosticPrivacyMessages.DiagnosticFailure(exception);

        Assert.Contains("Não foi possível concluir o diagnóstico", status, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users\\private-user", status, StringComparison.Ordinal);
        Assert.DoesNotContain("7b022f86-c5ea-428d-948f-98186d8af726", status, StringComparison.Ordinal);
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
        Assert.False(defaults.SaveDiagnosticHistory);
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
    public async Task SavingRetentionRequiresConfirmationAndCancelPreservesPreferencesRunsAndRepairAudit()
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
        context.RepairHistory.Add(new RepairHistoryEntity
        {
            Id = Guid.NewGuid(), ProposalId = "trace-repair", Title = "Repair audit", Details = "Preservar",
            StartedAtUnixMilliseconds = FixedNow.ToUnixTimeMilliseconds(), CompletedAtUnixMilliseconds = FixedNow.ToUnixTimeMilliseconds()
        });
        await context.SaveChangesAsync();
        var settingsRepository = new SqliteUserSettingsRepository(context);
        var service = new DiagnosticPreferencesService(settingsRepository, new DiagnosticHistoryMaintenanceService(repository));
        var requested = new UserSettings { SaveDiagnosticHistory = true, DiagnosticRetentionDays = 30 };

        var canceled = await service.SaveAsync(requested, confirmAgeBasedPurge: false, FixedNow);

        Assert.True(canceled.WasCanceled);
        Assert.Equal(0, canceled.PurgedRecords);
        Assert.False((await settingsRepository.GetAsync()).SaveDiagnosticHistory);
        Assert.Equal(0, (await settingsRepository.GetAsync()).DiagnosticRetentionDays);
        Assert.Equal(3, await context.DiagnosticRuns.CountAsync());

        var confirmed = await service.SaveAsync(requested, confirmAgeBasedPurge: true, FixedNow);

        Assert.False(confirmed.WasCanceled);
        Assert.Equal(1, confirmed.PurgedRecords);
        Assert.True((await settingsRepository.GetAsync()).SaveDiagnosticHistory);
        Assert.Equal(30, (await settingsRepository.GetAsync()).DiagnosticRetentionDays);
        Assert.Equal(2, await context.DiagnosticRuns.CountAsync());
        Assert.Equal(1, await context.RepairHistory.CountAsync());
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
        Assert.Equal(4, Convert.ToInt32(await verify.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SchemaV4DisablesImplicitHistoryOptInButPreservesExistingPayloadAndRetention()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var legacyPayload = "legacy-private-payload-preserved";
        var legacyId = Guid.NewGuid();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "DiagnosticRuns" ("Id" TEXT NOT NULL PRIMARY KEY, "CompletedAtUnixMilliseconds" INTEGER NOT NULL, "PayloadJson" TEXT NOT NULL);
                INSERT INTO "DiagnosticRuns" VALUES ($id, $completed, $payload);
                CREATE TABLE "UserSettings" ("Id" INTEGER NOT NULL PRIMARY KEY, "SaveDiagnosticHistory" INTEGER NOT NULL, "DiagnosticRetentionDays" INTEGER NOT NULL);
                INSERT INTO "UserSettings" VALUES (1, 1, 90);
                PRAGMA user_version = 3;
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
        Assert.Equal(legacyPayload, await context.DiagnosticRuns.Select(item => item.PayloadJson).SingleAsync());
        var settings = await new SqliteUserSettingsRepository(context).GetAsync();
        Assert.False(settings.SaveDiagnosticHistory);
        Assert.Equal(90, settings.DiagnosticRetentionDays);
    }

    private static DiagnosticRun BuildRun(DateTimeOffset completedAt, ComputerInventory inventory, DiagnosticResult? result = null)
    {
        var report = result is null
            ? null
            : new DiagnosticReport([result], completedAt.AddSeconds(-1), completedAt, TimeSpan.FromSeconds(1), new HealthScore(80));
        return new DiagnosticRun(Guid.NewGuid(), completedAt.AddSeconds(-1), completedAt, TimeSpan.FromSeconds(1), inventory, report);
    }

    private static DiagnosticResult BuildSensitivePluginResult(
        DateTimeOffset timestamp, string host, string user, string email, string password, string token,
        string ipv4, string ipv6, string path, string uniqueId) => new(
        $"Legacy Plugin {email}", $"System {host}", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
        $"<script>{path}</script>",
        $"Description account={email}; password={password}; path={path}; IP {ipv4}; HRESULT 0x80073712",
        $"Recommendation token={token}; host {host}",
        $"Evidence user={user}; address {ipv6}; run {uniqueId}; IP {ipv4}",
        TimeSpan.Zero, timestamp);

    private static void AssertPrivacyValuesAbsent(string text, params string[] values)
    {
        foreach (var value in values)
            Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
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
