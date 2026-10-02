using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class KnowledgePilotPackTests
{
    private const string PilotPackPath = "knowledge-packs/microsoft-windows-update-pilot.json";

    [Fact]
    public void PilotPackPreviewValidatesThreeRulesWithoutImportingThem()
    {
        var json = ReadPilotPack();
        var importer = new KnowledgeJsonImporter(new InMemoryKnowledgeRepository());

        var preview = importer.Preview(json);
        var package = DeserializePackage(json);

        Assert.Equal("microsoft-windows-pilot-2026-10-02-v3", preview.Version);
        Assert.Equal(3, preview.RuleCount);
        Assert.Equal(3, package.Rules.Count);
        Assert.Contains("curadoria de primeira parte", preview.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não autenticados criptograficamente", preview.Source, StringComparison.OrdinalIgnoreCase);
        Assert.All(package.Rules, rule =>
        {
            Assert.NotNull(rule.Match);
            Assert.NotNull(rule.Procedure);
            Assert.True(rule.Procedure!.ManualOnly);
            Assert.True(rule.Procedure.RequiresUserConfirmation);
            Assert.Empty(rule.ErrorCodes);
            Assert.Empty(rule.Symptoms);
        });
        Assert.Equal(64, preview.Sha256.Length);
    }

    [Fact]
    public void LegacySchema10RemainsImportCompatibleAndLimitsStayEnforced()
    {
        var importer = new KnowledgeJsonImporter(new InMemoryKnowledgeRepository());
        Assert.Equal(1, importer.Preview(CreateLegacyPackage(1)).RuleCount);
        Assert.Equal(KnowledgeJsonImporter.MaximumRules, importer.Preview(CreateLegacyPackage(KnowledgeJsonImporter.MaximumRules)).RuleCount);
        Assert.Throws<InvalidDataException>(() => importer.Preview(CreateLegacyPackage(KnowledgeJsonImporter.MaximumRules + 1)));
        Assert.Throws<InvalidDataException>(() => importer.Preview(CreateLegacyPackage(1) + new string(' ', KnowledgeJsonImporter.MaximumPackageBytes)));
        Assert.Throws<InvalidDataException>(() => importer.Preview(CreateLegacyPackage(1)
            .Replace("\"errorCodes\":[]", "\"errorCodes\":[\"0x800F0831\"]", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => importer.Preview(CreateLegacyPackage(1)
            .Replace("\"symptoms\":[]", "\"symptoms\":[\"0x800F0831\"]", StringComparison.Ordinal)));
    }

    [Fact]
    public void StrictSchemaRejectsGenericConditionsCommandsScriptsAndMalformedCodes()
    {
        var importer = new KnowledgeJsonImporter(new InMemoryKnowledgeRepository());
        var json = ReadPilotPack();
        var elevenContextTerms = string.Join(", ", Enumerable.Range(0, 11).Select(index => $"\"context-{index}\""));
        var invalidCases = new[]
        {
            json.Replace("\"requiredContextTerms\": [\"Windows Setup\"]", "\"requiredContextTerms\": [\"error\"]", StringComparison.Ordinal),
            json.Replace("\"requiredContextTerms\": [\"Windows Setup\"]",
                $"\"requiredContextTerms\": [{elevenContextTerms}]",
                StringComparison.Ordinal),
            json.Replace("\"exactErrorCode\": \"0xC1900107\"", "\"exactErrorCode\": \"0xC190107\"", StringComparison.Ordinal),
            json.Replace("\"requiredEvidence\": [", "\"command\": \"shutdown /r\", \"requiredEvidence\": [", StringComparison.Ordinal),
            json.Replace("\"requiredEvidence\": [", "\"script\": \"ignored\", \"requiredEvidence\": [", StringComparison.Ordinal),
            json.Replace("\"requiredSourceProviders\": [\"WindowsUpdateClient\"]", "\"requiredSourceProviders\": [\"UnknownProvider\"]", StringComparison.Ordinal),
            json.Replace("\"schemaVersion\": \"1.3\"", "\"schemaVersion\": \"1.2\"", StringComparison.Ordinal),
            json.Replace("\"requiredEvidenceTypes\": [\"ManifestMissing\", \"FailedToResolvePackage\"]", "\"requiredEvidenceTypes\": []", StringComparison.Ordinal),
            json.Replace("\"isModifying\": false", "\"isModifying\": true", StringComparison.Ordinal),
            json.Replace("Confirme o código exato e o contexto de Windows Setup no mesmo achado. Não aplique esta regra a um código isolado ou ocorrido em outro contexto.", "execute script document", StringComparison.Ordinal),
            json.Replace("Confirme o código exato e o contexto de Windows Setup no mesmo achado. Não aplique esta regra a um código isolado ou ocorrido em outro contexto.", "shutdown /r", StringComparison.Ordinal),
            json.Replace("Ação manual, elevada e modificadora: siga o procedimento DISM documentado pela Microsoft. Execute System File Checker (SFC) somente se DISM concluir com sucesso. Se DISM falhar, não prossiga para SFC. Preserve e inspecione CBS.log.", "DISM.exe /Online /Cleanup-Image", StringComparison.Ordinal)
        };

        Assert.All(invalidCases, invalid => Assert.Throws<InvalidDataException>(() => importer.Preview(invalid)));
    }

    [Fact]
    public void StrictRuleMatchesOnlyExactCodeAndRequiredContextInSameScannerFinding()
    {
        var package = DeserializePackage(ReadPilotPack());
        var setupRule = Assert.Single(package.Rules, rule => rule.Match!.ExactErrorCode == "0xC1900107");
        var updateRule = Assert.Single(package.Rules, rule => rule.Match!.ExactErrorCode == "0x80073712");
        var engine = new RecommendationEngine();
        var updateProvider = DiagnosticSourceMetadata.FromEventProvider("Microsoft-Windows-WindowsUpdateClient");

        Assert.Single(engine.Recommend(CreateReport(Finding("Windows Update", "Windows Setup reported 0xC1900107 during upgrade.")), [setupRule]));
        Assert.Empty(engine.Recommend(CreateReport(Finding("Windows Update", "Error 0xC1900107.")), [setupRule]));
        Assert.Empty(engine.Recommend(CreateReport(Finding("Disk", "Windows Setup reported 0xC1900107 during upgrade.")), [setupRule]));
        Assert.Empty(engine.Recommend(CreateReport(
            Finding("Windows Update", "Windows Setup reported an installation issue."),
            Finding("Windows Update", "Error 0xC1900107.")), [setupRule]));
        Assert.Empty(engine.Recommend(CreateReport(Finding("Windows Update", "Windows Setup reported 0xC19001070 during upgrade.")), [setupRule]));
        Assert.Empty(engine.Recommend(CreateReport(Finding("Windows Update", "Windows Setup reported 0xC1900107_suffix during upgrade.")), [setupRule]));

        Assert.NotNull(updateProvider);
        Assert.Equal(["WindowsUpdateClient"], updateRule.Match!.RequiredSourceProviders);
        Assert.Empty(updateRule.Match.RequiredContextTerms);
        Assert.Single(engine.Recommend(CreateReport(Finding("Windows Update", "0x80073712", updateProvider)), [updateRule]));
        Assert.Empty(engine.Recommend(CreateReport(Finding("Windows Update", "WindowsUpdateClient reported 0x80073712.")), [updateRule]));
        Assert.Empty(engine.Recommend(CreateReport(Finding("Windows Update", "WindowsUpdateClient event without code.", updateProvider)), [updateRule]));
        Assert.Empty(engine.Recommend(CreateReport(Finding("Windows Update", "0x80073712", new DiagnosticSourceMetadata("OtherProvider"))), [updateRule]));
    }

    [Fact]
    public void Error800f0831RuleRequiresCorrelatedTypedEvidenceAndIsDiagnosticOnly()
    {
        var package = DeserializePackage(ReadPilotPack());
        var cbsRule = Assert.Single(package.Rules, rule => rule.Match!.ExactErrorCode == "0x800F0831");
        Assert.Equal(["ManifestMissing", "FailedToResolvePackage"], cbsRule.Match!.RequiredEvidenceTypes);
        Assert.Equal(["WindowsUpdateClient"], cbsRule.Match.RequiredSourceProviders);
        Assert.False(cbsRule.Procedure!.IsModifying);
        Assert.False(cbsRule.Procedure.RequiresElevation);
        Assert.True(cbsRule.Procedure.ManualOnly);
        Assert.True(cbsRule.Procedure.RequiresUserConfirmation);
        Assert.Contains("não fornece nem executa reparo", cbsRule.Procedure.CorrectiveAction, StringComparison.OrdinalIgnoreCase);

        const string packageIdentity = "Package_123_for_KB3192392~31bf3856ad364e35~amd64~~6.3.1.4";
        var eventTimestamp = DateTimeOffset.UtcNow;
        var cbsTime = eventTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var sourceText = $"{cbsTime}, Info CBS Store corruption, manifest missing for package: {packageIdentity}\n";
        var windowsUpdateEvent = new DiagnosticEvent(20,
            WindowsUpdateCbsLogAnalyzer.WindowsUpdateOperationalChannel,
            "Microsoft-Windows-WindowsUpdateClient", 2, eventTimestamp,
            "Update failed with HRESULT 0x800F0831.");
        var diagnostic = new WindowsUpdateCbsLogAnalyzer().Analyze(sourceText, windowsUpdateEvent);
        Assert.NotNull(diagnostic);

        var engine = new RecommendationEngine();
        Assert.Null(diagnostic!.CbsEvidence?.PackageIdentity);
        Assert.Empty(engine.Recommend(CreateReport(diagnostic), [cbsRule]));
        Assert.Empty(engine.Recommend(CreateReport(Finding("Windows Update",
            "0x800F0831 CBS marker=ManifestMissing; package identity=" + packageIdentity,
            DiagnosticSourceMetadata.FromEventProvider("Microsoft-Windows-WindowsUpdateClient"))), [cbsRule]));
        Assert.Empty(engine.Recommend(CreateReport(diagnostic! with
        {
            WindowsUpdateEventEvidence = new WindowsUpdateEventEvidence("System", "0x800F0831")
        }), [cbsRule]));
        Assert.Empty(engine.Recommend(CreateReport(diagnostic! with
        {
            CbsEvidence = new CbsPackageEvidence(CbsEvidenceType.ManifestMissing, "C:\\Sensitive\\private.cab")
        }), [cbsRule]));
        Assert.Empty(engine.Recommend(CreateReport(diagnostic! with
        {
            Evidence = $"CBS marker=FailedToResolvePackage; package identity={packageIdentity}"
        }), [cbsRule]));
        Assert.Empty(engine.Recommend(CreateReport(diagnostic! with
        {
            CbsEvidence = new CbsPackageEvidence(CbsEvidenceType.ManifestMissing,
                "Package_456_for_KB3192392~31bf3856ad364e35~amd64~~6.3.1.4")
        }), [cbsRule]));
    }

    [Fact]
    public void PilotRecommendationsExposeManualSafetyMetadataAndUseStableOrdering()
    {
        var package = DeserializePackage(ReadPilotPack());
        var now = DateTimeOffset.UtcNow;
        var report = CreateReport(
            Finding("Windows Update", "Windows Setup upgrade reported 0xC1900107."),
            Finding("Windows Update", "Event code 0x80073712.", DiagnosticSourceMetadata.FromEventProvider("Microsoft-Windows-WindowsUpdateClient")));
        var recommendations = new RecommendationEngine().Recommend(report, package.Rules.Reverse());

        Assert.Equal(
            ["microsoft.windows-update.0x80073712", "microsoft.windows-setup.0xc1900107"],
            recommendations.Select(item => item.RuleId));
        Assert.All(recommendations, item =>
        {
            Assert.True(item.Procedure!.ManualOnly);
            Assert.True(item.Procedure.RequiresUserConfirmation);
            Assert.NotEmpty(item.RequiredEvidence!);
        });
        var servicingProcedure = Assert.Single(recommendations, item => item.RuleId.EndsWith("0x80073712", StringComparison.Ordinal)).Procedure!;
        Assert.True(servicingProcedure.RequiresElevation);
        Assert.True(servicingProcedure.IsModifying);
        Assert.Contains("indisponível", servicingProcedure.Rollback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CBS.log", servicingProcedure.DiagnosticAction, StringComparison.Ordinal);

        var run = new DiagnosticRun(Guid.NewGuid(), now, now, TimeSpan.Zero, new ComputerInventory(), report);
        var html = new HtmlDiagnosticReportFormatter().Format(run, recommendations, new RootCauseAnalysis("Causa indeterminada.", []));
        Assert.Contains("Ação corretiva manual (não executada)", html);
        Assert.Contains("elevação necessária: sim", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CBS.log", html);
        Assert.Contains("Limitação da fonte", html);
        Assert.Contains("rollback", html, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadPilotPack() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, PilotPackPath));

    private static KnowledgePackage DeserializePackage(string json) => JsonSerializer.Deserialize<KnowledgePackage>(json,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        })!;

    private static string CreateLegacyPackage(int count)
    {
        var rules = Enumerable.Range(0, count).Select(index => new KnowledgeRule(
            $"legacy-rule-{index:D3}", 1, "Fixture", "Regra legada de teste", KnowledgeImpact.Low,
            [], [], [], [], [new KnowledgeReference("Documento de teste", "https://example.invalid/docs")])).ToArray();
        return JsonSerializer.Serialize(new KnowledgePackage("1.0", "legacy-fixture-v1", "fixture local", rules),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
            });
    }

    private static DiagnosticResult Finding(string scanner, string evidence, DiagnosticSourceMetadata? sourceMetadata = null) => new(
        scanner, "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
        "Falha contextual observada", evidence, "Nenhuma ação foi executada.", evidence,
        TimeSpan.Zero, DateTimeOffset.UtcNow) { SourceMetadata = sourceMetadata };

    private static DiagnosticReport CreateReport(params DiagnosticResult[] results) => new(
        results, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), new HealthScore(80));

    private sealed class InMemoryKnowledgeRepository : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeRule>>([]);

        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
