using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    [Fact]
    public async Task ImporterPreviewShowsDeclaredMetadataAndHashWithoutPersisting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var importer = new KnowledgeJsonImporter(new SqliteKnowledgeRepository(context));

        var preview = importer.Preview(ValidPackageJson);

        Assert.Equal("fixture-v1", preview.Version);
        Assert.Equal("fixture local de teste", preview.Source);
        Assert.Equal(1, preview.RuleCount);
        Assert.Equal(64, preview.Sha256.Length);
        Assert.Equal(0, await context.KnowledgeRules.CountAsync());
        Assert.Equal(0, await context.KnowledgeBaseVersions.CountAsync());
    }

    [Fact]
    public async Task InvalidPackageIsRejectedBeforeAnySqliteWrite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var importer = new KnowledgeJsonImporter(new SqliteKnowledgeRepository(context));
        var invalidJson = ValidPackageJson.Replace("\"impact\": \"moderate\"", "\"impact\": \"not-an-impact\"", StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(invalidJson));

        Assert.Equal(0, await context.KnowledgeRules.CountAsync());
        Assert.Equal(0, await context.KnowledgeBaseVersions.CountAsync());
    }

    [Fact]
    public async Task ConflictingRuleRejectsWholePackageAndLeavesContextCleanForLaterImport()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var repository = new SqliteKnowledgeRepository(context);
        var importer = new KnowledgeJsonImporter(repository);
        await importer.ImportAsync(ValidPackageJson);
        var conflictingPackage = CreatePackageJson("fixture-v2",
            Rule("new-before-conflict", [], [], KnowledgeImpact.Low),
            Rule("fixture-rule", ["0xDIFFERENT"], [], KnowledgeImpact.Moderate));

        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportAsync(conflictingPackage));

        Assert.Equal(1, await context.KnowledgeRules.CountAsync());
        Assert.Equal(1, await context.KnowledgeBaseVersions.CountAsync());
        Assert.DoesNotContain(await context.KnowledgeRules.ToListAsync(), item => item.RuleId == "new-before-conflict");

        await importer.ImportAsync(CreatePackageJson("fixture-v3", Rule("recovered-rule", [], [], KnowledgeImpact.Low)));
        Assert.Equal(2, await context.KnowledgeRules.CountAsync());
        Assert.Equal(2, await context.KnowledgeBaseVersions.CountAsync());
        Assert.DoesNotContain(await context.KnowledgeRules.ToListAsync(), item => item.PackageVersion == "fixture-v2");
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
        Assert.Equal(0L, (long)(await verify.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task RepairEngineNeverCallsPluginUntilUserConfirmsAndLogsEachAttempt()
    {
        var plugin = new TestRepairPlugin();
        var audit = new InMemoryRepairAuditLog();
        var engine = CreateEngine(plugin, audit);

        var declined = await engine.ExecuteAsync(plugin.Proposal.Id, consent: null);
        Assert.Equal(RepairExecutionStatus.Declined, declined.Status);
        Assert.Equal(0, plugin.ExecuteCount);
        Assert.False(declined.UserConfirmed);

        var confirmed = await engine.ExecuteAsync(plugin.Proposal.Id, RepairConsent.Confirm(plugin.Proposal));
        Assert.Equal(RepairExecutionStatus.Succeeded, confirmed.Status);
        Assert.Equal(1, plugin.ExecuteCount);
        Assert.Equal(2, audit.Records.Count);

        var unsupportedRollback = await engine.RollbackAsync(confirmed.RepairExecutionId,
            RepairConsent.ConfirmRollback(plugin.Proposal, confirmed.RepairExecutionId));
        Assert.Equal(RepairExecutionStatus.NotImplemented, unsupportedRollback.Status);
        Assert.Equal(0, plugin.RollbackCount);
        Assert.Equal(3, audit.Records.Count);
    }

    [Fact]
    public async Task AssessmentServiceComposesHtmlWithoutUnscopedRepairHistory()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new DiagnosticRun(Guid.NewGuid(), now.AddMinutes(-1), now, TimeSpan.FromMinutes(1), new ComputerInventory(),
            CreateReport(Finding("Windows Update", "Sistema", "Evento observado", "Falha 0xAABBCCDD")));
        var rule = Rule("fixture-rule", ["0xAABBCCDD"], [], KnowledgeImpact.High);
        Assert.Single(new RecommendationEngine().Recommend(run.Report!, [rule]));
        var knowledge = new InMemoryKnowledgeRepository([rule]);
        var audit = new InMemoryRepairAuditLog();
        await audit.SaveAsync(new RepairHistoryRecord(Guid.NewGuid(), "fixture.inert", "Proposta de teste", RepairExecutionStatus.Declined,
            RepairRiskLevel.Low, false, false, now, now, "Ação recusada; nenhuma alteração."));
        var service = new DiagnosticAssessmentService(knowledge, new RecommendationEngine(), new RootCauseAnalyzer(), new HtmlDiagnosticReportFormatter());

        var html = await service.CreateHtmlReportAsync(run);

        Assert.Contains("Health Score: 80", html);
        Assert.Contains("Falha 0xAABBCCDD", html);
        Assert.Contains("Regra de teste sem afirma&#231;&#227;o factual", html);
        Assert.Contains("força do match literal", html);
        Assert.DoesNotContain("Histórico de propostas de reparo", html);
        Assert.DoesNotContain("Proposta de teste", html);
        Assert.Single(audit.Records);
        Assert.Contains("autoria autenticada", html);
    }

    [Fact]
    public async Task AssessmentServiceExplicitlyReportsWhenKnowledgeBaseIsEmpty()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new DiagnosticRun(Guid.NewGuid(), now.AddMinutes(-1), now, TimeSpan.FromMinutes(1), new ComputerInventory(),
            CreateReport(Finding("Scanner", "Sistema", "Achado", "Evidência local")));
        var service = new DiagnosticAssessmentService(new InMemoryKnowledgeRepository([]),
            new RecommendationEngine(), new RootCauseAnalyzer(), new HtmlDiagnosticReportFormatter());

        var html = await service.CreateHtmlReportAsync(run);

        Assert.Contains("Knowledge Base está vazia", html);
        Assert.Contains("Nenhuma recomendação de conhecimento foi gerada", html);
    }

    [Fact]
    public void HtmlReportRedactsAndEscapesUntrustedTextWithoutRepairHistory()
    {
        var malicious = "<script>alert('x')</script>";
        var run = new DiagnosticRun(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1), new ComputerInventory(), CreateReport(Finding("Scanner", "Sistema", malicious, malicious)));
        var recommendation = new DiagnosticRecommendation("r", 1, malicious, "Sistema", KnowledgeImpact.Moderate,
            MatchConfidence.Low, "Match textual fraco.", "Correspondência", ["causa"], ["solução"],
            [new RecommendationEvidence("Scanner", "Sistema", malicious, malicious, DateTimeOffset.UtcNow, "termo")],
            [new KnowledgeReference("Docs", "https://example.invalid/docs")]);
        var html = new HtmlDiagnosticReportFormatter().Format(run, [recommendation],
            new RootCauseAnalysis("Causa indeterminada.", []));

        Assert.Contains("Health Score", html);
        Assert.Contains("Evidências diagnósticas", html);
        Assert.Contains("Recomendações", html);
        Assert.DoesNotContain("Histórico de propostas de reparo", html);
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

    private static string CreatePackageJson(string version, params KnowledgeRule[] rules) => JsonSerializer.Serialize(
        new KnowledgePackage("1.0", version, "fixture local de teste", rules),
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } });

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

    private sealed class InMemoryKnowledgeRepository(IReadOnlyList<KnowledgeRule> rules) : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) => Task.FromResult(rules);
        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryRepairAuditLog : IRepairAuditLog
    {
        public List<RepairHistoryRecord> Records { get; } = [];
        private readonly HashSet<Guid> _consumedConsents = [];
        private readonly HashSet<string> _quarantinedFindings = new(StringComparer.Ordinal);
        public Task SaveAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default)
        {
            Records.RemoveAll(existing => existing.RepairExecutionId == record.RepairExecutionId);
            Records.Add(record);
            return Task.CompletedTask;
        }
        public async Task<RepairConsentAttemptResult> TrySaveConsentAttemptAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default)
        {
            if (record.ConsentId is { } consentId && !_consumedConsents.Add(consentId))
                return new RepairConsentAttemptResult(RepairConsentAttemptStatus.Replay);
            var key = QuarantineKey(record);
            if (key is not null && _quarantinedFindings.Contains(key))
            {
                var blocked = record with { Status = RepairExecutionStatus.Declined,
                    Details = "Finding em quarentena após Started; execute novo diagnóstico." };
                await SaveAsync(blocked, cancellationToken);
                return new RepairConsentAttemptResult(RepairConsentAttemptStatus.Quarantined, blocked);
            }
            await SaveAsync(record, cancellationToken);
            return new RepairConsentAttemptResult(RepairConsentAttemptStatus.Saved, record);
        }
        public async Task<bool> TryMarkStartedAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default)
        {
            var key = QuarantineKey(record);
            if (key is null || !_quarantinedFindings.Add(key)) return false;
            await SaveAsync(record, cancellationToken);
            return true;
        }
        public Task<IReadOnlyList<RepairHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepairHistoryRecord>>(Records.Take(count).ToArray());

        private static string? QuarantineKey(RepairHistoryRecord record) => record.DiagnosticRunId is { } runId
            && !string.IsNullOrWhiteSpace(record.FindingIdentity)
            ? $"{runId:D}:{record.FindingIdentity}:{record.Action}"
            : null;
    }

    private static RepairEngine CreateEngine(IRepairPlugin plugin, InMemoryRepairAuditLog audit)
    {
        var gate = new RepairEvidenceGate();
        var lease = gate.AcquireAsync().AsTask().GetAwaiter().GetResult();
        lease.MarkDiagnosticRunCurrent(plugin.Proposal.DiagnosticRunId, plugin.Proposal.EvidenceGeneration);
        lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return new RepairEngine([plugin], audit, new FixturePreconditionEvaluator(), new FixtureAllowlist(plugin.Proposal), gate);
    }

    private sealed class FixtureAllowlist(RepairProposal proposal) : IRepairProposalAllowlist
    {
        private readonly RepairProposalDefinition _definition = new(
            proposal.RuleId, proposal.RuleVersion, proposal.Kind, proposal.PlanVersion, proposal.OperationVersion,
            proposal.Id, proposal.Title, proposal.Description, proposal.Risk, proposal.Impact, proposal.Target,
            proposal.StructuredPreconditions, proposal.StructuredPostconditions, proposal.SupportsRollback,
            proposal.StructuredRollbackPreconditions, proposal.StructuredRollbackPostconditions);

        public bool TryGetDefinition(string ruleId, int ruleVersion, out RepairProposalDefinition? definition)
        {
            definition = string.Equals(ruleId, _definition.RuleId, StringComparison.Ordinal)
                && ruleVersion == _definition.RuleVersion ? _definition : null;
            return definition is not null;
        }
    }

    private sealed class FixturePreconditionEvaluator : IRepairPreconditionEvaluator
    {
        public Task<IReadOnlyList<RepairConditionResult>> EvaluateAsync(
            RepairProposal proposal, RepairAction action, Guid? relatedRepairExecutionId,
            CancellationToken cancellationToken = default)
        {
            var conditions = action == RepairAction.Rollback
                ? proposal.StructuredRollbackPreconditions : proposal.StructuredPreconditions;
            return Task.FromResult<IReadOnlyList<RepairConditionResult>>(conditions.Select(condition =>
                new RepairConditionResult(condition.Kind, RepairConditionStatus.Verified)).ToArray());
        }
    }

    private sealed class TestRepairPlugin : IRepairPlugin
    {
        public RepairProposal Proposal { get; } = new RepairProposal("test.inert", "Fake inerte", "Somente teste sem mudança de sistema.",
            RepairRiskLevel.Low, "Nenhum impacto real.", SupportsRollback: false, PlanVersion: 1,
            Target: "Fake local; sem alvo do sistema",
            Preconditions:
            [
                "A execução diagnóstica vinculada ainda é a atual.",
                "O achado identificado ainda existe na execução vinculada.",
                "A mesma versão da regra continua vigente.",
                "A evidência redigida continua correspondendo à regra vinculada.",
                "O tipo de plano e a regra estão na allowlist compilada."
            ],
            Postconditions: ["A simulação terminou sem alterar o sistema."])
        {
            Kind = RepairProposalKind.NoOpSimulation,
            OperationVersion = 1,
            DiagnosticRunId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            FindingIdentity = new string('b', 64),
            RuleId = "test.inert.rule",
            RuleVersion = 1,
            RedactedEvidence = "Fixture sem dados de host.",
            EvidenceFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Fixture sem dados de host."))).ToLowerInvariant(),
            StructuredPreconditions =
            [
                new(RepairConditionKind.DiagnosticRunIsCurrent),
                new(RepairConditionKind.FindingIsPresent),
                new(RepairConditionKind.RuleVersionIsCurrent),
                new(RepairConditionKind.FindingMatchesRule),
                new(RepairConditionKind.RuleIsAllowlisted)
            ],
            StructuredPostconditions = [new(RepairConditionKind.SimulationCompletedWithoutSystemChanges)]
        };
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
        public Task<RepairPostconditionReport> VerifyPostconditionsAsync(
            RepairAction action, Guid repairExecutionId, Guid? relatedRepairExecutionId,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new RepairPostconditionReport(RepairPostconditionStatus.Verified, "Simulação fake verificada.")
                {
                    Conditions = Proposal.StructuredPostconditions.Select(condition =>
                        new RepairConditionResult(condition.Kind, RepairConditionStatus.Verified)).ToArray()
                });
    }
}
