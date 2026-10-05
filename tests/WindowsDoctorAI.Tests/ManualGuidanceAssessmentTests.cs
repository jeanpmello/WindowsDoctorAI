using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsDoctorAI.App;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Database;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class ManualGuidanceAssessmentTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 4, 18, 0, 0, TimeSpan.Zero);
    private const string ErrorCode = "0xAABBCCDD";

    [Fact]
    public async Task ProjectionDistinguishesNoFindingsAndAlsoReportsAnEmptyKnowledgeBase()
    {
        var run = CreateRun(CreateReport());
        var service = CreateAssessmentService(new FakeKnowledgeRepository());

        var assessment = await service.CreateManualGuidanceAssessmentAsync(run);

        Assert.Equal(ManualGuidanceAssessmentStatus.NoFindings, assessment.Status);
        Assert.True(assessment.KnowledgeBaseIsEmpty);
        Assert.Empty(assessment.Findings);
        Assert.Contains("Nenhum achado", assessment.StatusText, StringComparison.Ordinal);
        Assert.Contains("base de conhecimento também está vazia", assessment.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectionReportsEmptyKnowledgeBaseWithoutCallingItAHealthyResult()
    {
        var finding = CreateFinding($"Falha observada {ErrorCode}");
        var assessment = await CreateAssessmentService(new FakeKnowledgeRepository())
            .CreateManualGuidanceAssessmentAsync(CreateRun(CreateReport(finding)));

        Assert.Equal(ManualGuidanceAssessmentStatus.KnowledgeBaseEmpty, assessment.Status);
        Assert.True(assessment.KnowledgeBaseIsEmpty);
        var projection = Assert.Single(assessment.Findings);
        Assert.Equal(ManualGuidanceFindingStatus.KnowledgeBaseEmpty, projection.Status);
        Assert.Contains("Base de conhecimento vazia", assessment.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("saudável", assessment.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectionDistinguishesAnUnmatchedFindingFromAHealthyConclusion()
    {
        var finding = CreateFinding($"Falha observada {ErrorCode}");
        var unrelatedRule = CreateManualRule("fixture.other", "0x11223344");
        var assessment = await CreateAssessmentService(new FakeKnowledgeRepository([unrelatedRule]))
            .CreateManualGuidanceAssessmentAsync(CreateRun(CreateReport(finding)));

        Assert.Equal(ManualGuidanceAssessmentStatus.NoMatchingRule, assessment.Status);
        var projection = Assert.Single(assessment.Findings);
        Assert.Equal(ManualGuidanceFindingStatus.NoMatch, projection.Status);
        Assert.Empty(projection.Recommendations);
        Assert.Contains("Nenhuma regra ManualOnly correspondeu", projection.StatusText, StringComparison.Ordinal);
        Assert.Contains("não prova que um problema inexiste", assessment.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectionListsEveryManualRuleCandidateForTheSameFinding()
    {
        var finding = CreateFinding($"Falha observada {ErrorCode}",
            DiagnosticSourceMetadata.FromEventProvider("Microsoft-Windows-WindowsUpdateClient"));
        var first = CreateManualRule("fixture.first");
        var second = CreateManualRule("fixture.second");
        var assessment = await CreateAssessmentService(new FakeKnowledgeRepository([first, second]))
            .CreateManualGuidanceAssessmentAsync(CreateRun(CreateReport(finding)));

        Assert.Equal(ManualGuidanceAssessmentStatus.GuidanceAvailable, assessment.Status);
        var projection = Assert.Single(assessment.Findings);
        Assert.Equal(ManualGuidanceFindingStatus.MultipleCandidates, projection.Status);
        Assert.Equal(2, projection.Recommendations.Count);
        Assert.Contains("todas estão listadas", projection.StatusText, StringComparison.Ordinal);
        Assert.All(projection.Recommendations, recommendation =>
        {
            Assert.Equal("ManualOnly / não executada", recommendation.ManualOnlyStatusText);
            Assert.Contains("Força do match", recommendation.MatchStrengthText, StringComparison.Ordinal);
            Assert.Contains("não é probabilidade causal", recommendation.MatchStrengthText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("backup", recommendation.Backup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Rollback", recommendation.Rollback, StringComparison.Ordinal);
            Assert.Contains("privilégio", recommendation.RequiredPrivilege, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("não", recommendation.ElevationText, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task RequiredEvidenceTypesProducesIncompleteAssessmentAndNeverARecommendation()
    {
        var finding = CreateFinding($"Falha observada {ErrorCode}",
            DiagnosticSourceMetadata.FromEventProvider("WindowsUpdateClient"));
        var rule = CreateManualRule("fixture.required-evidence", strictMatch: new KnowledgeMatchCondition(
            ErrorCode,
            ["Windows Update"],
            [])
        {
            RequiredSourceProviders = ["WindowsUpdateClient"],
            RequiredEvidenceTypes = [nameof(CbsMarkerType.ManifestMissing)]
        }) with
        {
            OsTarget = new KnowledgeOperatingSystemTarget(
                [KnowledgeOperatingSystemFamily.WindowsClient], 20000, 30000)
        };
        var report = CreateReport(finding);
        var engine = new RecommendationEngine();

        Assert.Empty(engine.Recommend(report, [rule]));
        var perFinding = Assert.Single(engine.AssessManualFindings(report, [rule]));
        Assert.Empty(perFinding.Matches);
        Assert.Contains("Evidência insuficiente/incompleta", Assert.Single(perFinding.IncompleteCandidates).Reason, StringComparison.Ordinal);

        var inventory = new ComputerInventory
        {
            OperatingSystem = new OperatingSystemDetails
            {
                ProductType = OperatingSystemProductType.Workstation,
                Build = "22621"
            }
        };
        var assessment = await CreateAssessmentService(new FakeKnowledgeRepository([rule]))
            .CreateManualGuidanceAssessmentAsync(CreateRun(report, inventory));
        Assert.Equal(ManualGuidanceAssessmentStatus.Incomplete, assessment.Status);
        var projection = Assert.Single(assessment.Findings);
        Assert.Equal(ManualGuidanceFindingStatus.Incomplete, projection.Status);
        Assert.Empty(projection.Recommendations);
        Assert.Contains("Evidência insuficiente/incompleta", projection.StatusText, StringComparison.Ordinal);
        var incompleteCandidate = Assert.Single(projection.IncompleteCandidates);
        Assert.Contains("Aplicabilidade estruturada verificada", incompleteCandidate.ApplicabilityText, StringComparison.Ordinal);
        Assert.Contains("match: não confirmada", incompleteCandidate.MatchStrengthText, StringComparison.Ordinal);
        Assert.Contains("Avaliação incompleta", assessment.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("saudável", assessment.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectionMarksMixedCandidatesIncompleteAndKeepsCompleteGuidanceSeparate()
    {
        var report = CreateReport(CreateFinding($"Falha observada {ErrorCode}"));
        var completeRule = CreateManualRule("fixture.mixed-complete");
        var incompleteRule = CreateManualRule("fixture.mixed-incomplete", strictMatch: new KnowledgeMatchCondition(
            ErrorCode,
            ["Windows Update"],
            []) { RequiredEvidenceTypes = [nameof(CbsMarkerType.ManifestMissing)] });
        var assessment = await CreateAssessmentService(new FakeKnowledgeRepository([completeRule, incompleteRule]))
            .CreateManualGuidanceAssessmentAsync(CreateRun(report));

        Assert.Equal(ManualGuidanceAssessmentStatus.Incomplete, assessment.Status);
        var projection = Assert.Single(assessment.Findings);
        Assert.Equal(ManualGuidanceFindingStatus.Incomplete, projection.Status);
        Assert.Single(projection.Recommendations);
        Assert.Single(projection.IncompleteCandidates);
        Assert.Contains("orientações completas continuam listadas separadamente", projection.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingStructuredProviderIsAnIncompleteCandidateNotAConfirmedMatch()
    {
        var rule = CreateManualRule("fixture.provider-required", strictMatch: new KnowledgeMatchCondition(
            ErrorCode,
            ["Windows Update"],
            []) { RequiredSourceProviders = ["WindowsUpdateClient"] });
        var report = CreateReport(CreateFinding($"Falha observada {ErrorCode}"));

        var result = Assert.Single(new RecommendationEngine().AssessManualFindings(report, [rule]));

        Assert.Empty(result.Matches);
        Assert.Contains("provider estruturado", Assert.Single(result.IncompleteCandidates).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectionShowsStructuredVerifiedVersusLegacyUnverifiedAndUnauthenticatedProvenance()
    {
        var finding = CreateFinding($"Falha observada {ErrorCode}; password=topsecret; host-jean-local",
            DiagnosticSourceMetadata.FromEventProvider("Microsoft-Windows-WindowsUpdateClient"));
        var legacy = CreateManualRule("fixture.legacy");
        var structured = CreateManualRule("fixture.structured", strictMatch: new KnowledgeMatchCondition(
            ErrorCode,
            ["Windows Update"],
            []) { RequiredSourceProviders = ["WindowsUpdateClient"] }) with
        {
            OsTarget = new KnowledgeOperatingSystemTarget(
                [KnowledgeOperatingSystemFamily.WindowsClient], 20000, 30000)
        };
        var legacyProvenance = new KnowledgeRuleProvenance(legacy.Id, legacy.Version, "pkg-legacy-v7", "Curadoria declarada por host-jean-local", new string('a', 64));
        var structuredProvenance = new KnowledgeRuleProvenance(structured.Id, structured.Version, "pkg-os-v4", "Fonte estruturada da fixture", new string('b', 64));
        var repository = new FakeKnowledgeRepository([legacy, structured], [legacyProvenance, structuredProvenance]);
        var service = CreateAssessmentService(repository);
        var run = CreateRun(CreateReport(finding), new ComputerInventory
        {
            ComputerName = "host-jean-local",
            OperatingSystem = new OperatingSystemDetails
            {
                ProductType = OperatingSystemProductType.Workstation,
                Build = "22621"
            }
        });

        var assessment = await service.CreateManualGuidanceAssessmentAsync(run);

        Assert.Equal(ManualGuidanceAssessmentStatus.GuidanceAvailable, assessment.Status);
        var projection = Assert.Single(assessment.Findings);
        Assert.Equal("Provider estruturado (allowlist): WindowsUpdateClient", projection.ProviderText);
        Assert.Contains("Execução", projection.RunReferenceText, StringComparison.Ordinal);
        Assert.DoesNotContain("host-jean-local", projection.FindingHeading, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host-jean-local", projection.EvidenceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("topsecret", projection.EvidenceText, StringComparison.Ordinal);
        Assert.Contains("password=[redigido]", projection.EvidenceText, StringComparison.OrdinalIgnoreCase);
        var legacyCard = Assert.Single(projection.Recommendations, item => item.RuleIdentityText.StartsWith(legacy.Id, StringComparison.Ordinal));
        Assert.Contains("legada não verificada", legacyCard.ApplicabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Aplicável", legacyCard.DeclaredApplicability, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não autenticada", legacyCard.DeclaredPackageSourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host-jean-local", legacyCard.DeclaredPackageSourceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pkg-legacy-v7", legacyCard.PackageVersionText, StringComparison.Ordinal);
        Assert.Contains(new string('a', 64), legacyCard.PackageSha256Text, StringComparison.Ordinal);

        var structuredCard = Assert.Single(projection.Recommendations, item => item.RuleIdentityText.StartsWith(structured.Id, StringComparison.Ordinal));
        Assert.Contains("estruturada verificada", structuredCard.ApplicabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não autenticada", structuredCard.DeclaredPackageSourceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pkg-os-v4", structuredCard.PackageVersionText, StringComparison.Ordinal);
        Assert.Contains(new string('b', 64), structuredCard.PackageSha256Text, StringComparison.Ordinal);
        Assert.Equal(0, repository.SaveImportCallCount);
    }

    [Fact]
    public async Task HomeViewModelPublishesIncompleteAndProvenanceStatesWithoutRepairProposals()
    {
        var rule = CreateManualRule("fixture.home", strictMatch: new KnowledgeMatchCondition(
            ErrorCode,
            ["Windows Update"],
            [])
        {
            RequiredSourceProviders = ["WindowsUpdateClient"],
            RequiredEvidenceTypes = [nameof(CbsMarkerType.ManifestMissing)]
        });
        var run = CreateRun(CreateReport(CreateFinding($"Falha observada {ErrorCode}",
            DiagnosticSourceMetadata.FromEventProvider("WindowsUpdateClient"))));
        var provenance = new KnowledgeRuleProvenance(rule.Id, rule.Version, "local-v5", "Fonte declarada local", new string('c', 64));
        var repository = new FakeKnowledgeRepository([rule], [provenance]);
        var viewModel = CreateHomeViewModel(new FakeHistory(run), repository);

        await viewModel.LoadLatestAsync();

        Assert.Contains("Avaliação incompleta", viewModel.ManualGuidanceStatus, StringComparison.Ordinal);
        var finding = Assert.Single(viewModel.ManualGuidanceFindings);
        var candidate = Assert.Single(finding.IncompleteCandidates);
        Assert.Contains("Aplicabilidade legada não verificada", candidate.ApplicabilityText, StringComparison.Ordinal);
        Assert.Contains("match: não confirmada", candidate.MatchStrengthText, StringComparison.Ordinal);
        Assert.Contains("não autenticada", candidate.DeclaredPackageSourceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local-v5", candidate.PackageVersionText, StringComparison.Ordinal);
        Assert.Contains(new string('c', 64), candidate.PackageSha256Text, StringComparison.Ordinal);
        Assert.Empty(finding.Recommendations);
        Assert.Empty(viewModel.RepairProposals);
    }

    [Fact]
    public async Task HomeViewModelProjectsNoFindingsAsASeparateNonPositiveState()
    {
        var repo = new FakeKnowledgeRepository([CreateManualRule("fixture.home-no-findings")]);
        var viewModel = CreateHomeViewModel(new FakeHistory(CreateRun(CreateReport())), repo);

        await viewModel.LoadLatestAsync();

        Assert.Contains("Nenhum achado", viewModel.ManualGuidanceStatus, StringComparison.Ordinal);
        Assert.Empty(viewModel.ManualGuidanceFindings);
        Assert.Empty(viewModel.RepairProposals);
    }

    [Fact]
    public async Task HomeViewModelShowsAnEmptyKnowledgeBaseSeparately()
    {
        var viewModel = CreateHomeViewModel(
            new FakeHistory(CreateRun(CreateReport(CreateFinding($"Falha {ErrorCode}")))),
            new FakeKnowledgeRepository());

        await viewModel.LoadLatestAsync();

        Assert.Contains("Base de conhecimento vazia", viewModel.ManualGuidanceStatus, StringComparison.Ordinal);
        Assert.Equal(ManualGuidanceFindingStatus.KnowledgeBaseEmpty, Assert.Single(viewModel.ManualGuidanceFindings).Status);
    }

    [Fact]
    public async Task HomeViewModelShowsAnUnmatchedFindingWithoutPositiveHealthLanguage()
    {
        var repository = new FakeKnowledgeRepository([CreateManualRule("fixture.unmatched", "0x11223344")]);
        var viewModel = CreateHomeViewModel(
            new FakeHistory(CreateRun(CreateReport(CreateFinding($"Falha {ErrorCode}")))), repository);

        await viewModel.LoadLatestAsync();

        Assert.Contains("Nenhuma regra ManualOnly correspondeu", viewModel.ManualGuidanceStatus, StringComparison.Ordinal);
        Assert.Equal(ManualGuidanceFindingStatus.NoMatch, Assert.Single(viewModel.ManualGuidanceFindings).Status);
    }

    [Fact]
    public async Task HomeViewModelShowsAllMultipleCandidatesWithoutSelectingOne()
    {
        var repository = new FakeKnowledgeRepository(
            [CreateManualRule("fixture.home-a"), CreateManualRule("fixture.home-b")]);
        var viewModel = CreateHomeViewModel(
            new FakeHistory(CreateRun(CreateReport(CreateFinding($"Falha {ErrorCode}")))), repository);

        await viewModel.LoadLatestAsync();

        var finding = Assert.Single(viewModel.ManualGuidanceFindings);
        Assert.Equal(ManualGuidanceFindingStatus.MultipleCandidates, finding.Status);
        Assert.Equal(2, finding.Recommendations.Count);
        Assert.Contains("todas estão listadas", finding.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqliteRepositoryReturnsPreviouslyStoredPackageProvenanceForEachCurrentRule()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var repository = new SqliteKnowledgeRepository(context);
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "knowledge-packs/microsoft-windows-update-pilot.json"));
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        };
        var package = JsonSerializer.Deserialize<KnowledgePackage>(json, jsonOptions)!;

        var imported = await new KnowledgeJsonImporter(repository).ImportAsync(json);
        var provenance = await repository.GetLatestRuleProvenanceAsync();

        Assert.Equal(package.Rules.Count, provenance.Count);
        var rule = Assert.Single(package.Rules, item => item.Id == provenance[0].RuleId);
        var metadata = Assert.Single(provenance, item => item.RuleId == rule.Id && item.RuleVersion == rule.Version);
        Assert.Equal(package.Version, metadata.PackageVersion);
        Assert.Equal(package.Source, metadata.DeclaredSource);
        Assert.Equal(imported.Sha256, metadata.Sha256);
        Assert.Equal(1, await context.KnowledgeBaseVersions.CountAsync());
    }

    [Fact]
    public async Task SqliteProvenanceMatchesPackageMetadataAlreadyStoredByImporter()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var repository = new SqliteKnowledgeRepository(context);
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "knowledge-packs/microsoft-windows-update-pilot.json"));
        var package = JsonSerializer.Deserialize<KnowledgePackage>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        })!;

        var imported = await new KnowledgeJsonImporter(repository).ImportAsync(json);
        var provenance = await repository.GetLatestRuleProvenanceAsync();

        Assert.Equal(package.Rules.Count, provenance.Count);
        Assert.All(provenance, item =>
        {
            Assert.Equal(package.Version, item.PackageVersion);
            Assert.Equal(package.Source, item.DeclaredSource);
            Assert.Equal(imported.Sha256, item.Sha256);
        });
    }

    [Fact]
    public async Task MissingDiagnosticReportIsShownAsIncomplete()
    {
        var run = CreateRun(report: null);
        var assessment = await CreateAssessmentService(new FakeKnowledgeRepository([CreateManualRule("fixture.legacy-run")]))
            .CreateManualGuidanceAssessmentAsync(run);

        Assert.Equal(ManualGuidanceAssessmentStatus.NoDiagnosticReport, assessment.Status);
        Assert.Contains("Avaliação incompleta", assessment.StatusText, StringComparison.Ordinal);
        Assert.Empty(assessment.Findings);
    }

    private static DiagnosticAssessmentService CreateAssessmentService(FakeKnowledgeRepository repository) => new(
        repository,
        new RecommendationEngine(),
        new RootCauseAnalyzer(),
        new HtmlDiagnosticReportFormatter());

    private static HomeViewModel CreateHomeViewModel(FakeHistory history, FakeKnowledgeRepository repository)
    {
        var useCase = new RunComputerInventoryDiagnosticUseCase(
            new FakeInventoryScanner(),
            new FakeDiagnosticEngine(CreateReport()),
            history,
            new FakeUserSettingsRepository(),
            NullLogger<RunComputerInventoryDiagnosticUseCase>.Instance);
        return new HomeViewModel(
            useCase,
            history,
            repository,
            new KnowledgeJsonImporter(repository),
            CreateAssessmentService(repository),
            new CbsLogImportService(new NoCbsLogPicker(), new CbsLogMarkerClassifier()),
            new UnsupportedBackupSetCatalogSource(),
            NullLogger<HomeViewModel>.Instance);
    }

    private static KnowledgeRule CreateManualRule(
        string id,
        string code = ErrorCode,
        KnowledgeMatchCondition? strictMatch = null) => new(
            id,
            1,
            "Fixture",
            $"Orientação {id}",
            KnowledgeImpact.Moderate,
            strictMatch is null ? [code] : [],
            [],
            [],
            [],
            [new KnowledgeReference("Documento declarado", "https://example.invalid/manual")],
            "Aplicável conforme descrição declarada, sem verificação automática.",
            strictMatch,
            ["Confirme os dados observáveis antes de qualquer decisão humana."],
            new KnowledgeProcedure(
                "Revise o achado e a evidência redigida.",
                "Consulte a documentação e decida manualmente se a orientação cabe ao caso.",
                "Conta padrão; privilégio adicional não declarado.",
                false,
                "Risco moderado declarado; efeito não medido.",
                "Valide a política local de backup antes de qualquer ação futura.",
                "Rollback não especificado nesta fixture.",
                "Fonte declarada não verificada.",
                false,
                true,
                true));

    private static DiagnosticResult CreateFinding(string evidence, DiagnosticSourceMetadata? provider = null) => new(
        "Windows Update",
        "Sistema",
        DiagnosticSeverity.Warning,
        DiagnosticStatus.Finding,
        "Evento observado",
        evidence,
        "Nenhuma ação foi executada.",
        evidence,
        TimeSpan.Zero,
        FixedNow)
    {
        SourceMetadata = provider
    };

    private static DiagnosticReport CreateReport(params DiagnosticResult[] findings) => new(
        findings,
        FixedNow.AddMinutes(-1),
        FixedNow,
        TimeSpan.FromMinutes(1),
        null);

    private static DiagnosticRun CreateRun(DiagnosticReport? report, ComputerInventory? inventory = null) => new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        FixedNow.AddMinutes(-1),
        FixedNow,
        TimeSpan.FromMinutes(1),
        inventory ?? new ComputerInventory(),
        report);

    private sealed class FakeKnowledgeRepository(
        IReadOnlyList<KnowledgeRule>? rules = null,
        IReadOnlyList<KnowledgeRuleProvenance>? provenance = null) : IKnowledgeRepository
    {
        private readonly IReadOnlyList<KnowledgeRule> _rules = rules ?? Array.Empty<KnowledgeRule>();
        private readonly IReadOnlyList<KnowledgeRuleProvenance> _provenance = provenance ?? Array.Empty<KnowledgeRuleProvenance>();
        public int SaveImportCallCount { get; private set; }

        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_rules);

        public Task<IReadOnlyList<KnowledgeRuleProvenance>> GetLatestRuleProvenanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_provenance);

        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default)
        {
            SaveImportCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHistory(DiagnosticRun? latest) : IDiagnosticRunRepository
    {
        public Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DiagnosticRun?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromResult(latest);
        public Task<int> DeleteCompletedBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeInventoryScanner : IComputerInventoryScanner
    {
        public Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ComputerInventory());
    }

    private sealed class FakeDiagnosticEngine(DiagnosticReport report) : IDiagnosticEngine
    {
        public Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(report);
    }

    private sealed class FakeUserSettingsRepository : IUserSettingsRepository
    {
        public Task<UserSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new UserSettings());
        public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoCbsLogPicker : ICbsLogFilePicker
    {
        public Task<Stream?> PickCbsLogAsync(CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
    }
}
