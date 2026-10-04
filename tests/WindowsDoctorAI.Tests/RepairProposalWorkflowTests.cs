using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Repair;

namespace WindowsDoctorAI.Tests;

public sealed class RepairProposalWorkflowTests
{
    private static readonly IReadOnlyList<RepairPlanCondition> Preconditions =
    [
        new(RepairConditionKind.DiagnosticRunIsCurrent),
        new(RepairConditionKind.FindingIsPresent),
        new(RepairConditionKind.RuleVersionIsCurrent),
        new(RepairConditionKind.FindingMatchesRule),
        new(RepairConditionKind.RuleIsAllowlisted)
    ];
    private static readonly IReadOnlyList<RepairPlanCondition> Postconditions =
        [new(RepairConditionKind.SimulationCompletedWithoutSystemChanges)];

    [Fact]
    public void BuilderCreatesOnlyTypedAllowlistedProposalAndStableFindingIdentity()
    {
        var firstFinding = Finding("Falha 0xAABBCCDD", "token=fixture-secret contact person@example.invalid");
        var laterObservation = firstFinding with { Timestamp = DateTimeOffset.UtcNow.AddDays(1) };
        Assert.Equal(DiagnosticFindingIdentity.Create(firstFinding), DiagnosticFindingIdentity.Create(laterObservation));

        var run = CreateRun(firstFinding);
        var builder = CreateBuilder();
        var proposal = Assert.Single(builder.Build(run, [SafeRule()]));

        Assert.Equal(run.Id, proposal.DiagnosticRunId);
        Assert.Equal(DiagnosticFindingIdentity.Create(firstFinding), proposal.FindingIdentity);
        Assert.Equal("fixture.safe-rule", proposal.RuleId);
        Assert.Equal(7, proposal.RuleVersion);
        Assert.Equal(RepairProposalKind.NoOpSimulation, proposal.Kind);
        Assert.Equal(5, proposal.StructuredPreconditions.Count);
        Assert.Single(proposal.StructuredPostconditions);
        Assert.DoesNotContain("fixture-secret", proposal.RedactedEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.invalid", proposal.RedactedEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualOnlyRuleNeverBecomesProposalEvenWhenTestAllowlistContainsItsVersion()
    {
        var run = CreateRun(Finding("Falha 0xAABBCCDD", "evidência fake"));
        var manualRule = SafeRule() with
        {
            Procedure = new KnowledgeProcedure("Diagnosticar", "Aplicar instrução manual", "administrator",
                RequiresElevation: true, Risk: "high", Backup: "manual", Rollback: "manual",
                SourceLimitation: "procedimento manual", IsModifying: true,
                RequiresUserConfirmation: true, ManualOnly: true)
        };

        Assert.Empty(CreateBuilder().Build(run, [manualRule]));
    }

    [Fact]
    public void ProductionAllowlistAndCatalogContainNoProposals()
    {
        var allowlist = new CodeRepairProposalAllowlist();

        Assert.False(allowlist.TryGetDefinition("fixture.safe-rule", 7, out _));
        Assert.Empty(new EmptyRepairCatalog().GetAvailableProposals());
    }

    [Fact]
    public async Task FreshRunFindingAndRuleVersionMustAllMatchBeforePreconditionsVerify()
    {
        var originalFinding = Finding("Falha 0xAABBCCDD", "observação fake");
        var run = CreateRun(originalFinding);
        var rule = SafeRule();
        var history = new FakeRunRepository(run);
        var knowledge = new FakeKnowledgeRepository([rule]);
        var recommendations = new RecommendationEngine();
        var allowlist = new FixtureAllowlist(rule.Id, rule.Version);
        var evaluator = new DiagnosticRepairPreconditionEvaluator(history, knowledge, recommendations, allowlist);
        var proposal = Assert.Single(new RepairProposalBuilder(recommendations, allowlist).Build(run, [rule]));

        var verified = await evaluator.EvaluateAsync(proposal, RepairAction.Execute, null);
        Assert.Equal(5, verified.Count);
        Assert.All(verified, result => Assert.Equal(RepairConditionStatus.Verified, result.Status));

        history.Latest = run with { Id = Guid.NewGuid() };
        var staleRun = await evaluator.EvaluateAsync(proposal, RepairAction.Execute, null);
        Assert.Equal(RepairConditionStatus.Failed,
            staleRun.Single(result => result.Kind == RepairConditionKind.DiagnosticRunIsCurrent).Status);

        history.Latest = CreateRun(Finding("Falha 0xAABBCCDD", "evidência alterada"), run.Id);
        var staleFinding = await evaluator.EvaluateAsync(proposal, RepairAction.Execute, null);
        Assert.Equal(RepairConditionStatus.Failed,
            staleFinding.Single(result => result.Kind == RepairConditionKind.FindingIsPresent).Status);
        Assert.Equal(RepairConditionStatus.Failed,
            staleFinding.Single(result => result.Kind == RepairConditionKind.FindingMatchesRule).Status);

        history.Latest = run;
        knowledge.Rules = [SafeRule() with { Version = 8 }];
        var staleRule = await evaluator.EvaluateAsync(proposal, RepairAction.Execute, null);
        Assert.Equal(RepairConditionStatus.Failed,
            staleRule.Single(result => result.Kind == RepairConditionKind.RuleVersionIsCurrent).Status);
        Assert.Equal(RepairConditionStatus.Failed,
            staleRule.Single(result => result.Kind == RepairConditionKind.RuleIsAllowlisted).Status);
    }

    [Fact]
    public async Task StructuredConditionsAreNotEvaluatedWhenRequiredSourceRunIsMissing()
    {
        var run = CreateRun(Finding("Falha 0xAABBCCDD", "evidência fake"));
        var rule = SafeRule();
        var history = new FakeRunRepository(null);
        var knowledge = new FakeKnowledgeRepository([rule]);
        var allowlist = new FixtureAllowlist(rule.Id, rule.Version);
        var evaluator = new DiagnosticRepairPreconditionEvaluator(history, knowledge, new RecommendationEngine(), allowlist);
        var proposal = Assert.Single(new RepairProposalBuilder(new RecommendationEngine(), allowlist).Build(run, [rule]));

        var results = await evaluator.EvaluateAsync(proposal, RepairAction.Execute, null);

        Assert.Equal(RepairConditionStatus.Failed,
            results.Single(result => result.Kind == RepairConditionKind.DiagnosticRunIsCurrent).Status);
        Assert.Equal(RepairConditionStatus.Failed,
            results.Single(result => result.Kind == RepairConditionKind.FindingIsPresent).Status);
    }

    private static RepairProposalBuilder CreateBuilder() => new(new RecommendationEngine(), new FixtureAllowlist("fixture.safe-rule", 7));

    private static KnowledgeRule SafeRule() => new(
        "fixture.safe-rule", 7, "Fixture", "Regra sintética", KnowledgeImpact.Low,
        ["0xAABBCCDD"], [], ["causa sintética"], ["texto descritivo nunca executado"],
        [new KnowledgeReference("Fixture", "https://example.invalid")],
        Procedure: new KnowledgeProcedure("Ler apenas", "Não modificar", "standard", false,
            "low", "não necessário", "não suportado", "fixture local", false, true, false));

    private static DiagnosticRun CreateRun(DiagnosticResult finding, Guid? id = null)
    {
        var now = DateTimeOffset.UtcNow;
        var report = new DiagnosticReport([finding], now.AddSeconds(-1), now, TimeSpan.FromSeconds(1), new HealthScore(80));
        return new DiagnosticRun(id ?? Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), now.AddSeconds(-1), now,
            TimeSpan.FromSeconds(1), new ComputerInventory(), report);
    }

    private static DiagnosticResult Finding(string title, string evidence) => new(
        "Windows Update", "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
        title, "Descrição sintética", "Ação manual descritiva", evidence,
        TimeSpan.FromMilliseconds(1), DateTimeOffset.UtcNow);

    private sealed class FixtureAllowlist(string ruleId, int ruleVersion) : IRepairProposalAllowlist
    {
        private readonly RepairProposalDefinition _definition = new(
            ruleId, ruleVersion, RepairProposalKind.NoOpSimulation, PlanVersion: 2, OperationVersion: 3,
            ProposalId: "fixture.preview", Title: "Prévia de simulação", Description: "Nenhuma alteração do sistema.",
            Risk: RepairRiskLevel.Low, Impact: "Somente teste", Target: "Alvo sintético",
            Preconditions, Postconditions);

        public bool TryGetDefinition(string requestedRuleId, int requestedRuleVersion, out RepairProposalDefinition? definition)
        {
            definition = string.Equals(requestedRuleId, ruleId, StringComparison.Ordinal)
                && requestedRuleVersion == ruleVersion ? _definition : null;
            return definition is not null;
        }
    }

    private sealed class FakeRunRepository(DiagnosticRun? latest) : IDiagnosticRunRepository
    {
        public DiagnosticRun? Latest { get; set; } = latest;
        public Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DiagnosticRun?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromResult(Latest);
        public Task<int> DeleteCompletedBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeKnowledgeRepository(IReadOnlyList<KnowledgeRule> rules) : IKnowledgeRepository
    {
        public IReadOnlyList<KnowledgeRule> Rules { get; set; } = rules;
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Rules);
        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
