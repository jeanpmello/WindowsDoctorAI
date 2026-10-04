using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Database;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class OperatingSystemApplicabilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new KnowledgeOperatingSystemFamilyJsonConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        }
    };

    [Fact]
    public void OperatingSystemWmiQueryIncludesProductType()
    {
        Assert.Contains("BuildNumber", WindowsOperatingSystemMetadata.WmiQuery, StringComparison.Ordinal);
        Assert.Contains("ProductType", WindowsOperatingSystemMetadata.WmiQuery, StringComparison.Ordinal);
        Assert.Contains("Win32_OperatingSystem", WindowsOperatingSystemMetadata.WmiQuery, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", OperatingSystemProductType.Workstation)]
    [InlineData("2", OperatingSystemProductType.DomainController)]
    [InlineData("3", OperatingSystemProductType.Server)]
    public void WmiProductTypeMapsOnlyDocumentedCanonicalValues(string raw, OperatingSystemProductType expected)
    {
        Assert.Equal(expected, WindowsOperatingSystemMetadata.ParseProductType(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("01")]
    [InlineData(" 1")]
    [InlineData("client")]
    public void UnknownOrMalformedWmiProductTypeRemainsNull(string? raw)
    {
        Assert.Null(WindowsOperatingSystemMetadata.ParseProductType(raw));
    }

    [Theory]
    [InlineData("22621", true)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("+22621", false)]
    [InlineData("022621", false)]
    [InlineData("22621.0", false)]
    [InlineData(" 22621", false)]
    [InlineData("22621 ", false)]
    [InlineData("not-a-build", false)]
    [InlineData("9223372036854775808", false)]
    [InlineData(null, false)]
    public void BuildNumberMustBeCanonicalPositiveInvariantDecimal(string? raw, bool expectedValid)
    {
        Assert.Equal(expectedValid, OperatingSystemBuildNumber.TryParseCanonicalPositive(raw, out _));
        Assert.Equal(expectedValid ? raw : null, OperatingSystemBuildNumber.Normalize(raw));
    }

    [Fact]
    public void ProductTypeOneMatchesClientAndTypesTwoAndThreeMatchServer()
    {
        var engine = new RecommendationEngine();
        var report = MatchingReport();
        var clientRule = Rule("client-target", Target([KnowledgeOperatingSystemFamily.WindowsClient]));
        var serverRule = Rule("server-target", Target([KnowledgeOperatingSystemFamily.WindowsServer]));

        Assert.Single(engine.Recommend(report, [clientRule], Inventory(OperatingSystemProductType.Workstation, "22621")));
        Assert.Empty(engine.Recommend(report, [clientRule], Inventory(OperatingSystemProductType.DomainController, "22621")));
        Assert.Empty(engine.Recommend(report, [clientRule], Inventory(OperatingSystemProductType.Server, "22621")));
        Assert.Empty(engine.Recommend(report, [serverRule], Inventory(OperatingSystemProductType.Workstation, "22621")));
        Assert.Single(engine.Recommend(report, [serverRule], Inventory(OperatingSystemProductType.DomainController, "22621")));
        Assert.Single(engine.Recommend(report, [serverRule], Inventory(OperatingSystemProductType.Server, "22621")));
    }

    [Theory]
    [InlineData("22000", true)]
    [InlineData("22631", true)]
    [InlineData("21999", false)]
    [InlineData("22632", false)]
    public void BuildBoundsAreInclusiveAndOutOfRangeFailsClosed(string build, bool expectedMatch)
    {
        var rule = Rule("bounded", Target([KnowledgeOperatingSystemFamily.WindowsClient], 22000, 22631));
        var result = new RecommendationEngine().Recommend(MatchingReport(), [rule], Inventory(OperatingSystemProductType.Workstation, build));
        Assert.Equal(expectedMatch, result.Count == 1);
    }

    [Fact]
    public void MissingUnknownInvalidProductTypeOrInvalidBuildNeverMatchesSchema14()
    {
        var rule = Rule("fail-closed", Target([KnowledgeOperatingSystemFamily.WindowsClient]));
        var engine = new RecommendationEngine();
        var report = MatchingReport();
        var invalidInventories = new ComputerInventory?[]
        {
            null,
            Inventory(null, "22621"),
            Inventory((OperatingSystemProductType)99, "22621"),
            Inventory(OperatingSystemProductType.Workstation, null),
            Inventory(OperatingSystemProductType.Workstation, "0"),
            Inventory(OperatingSystemProductType.Workstation, "-1"),
            Inventory(OperatingSystemProductType.Workstation, "+22621"),
            Inventory(OperatingSystemProductType.Workstation, "022621"),
            Inventory(OperatingSystemProductType.Workstation, "22621.0"),
            Inventory(OperatingSystemProductType.Workstation, "malformed")
        };

        foreach (var inventory in invalidInventories)
            Assert.Empty(engine.Recommend(report, [rule], inventory));
    }

    [Fact]
    public void LegacyRuleStillMatchesWithoutInventoryAndTextApplicabilityIsNotUsedAsFilter()
    {
        var legacyRule = new KnowledgeRule("legacy", 1, "Fixture", "Regra legada", KnowledgeImpact.Moderate,
            ["0xAABBCCDD"], [], [], [], [new KnowledgeReference("Fixture", "https://example.invalid/docs")],
            "Aplicável somente a um sistema que não corresponde ao inventário.");

        var recommendation = Assert.Single(new RecommendationEngine().Recommend(MatchingReport(), [legacyRule]));

        Assert.Null(recommendation.OsTarget);
        Assert.Contains("Regra legada", recommendation.Explanation, StringComparison.Ordinal);
        Assert.Contains("não foi verificada automaticamente", recommendation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema14RequiresValidExplicitTargetAndOlderSchemasRejectOsTarget()
    {
        var importer = new KnowledgeJsonImporter(new InMemoryKnowledgeRepository([]));
        var validTarget = Target([KnowledgeOperatingSystemFamily.WindowsClient], 22000, 22631);
        Assert.Equal(1, importer.Preview(CreatePackage("1.4", Rule("valid-14", validTarget))).RuleCount);
        Assert.Throws<InvalidDataException>(() => importer.Preview(CreatePackage("1.4", Rule("missing-target", null))));

        var invalidSchema10 = LegacyRule("legacy-with-target") with { OsTarget = validTarget };
        Assert.Throws<InvalidDataException>(() => importer.Preview(CreatePackage("1.0", invalidSchema10)));
        foreach (var schema in new[] { "1.1", "1.2", "1.3" })
            Assert.Throws<InvalidDataException>(() => importer.Preview(CreatePackage(schema, Rule($"old-{schema}", validTarget))));
    }

    [Fact]
    public void Schema14RejectsInvertedNonPositiveDuplicateAndUnknownTargets()
    {
        var importer = new KnowledgeJsonImporter(new InMemoryKnowledgeRepository([]));
        var invalidTargets = new[]
        {
            Target([KnowledgeOperatingSystemFamily.WindowsClient], 22631, 22000),
            Target([KnowledgeOperatingSystemFamily.WindowsClient], 0, 22000),
            Target([KnowledgeOperatingSystemFamily.WindowsClient], 22000, -1),
            Target([KnowledgeOperatingSystemFamily.WindowsClient, KnowledgeOperatingSystemFamily.WindowsClient]),
            Target([], null, null)
        };
        foreach (var target in invalidTargets)
            Assert.Throws<InvalidDataException>(() => importer.Preview(CreatePackage("1.4", Rule("invalid-target", target))));

        var validJson = CreatePackage("1.4", Rule("unknown-family", Target([KnowledgeOperatingSystemFamily.WindowsClient])));
        var unknownFamily = validJson.Replace("WindowsClient", "WindowsServer2022", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => importer.Preview(unknownFamily));
        var numericFamily = validJson.Replace("\"WindowsClient\"", "1", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => importer.Preview(numericFamily));
    }

    [Fact]
    public async Task InvalidSchema14ImportDoesNotWriteAnyPartOfPackage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var importer = new KnowledgeJsonImporter(new SqliteKnowledgeRepository(context));
        var goodRule = Rule("valid-first", Target([KnowledgeOperatingSystemFamily.WindowsClient]));
        var invalidRule = Rule("missing-second", null);
        var json = CreatePackage("1.4", goodRule, invalidRule);

        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportAsync(json));

        Assert.Equal(0, await context.KnowledgeRules.CountAsync());
        Assert.Equal(0, await context.KnowledgeBaseVersions.CountAsync());
    }

    [Fact]
    public async Task ComplementarySchema14PackageUpdatesOnly80073712AndEngineVerifiesFamilyAndBuild()
    {
        var historicalJson = ReadKnowledgePack("microsoft-windows-update-pilot.json");
        var updateJson = ReadKnowledgePack("microsoft-windows-update-pilot-1.4.json");
        var historicalPackage = JsonSerializer.Deserialize<KnowledgePackage>(historicalJson, JsonOptions)!;
        var updatePackage = JsonSerializer.Deserialize<KnowledgePackage>(updateJson, JsonOptions)!;
        Assert.Equal("1.4", updatePackage.SchemaVersion);
        Assert.Equal(2, updatePackage.Rules.Count);
        Assert.All(updatePackage.Rules, rule => Assert.Equal("0x80073712", rule.Match!.ExactErrorCode));
        Assert.DoesNotContain(updatePackage.Rules,
            rule => string.Equals(rule.Match?.ExactErrorCode, "0xC1900107", StringComparison.OrdinalIgnoreCase));

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var repository = new SqliteKnowledgeRepository(context);
        var importer = new KnowledgeJsonImporter(repository);

        await importer.ImportAsync(historicalJson);
        var importedUpdate = await importer.ImportAsync(updateJson);
        var latestRules = await repository.GetLatestRulesAsync();
        var clientRule = Assert.Single(latestRules, rule => rule.Id == "microsoft.windows-update.0x80073712");
        var serverRule = Assert.Single(latestRules, rule => rule.Id == "microsoft.windows-update.0x80073712.server");
        var setupRule = Assert.Single(latestRules, rule => rule.Id == "microsoft.windows-setup.0xc1900107");
        var historicalUpdate = Assert.Single(historicalPackage.Rules,
            rule => rule.Id == "microsoft.windows-update.0x80073712");

        Assert.Equal("microsoft-windows-pilot-2026-10-04-v6", importedUpdate.Version);
        Assert.Equal(2, importedUpdate.ImportedRules);
        Assert.Equal(2, clientRule.Version);
        Assert.Equal(2, serverRule.Version);
        Assert.Equal(1, setupRule.Version);
        Assert.Equal([KnowledgeOperatingSystemFamily.WindowsClient], clientRule.OsTarget!.Families);
        Assert.Equal(10240, clientRule.OsTarget.MinimumBuild);
        Assert.Null(clientRule.OsTarget.MaximumBuild);
        Assert.Equal([KnowledgeOperatingSystemFamily.WindowsServer], serverRule.OsTarget!.Families);
        Assert.Equal(14393, serverRule.OsTarget.MinimumBuild);
        Assert.Null(serverRule.OsTarget.MaximumBuild);
        Assert.Equal(historicalUpdate.Match!.ExactErrorCode, clientRule.Match!.ExactErrorCode);
        Assert.Equal(historicalUpdate.Match.ScannerNames, clientRule.Match.ScannerNames);
        Assert.Equal(historicalUpdate.Match.RequiredSourceProviders, clientRule.Match.RequiredSourceProviders);
        Assert.Equal(historicalUpdate.RequiredEvidence, clientRule.RequiredEvidence);
        Assert.Equal(historicalUpdate.Procedure!.DiagnosticAction, clientRule.Procedure!.DiagnosticAction);
        Assert.Equal(historicalUpdate.Procedure.CorrectiveAction, clientRule.Procedure.CorrectiveAction);
        Assert.Equal(historicalUpdate.Procedure.RequiredPrivilege, clientRule.Procedure.RequiredPrivilege);
        Assert.Equal(historicalUpdate.Procedure.RequiresElevation, clientRule.Procedure.RequiresElevation);
        Assert.Equal(historicalUpdate.Procedure.Risk, clientRule.Procedure.Risk);
        Assert.Equal(historicalUpdate.Procedure.Backup, clientRule.Procedure.Backup);
        Assert.Equal(historicalUpdate.Procedure.Rollback, clientRule.Procedure.Rollback);
        Assert.Equal(historicalUpdate.Procedure.SourceLimitation, clientRule.Procedure.SourceLimitation);
        Assert.True(clientRule.Procedure.ManualOnly);
        Assert.True(clientRule.Procedure.RequiresUserConfirmation);
        Assert.Equal(clientRule.Match.ExactErrorCode, serverRule.Match!.ExactErrorCode);
        Assert.Equal(clientRule.Match.RequiredSourceProviders, serverRule.Match.RequiredSourceProviders);
        Assert.Equal(clientRule.RequiredEvidence, serverRule.RequiredEvidence);
        Assert.Equal(clientRule.Procedure.CorrectiveAction, serverRule.Procedure!.CorrectiveAction);
        Assert.True(serverRule.Procedure.ManualOnly);
        Assert.True(serverRule.Procedure.RequiresUserConfirmation);
        Assert.Equal(3, latestRules.Count);
        Assert.Equal(4, await context.KnowledgeRules.CountAsync());
        Assert.Equal(2, await context.KnowledgeBaseVersions.CountAsync());

        var engine = new RecommendationEngine();
        var provider = DiagnosticSourceMetadata.FromEventProvider("Microsoft-Windows-WindowsUpdateClient");
        var report = Error80073712Report(provider);
        Assert.Equal(clientRule.Id, Assert.Single(engine.Recommend(report, [clientRule, serverRule],
            Inventory(OperatingSystemProductType.Workstation, "10240"))).RuleId);
        Assert.Empty(engine.Recommend(report, [clientRule, serverRule],
            Inventory(OperatingSystemProductType.Workstation, "10239")));
        Assert.Equal(clientRule.Id, Assert.Single(engine.Recommend(report, [clientRule, serverRule],
            Inventory(OperatingSystemProductType.Workstation, "22621"))).RuleId);
        Assert.Empty(engine.Recommend(report, [clientRule, serverRule],
            Inventory(OperatingSystemProductType.Server, "14392")));
        Assert.Equal(serverRule.Id, Assert.Single(engine.Recommend(report, [clientRule, serverRule],
            Inventory(OperatingSystemProductType.Server, "14393"))).RuleId);
        Assert.Equal(serverRule.Id, Assert.Single(engine.Recommend(report, [clientRule, serverRule],
            Inventory(OperatingSystemProductType.DomainController, "14393"))).RuleId);
        Assert.Equal(serverRule.Id, Assert.Single(engine.Recommend(report, [clientRule, serverRule],
            Inventory(OperatingSystemProductType.Server, "20348"))).RuleId);
        Assert.Empty(engine.Recommend(report, [clientRule, serverRule], Inventory(null, "22621")));
    }

    [Fact]
    public async Task AssessmentUsesRedactedRunInventoryAndReportDistinguishesVerifiedTargetFromLegacy()
    {
        var targetRule = Rule("targeted", Target([KnowledgeOperatingSystemFamily.WindowsClient], 22000, 23000));
        var legacyRule = LegacyRule("legacy-report") with { Applicability = "Texto legado apenas descritivo." };
        var now = DateTimeOffset.UtcNow;
        var run = new DiagnosticRun(Guid.NewGuid(), now, now, TimeSpan.Zero,
            Inventory(OperatingSystemProductType.Workstation, "22621"), MatchingReport());
        var service = new DiagnosticAssessmentService(new InMemoryKnowledgeRepository([targetRule, legacyRule]),
            new RecommendationEngine(), new RootCauseAnalyzer(), new HtmlDiagnosticReportFormatter());

        var html = await service.CreateHtmlReportAsync(run);

        Assert.Contains("Alvo OS+build estruturado verificado", html, StringComparison.Ordinal);
        Assert.Contains("Regra legada — aplicabilidade não verificada automaticamente", html, StringComparison.Ordinal);
        Assert.Contains("texto explicativo; não usada como filtro", html, StringComparison.Ordinal);
        Assert.Contains("não estabelece causa", html, StringComparison.Ordinal);
        Assert.DoesNotContain("22621", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductType", html, StringComparison.Ordinal);
        Assert.Equal(OperatingSystemProductType.Workstation,
            DiagnosticPrivacyRedactor.RedactInventory(run.Inventory).OperatingSystem.ProductType);
    }

    [Fact]
    public void ProductTypeIsTheOnlyNewOperatingSystemInventoryValueAndIsPreservedByRedaction()
    {
        var inventory = Inventory(OperatingSystemProductType.Server, "20348");
        var redacted = DiagnosticPrivacyRedactor.RedactInventory(inventory);
        Assert.Equal(OperatingSystemProductType.Server, redacted.OperatingSystem.ProductType);
        Assert.Equal("20348", redacted.OperatingSystem.Build);
        Assert.Equal("20348", OperatingSystemBuildNumber.Normalize("20348"));
        Assert.Null(OperatingSystemBuildNumber.Normalize("20348x"));
    }

    [Fact]
    public async Task LocalHistoryPersistsAndRestoresAllowlistedProductTypeInsideInventory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var repository = new SqliteDiagnosticRunRepository(context);
        var now = DateTimeOffset.UtcNow;
        var run = new DiagnosticRun(Guid.NewGuid(), now, now, TimeSpan.Zero,
            Inventory(OperatingSystemProductType.Server, "20348"), MatchingReport());

        await repository.SaveAsync(run);

        var payload = await context.DiagnosticRuns.AsNoTracking().Select(item => item.PayloadJson).SingleAsync();
        var restored = await repository.GetLatestAsync();
        Assert.Contains("\"productType\":3", payload, StringComparison.Ordinal);
        Assert.Equal(OperatingSystemProductType.Server, restored!.Inventory.OperatingSystem.ProductType);
    }

    private static KnowledgeOperatingSystemTarget Target(
        IReadOnlyList<KnowledgeOperatingSystemFamily> families,
        long? minimumBuild = null,
        long? maximumBuild = null) => new(families, minimumBuild, maximumBuild);

    private static KnowledgeRule Rule(string id, KnowledgeOperatingSystemTarget? target) => new(
        id, 1, "Fixture", "Regra estruturada", KnowledgeImpact.Moderate, [], [], [],
        ["Revisar manualmente."], [new KnowledgeReference("Fixture", "https://example.invalid/docs")],
        "Texto explicativo não usado como filtro.",
        new KnowledgeMatchCondition("0xAABBCCDD", ["Windows Update"], ["Windows Setup"]),
        ["Confirmar código e contexto no mesmo achado."],
        new KnowledgeProcedure("Confirmar evidência local.", "Nenhuma ação automática.", "Usuário", false,
            "Risco não avaliado.", "Backup não aplicável.", "Rollback não aplicável.", "Fixture sintética.",
            false, true, true)) { OsTarget = target };

    private static KnowledgeRule LegacyRule(string id) => new(
        id, 1, "Fixture", "Regra legada", KnowledgeImpact.Low, ["0xAABBCCDD"], [], [], [],
        [new KnowledgeReference("Fixture", "https://example.invalid/docs")]);

    private static string CreatePackage(string schema, params KnowledgeRule[] rules) =>
        JsonSerializer.Serialize(new KnowledgePackage(schema, "fixture-v1", "fixture local", rules), JsonOptions);

    private static string ReadKnowledgePack(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "knowledge-packs", fileName));

    private static DiagnosticReport Error80073712Report(DiagnosticSourceMetadata? sourceMetadata) => new(
        [new DiagnosticResult("Windows Update", "Windows Update", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
            "Falha no Windows Update", "0x80073712", "Nenhuma ação executada.", "0x80073712",
            TimeSpan.Zero, DateTimeOffset.UtcNow) { SourceMetadata = sourceMetadata }],
        DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), new HealthScore(80));

    private static ComputerInventory Inventory(OperatingSystemProductType? productType, string? build) =>
        new() { OperatingSystem = new OperatingSystemDetails { ProductType = productType, Build = build } };

    private static DiagnosticReport MatchingReport() => new(
        [new DiagnosticResult("Windows Update", "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
            "Windows Setup observou 0xAABBCCDD", "Windows Setup reported error 0xAABBCCDD.", "Manual", "Windows Setup 0xAABBCCDD",
            TimeSpan.Zero, DateTimeOffset.UtcNow)],
        DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), new HealthScore(80));

    private sealed class InMemoryKnowledgeRepository(IReadOnlyList<KnowledgeRule> rules) : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) => Task.FromResult(rules);
        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
