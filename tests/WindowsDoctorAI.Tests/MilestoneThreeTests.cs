using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Database;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Repair;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class MilestoneThreeTests
{
    [Fact]
    public void RecommendationEngineUsesOnlyFindingEvidenceAndReportsMatchStrengthNotProbability()
    {
        var report = CreateReport(
            Finding("Windows Update", "Sistema", "Evento 20", "Falha 0xAABBCCDD"),
            Finding("Event Viewer", "Eventos", "Erro observado", "Registro contém 0xAABBCCDD"),
            new DiagnosticResult("Disk", "Hardware", DiagnosticSeverity.Information, DiagnosticStatus.Healthy,
                "OK", "", "", "0xAABBCCDD", TimeSpan.Zero, DateTimeOffset.UtcNow));
        var rule = Rule("sample.literal-code", ["0xAABBCCDD"], [], KnowledgeImpact.High);

        var recommendation = Assert.Single(new RecommendationEngine().Recommend(report, [rule]));

        Assert.Equal("sample.literal-code", recommendation.RuleId);
        Assert.Equal(KnowledgeImpact.High, recommendation.Impact);
        Assert.Equal(MatchConfidence.High, recommendation.Confidence);
        Assert.Equal(2, recommendation.Evidence.Count);
        Assert.Contains("não causalidade", recommendation.ConfidenceExplanation);
        Assert.Contains("0xAABBCCDD", recommendation.Explanation);
    }

    [Fact]
    public void RecommendationEngineLeavesConfidenceIndeterminateWhenThereIsNoMatch()
    {
        var report = CreateReport(Finding("Drivers", "Hardware", "Falha", "Código 0x11223344"));
        Assert.Empty(new RecommendationEngine().Recommend(report, []));
    }

    [Fact]
    public void RootCauseAnalyzerReportsOnlySharedIdentifiersAndNeverDeterminesCause()
    {
        var report = CreateReport(
            Finding("Windows Update", "Sistema", "Evento de atualização", "KB1234567; registro observado"),
            Finding("Drivers", "Hardware", "Dispositivo com falha", "O registro cita KB1234567"));

        var analysis = new RootCauseAnalyzer().Analyze(report);

        var correlation = Assert.Single(analysis.Correlations);
        Assert.Equal("KB1234567", correlation.SharedIdentifier, ignoreCase: true);
        Assert.Equal(2, correlation.Evidence.Count);
        Assert.False(analysis.CauseDetermined);
        Assert.Contains("não provam causalidade", analysis.Summary);
    }

    [Fact]
    public void RootCauseAnalyzerLinksAnExplicitServiceNameAcrossScannerSources()
    {
        var report = CreateReport(
            Finding("Services", "Sistema", "Serviço crítico parado", "Nome=RpcSs; estado=Stopped."),
            Finding("Event Viewer", "Sistema", "Error · Service Control Manager · evento 7034", "O serviço RpcSs terminou; evento 7034."));

        var analysis = new RootCauseAnalyzer().Analyze(report);

        var correlation = Assert.Single(analysis.Correlations, item => item.SharedIdentifier.StartsWith("service:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("service:RpcSs", correlation.SharedIdentifier, ignoreCase: true);
        Assert.Equal(2, correlation.Evidence.Count);
        Assert.False(analysis.CauseDetermined);
    }

    [Fact]
    public void RootCauseAnalyzerDoesNotInventAssociationsFromUnrelatedFindings()
    {
        var analysis = new RootCauseAnalyzer().Analyze(CreateReport(
            Finding("Services", "Sistema", "Serviço interrompido", "Nome: TestService"),
            Finding("Disk", "Hardware", "Pouco espaço", "Volume local observado")));

        Assert.Empty(analysis.Correlations);
        Assert.False(analysis.CauseDetermined);
        Assert.Contains("indeterminada", analysis.Summary);
    }

    [Fact]
    public async Task ImporterPersistsRuleAndPackageVersionsInSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var repository = new SqliteKnowledgeRepository(context);
        var importer = new KnowledgeJsonImporter(repository);

        var imported = await importer.ImportAsync(ValidPackageJson);
        var repeated = await importer.ImportAsync(ValidPackageJson);
        var nextVersionJson = ValidPackageJson
            .Replace("fixture-v1", "fixture-v2", StringComparison.Ordinal)
            .Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal);
        var nextVersion = await importer.ImportAsync(nextVersionJson);
        var rules = await repository.GetLatestRulesAsync();

        Assert.Equal("fixture-v1", imported.Version);
        Assert.Equal(1, imported.ImportedRules);
        Assert.Equal(imported.Sha256, repeated.Sha256);
        Assert.Equal("fixture-rule", Assert.Single(rules).Id);
        Assert.Equal("fixture-v2", nextVersion.Version);
        Assert.Equal(2, rules[0].Version);
        Assert.Equal(2, await context.KnowledgeRules.CountAsync());
        Assert.Equal(2, await context.KnowledgeBaseVersions.CountAsync());

        var audit = new SqliteRepairAuditLog(context);
        var now = DateTimeOffset.UtcNow;
        var history = new RepairHistoryRecord(Guid.NewGuid(), "fixture.inert", "Fixture", RepairExecutionStatus.Declined,
            RepairRiskLevel.Low, false, false, now, now, "Sem alteração.");
        await audit.SaveAsync(history);
        var persistedHistory = Assert.Single(await audit.GetRecentAsync(10));
        Assert.Equal(RepairExecutionStatus.Declined, persistedHistory.Status);
        Assert.False(persistedHistory.UserConfirmed);
    }

    [Theory]
    [InlineData("\"command\":\"whoami\"")]
    [InlineData("\"solutions\":[\"whoami && del C:\\\\temp\\\\x\"]")]
    [InlineData("\"solutions\":[\"/etc/passwd\"]")]
    public async Task ImporterRejectsUnknownExecutableFieldsAndCommandLikeText(string injectedProperty)
    {
        var json = ValidPackageJson.Replace("\"references\"", $"{injectedProperty},\"references\"", StringComparison.Ordinal);
        var importer = new KnowledgeJsonImporter(new NoOpKnowledgeRepository());

        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(json));
    }

    [Fact]
    public async Task ImporterRejectsNonHttpsReferencesAndUnknownSchemaVersions()
    {
        var importer = new KnowledgeJsonImporter(new NoOpKnowledgeRepository());
        var localReference = ValidPackageJson.Replace("https://example.invalid/docs", "file:///etc/passwd", StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(localReference));
        var futureSchema = ValidPackageJson.Replace("\"schemaVersion\": \"1.0\"", "\"schemaVersion\": \"99.0\"", StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(futureSchema));
    }

    [Fact]
    public async Task DatabaseMigratorUpgradesAnEnsureCreatedStyleDatabaseAndTracksSchemaVersion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var createLegacy = connection.CreateCommand())
        {
            createLegacy.CommandText = """
                CREATE TABLE "DiagnosticRuns" ("Id" TEXT NOT NULL CONSTRAINT "PK_DiagnosticRuns" PRIMARY KEY, "CompletedAtUnixMilliseconds" INTEGER NOT NULL, "PayloadJson" TEXT NOT NULL);
                CREATE TABLE "UserSettings" ("Id" INTEGER NOT NULL CONSTRAINT "PK_UserSettings" PRIMARY KEY, "SaveDiagnosticHistory" INTEGER NOT NULL);
                INSERT INTO "UserSettings" ("Id", "SaveDiagnosticHistory") VALUES (1, 1);
                """;
            await createLegacy.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);

        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);

        await using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT (SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='KnowledgeRules'), PRAGMA_USER_VERSION();";
        // SQLite does not expose PRAGMA as a scalar function, so verify the new tables and version separately.
        verify.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='KnowledgeRules';";
        Assert.Equal(1L, (long)(await verify.ExecuteScalarAsync())!);
        verify.CommandText = "PRAGMA user_version;";
        Assert.Equal(WindowsDoctorDatabaseMigrator.CurrentVersion, Convert.ToInt32(await verify.ExecuteScalarAsync()));
        verify.CommandText = "SELECT \"SaveDiagnosticHistory\" FROM \"UserSettings\" WHERE \"Id\"=1;";
        Assert.Equal(1L, (long)(await verify.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task RepairEngineNeverCallsPluginUntilUserConfirmsAndLogsEachAttempt()
    {
        var plugin = new TestRepairPlugin();
        var audit = new InMemoryRepairAuditLog();
        var engine = new RepairEngine([plugin], audit);

        var declined = await engine.ExecuteAsync(plugin.Proposal.Id, userConfirmed: false);
        Assert.Equal(RepairExecutionStatus.Declined, declined.Status);
        Assert.Equal(0, plugin.ExecuteCount);
        Assert.False(declined.UserConfirmed);

        var confirmed = await engine.ExecuteAsync(plugin.Proposal.Id, userConfirmed: true);
        Assert.Equal(RepairExecutionStatus.Succeeded, confirmed.Status);
        Assert.Equal(1, plugin.ExecuteCount);
        Assert.Equal(2, audit.Records.Count);

        var unsupportedRollback = await engine.RollbackAsync(plugin.Proposal.Id, userConfirmed: true);
        Assert.Equal(RepairExecutionStatus.NotImplemented, unsupportedRollback.Status);
        Assert.Equal(0, plugin.RollbackCount);
        Assert.Equal(3, audit.Records.Count);
    }

    [Fact]
    public void HtmlReportEscapesUntrustedTextAndIncludesScoreEvidenceRecommendationsAndActionHistory()
    {
        var malicious = "<script>alert('x')</script>";
        var run = new DiagnosticRun(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1), new ComputerInventory(), CreateReport(Finding("Scanner", "Sistema", malicious, malicious)));
        var recommendation = new DiagnosticRecommendation("r", 1, malicious, "Sistema", KnowledgeImpact.Moderate,
            MatchConfidence.Low, "Match textual fraco.", "Correspondência", ["causa"], ["solução"],
            [new RecommendationEvidence("Scanner", "Sistema", malicious, malicious, DateTimeOffset.UtcNow, "termo")],
            [new KnowledgeReference("Docs", "https://example.invalid/docs")]);
        var history = new RepairHistoryRecord(Guid.NewGuid(), "demo", "Demonstração", RepairExecutionStatus.Declined,
            RepairRiskLevel.Low, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Nenhuma alteração.");
        var html = new HtmlDiagnosticReportFormatter().Format(run, [recommendation],
            new RootCauseAnalysis("Causa indeterminada.", []), [history]);

        Assert.Contains("Health Score", html);
        Assert.Contains("Evidências diagnósticas", html);
        Assert.Contains("Recomendações", html);
        Assert.Contains("Histórico de propostas de reparo", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain(malicious, html);
        Assert.Contains("https://example.invalid/docs", html);
    }

    private static DiagnosticResult Finding(string scanner, string category, string title, string evidence) => new(
        scanner, category, DiagnosticSeverity.Warning, DiagnosticStatus.Finding, title, "Descrição", "Ação manual", evidence,
        TimeSpan.FromMilliseconds(10), DateTimeOffset.UtcNow);

    private static DiagnosticReport CreateReport(params DiagnosticResult[] results) => new(
        results, DateTimeOffset.UtcNow.AddSeconds(-2), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2), new HealthScore(80));

    private static KnowledgeRule Rule(string id, IReadOnlyList<string> codes, IReadOnlyList<string> symptoms, KnowledgeImpact impact) => new(
        id, 1, "Fixture", "Regra de teste sem afirmação factual", impact, codes, symptoms,
        ["causa de teste"], ["solução descritiva de teste"], [new KnowledgeReference("Fonte de fixture", "https://example.invalid/docs")]);

    private const string ValidPackageJson = """
        {
          "schemaVersion": "1.0",
          "version": "fixture-v1",
          "source": "fixture local de teste",
          "rules": [
            {
              "id": "fixture-rule",
              "version": 1,
              "domain": "Fixture",
              "title": "Regra declarativa de teste",
              "impact": "moderate",
              "errorCodes": ["0xAABBCCDD"],
              "symptoms": ["sintoma de fixture"],
              "causes": ["causa de fixture"],
              "solutions": ["revisão manual de fixture"],
              "references": [{ "title": "Documento de fixture", "url": "https://example.invalid/docs" }]
            }
          ]
        }
        """;

    private sealed class NoOpKnowledgeRepository : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeRule>>([]);
        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class InMemoryRepairAuditLog : IRepairAuditLog
    {
        public List<RepairHistoryRecord> Records { get; } = [];
        public Task SaveAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<RepairHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepairHistoryRecord>>(Records.Take(count).ToArray());
    }

    private sealed class TestRepairPlugin : IRepairPlugin
    {
        public RepairProposal Proposal { get; } = new("test.inert", "Fake inerte", "Somente teste sem mudança de sistema.",
            RepairRiskLevel.Low, "Nenhum impacto real.", SupportsRollback: false);
        public int ExecuteCount { get; private set; }
        public int RollbackCount { get; private set; }
        public Task<string> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult("fake executado somente em memória");
        }
        public Task<string> RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.FromResult("fake rollback");
        }
    }
}
