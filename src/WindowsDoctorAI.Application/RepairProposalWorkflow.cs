using System.Security.Cryptography;
using System.Text;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

/// <summary>Gera planos somente quando uma regra/versão e um tipo aparecem na allowlist compilada.</summary>
public sealed class RepairProposalBuilder(
    RecommendationEngine recommendationEngine,
    IRepairProposalAllowlist allowlist)
{
    private static readonly RepairConditionKind[] RequiredPreconditions =
    [
        RepairConditionKind.DiagnosticRunIsCurrent,
        RepairConditionKind.FindingIsPresent,
        RepairConditionKind.RuleVersionIsCurrent,
        RepairConditionKind.FindingMatchesRule,
        RepairConditionKind.RuleIsAllowlisted
    ];
    private static readonly RepairConditionKind[] RequiredRollbackPreconditions =
    [
        RepairConditionKind.DiagnosticRunIsCurrent,
        RepairConditionKind.FindingIsPresent,
        RepairConditionKind.RuleVersionIsCurrent,
        RepairConditionKind.FindingMatchesRule,
        RepairConditionKind.RuleIsAllowlisted,
        RepairConditionKind.OriginalExecutionSucceeded
    ];

    public IReadOnlyList<RepairProposal> Build(DiagnosticRun? run, IReadOnlyList<KnowledgeRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (run?.Report is null || run.Id == Guid.Empty) return Array.Empty<RepairProposal>();

        var report = DiagnosticPrivacyRedactor.RedactReport(run.Report, run.Inventory)!;
        var recommendations = recommendationEngine.Recommend(report, rules, DiagnosticPrivacyRedactor.RedactInventory(run.Inventory));
        var rulesByVersion = rules.ToDictionary(rule => (rule.Id, rule.Version));
        var proposals = new List<RepairProposal>();

        foreach (var recommendation in recommendations)
        {
            if (!rulesByVersion.TryGetValue((recommendation.RuleId, recommendation.RuleVersion), out var rule)
                || rule.Procedure is not { ManualOnly: false, IsModifying: false, RequiresElevation: false, RequiresUserConfirmation: true }
                || !allowlist.TryGetDefinition(rule.Id, rule.Version, out var definition)
                || !IsValidDefinition(definition, rule))
                continue;

            foreach (var recommendationEvidence in recommendation.Evidence)
            {
                var findingIdentity = DiagnosticFindingIdentity.Create(recommendationEvidence);
                if (!report.Results.Any(result => result.Status == DiagnosticStatus.Finding
                    && string.Equals(DiagnosticFindingIdentity.Create(result), findingIdentity, StringComparison.Ordinal)))
                    continue;

                var redactedEvidence = DiagnosticPrivacyRedactor.RedactText(
                    $"{recommendationEvidence.ScannerName} · {recommendationEvidence.Category} · {recommendationEvidence.Title} · {recommendationEvidence.Evidence}",
                    run.Inventory);
                var evidenceFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(redactedEvidence))).ToLowerInvariant();
                var proposalId = $"{definition!.ProposalId}.{findingIdentity[..16]}";
                proposals.Add(new RepairProposal(
                    proposalId,
                    definition.Title,
                    definition.Description,
                    definition.Risk,
                    definition.Impact,
                    RequiresExplicitApproval: true,
                    SupportsRollback: definition.SupportsRollback,
                    PlanVersion: definition.PlanVersion,
                    Target: definition.Target,
                    Preconditions: definition.Preconditions.Select(condition => condition.DisplayText).ToArray(),
                    Postconditions: definition.Postconditions.Select(condition => condition.DisplayText).ToArray(),
                    RollbackPreconditions: (definition.RollbackPreconditions ?? Array.Empty<RepairPlanCondition>())
                        .Select(condition => condition.DisplayText).ToArray(),
                    RollbackPostconditions: (definition.RollbackPostconditions ?? Array.Empty<RepairPlanCondition>())
                        .Select(condition => condition.DisplayText).ToArray())
                {
                    Kind = definition.Kind,
                    OperationVersion = definition.OperationVersion,
                    DiagnosticRunId = run.Id,
                    FindingIdentity = findingIdentity,
                    RuleId = rule.Id,
                    RuleVersion = rule.Version,
                    EvidenceFingerprint = evidenceFingerprint,
                    RedactedEvidence = redactedEvidence,
                    StructuredPreconditions = definition.Preconditions.ToArray(),
                    StructuredPostconditions = definition.Postconditions.ToArray(),
                    StructuredRollbackPreconditions = (definition.RollbackPreconditions ?? Array.Empty<RepairPlanCondition>()).ToArray(),
                    StructuredRollbackPostconditions = (definition.RollbackPostconditions ?? Array.Empty<RepairPlanCondition>()).ToArray()
                });
            }
        }

        return proposals.OrderBy(proposal => proposal.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(proposal => proposal.FindingIdentity, StringComparer.Ordinal).ToArray();
    }

    private static bool IsValidDefinition(RepairProposalDefinition? definition, KnowledgeRule rule) =>
        definition is not null
        && string.Equals(definition.RuleId, rule.Id, StringComparison.Ordinal)
        && definition.RuleVersion == rule.Version
        && definition.Kind == RepairProposalKind.NoOpSimulation
        && definition.PlanVersion > 0
        && definition.OperationVersion > 0
        && !string.IsNullOrWhiteSpace(definition.ProposalId)
        && !string.IsNullOrWhiteSpace(definition.Title)
        && !string.IsNullOrWhiteSpace(definition.Description)
        && definition.Risk != RepairRiskLevel.Unknown
        && !string.IsNullOrWhiteSpace(definition.Target)
        && HasExactConditions(definition.Preconditions, RequiredPreconditions)
        && HasExactConditions(definition.Postconditions,
            [RepairConditionKind.SimulationCompletedWithoutSystemChanges])
        && (definition.SupportsRollback
            ? HasExactConditions(definition.RollbackPreconditions, RequiredRollbackPreconditions)
                && HasExactConditions(definition.RollbackPostconditions,
                    [RepairConditionKind.SimulationCompletedWithoutSystemChanges])
            : (definition.RollbackPreconditions is null or { Count: 0 })
                && (definition.RollbackPostconditions is null or { Count: 0 }));

    private static bool HasExactConditions(
        IReadOnlyList<RepairPlanCondition>? actual,
        IReadOnlyCollection<RepairConditionKind> expected) =>
        actual is { Count: > 0 }
        && actual.All(condition => condition is not null && Enum.IsDefined(condition.Kind))
        && actual.Select(condition => condition.Kind).Distinct().Count() == actual.Count
        && expected.All(kind => actual.Any(condition => condition.Kind == kind))
        && actual.Count == expected.Count;
}

/// <summary>Revalida a proposta contra o último snapshot e a versão atual da regra, sem confiar no texto exibido.</summary>
public sealed class DiagnosticRepairPreconditionEvaluator(
    IDiagnosticRunRepository history,
    IKnowledgeRepository knowledgeRepository,
    RecommendationEngine recommendationEngine,
    IRepairProposalAllowlist allowlist) : IRepairPreconditionEvaluator
{
    public async Task<IReadOnlyList<RepairConditionResult>> EvaluateAsync(
        RepairProposal proposal,
        RepairAction action,
        Guid? relatedRepairExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var conditions = action == RepairAction.Rollback
            ? proposal.StructuredRollbackPreconditions
            : proposal.StructuredPreconditions;
        if (conditions.Count == 0) return Array.Empty<RepairConditionResult>();

        var latest = await history.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        var rules = await knowledgeRepository.GetLatestRulesAsync(cancellationToken).ConfigureAwait(false);
        var matchingRules = rules.Where(rule => string.Equals(rule.Id, proposal.RuleId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var currentRule = matchingRules.OrderByDescending(rule => rule.Version).FirstOrDefault();
        var runIsCurrent = latest?.Id == proposal.DiagnosticRunId && latest.Report is not null;
        var findingIsPresent = runIsCurrent && latest!.Report!.Results.Any(result =>
            result.Status == DiagnosticStatus.Finding
            && string.Equals(DiagnosticFindingIdentity.Create(result), proposal.FindingIdentity, StringComparison.Ordinal));
        var exactRuleVersion = currentRule is not null && currentRule.Version == proposal.RuleVersion;
        var ruleIsSafe = currentRule?.Procedure is
            { ManualOnly: false, IsModifying: false, RequiresElevation: false, RequiresUserConfirmation: true };
        var allowlisted = exactRuleVersion && ruleIsSafe
            && allowlist.TryGetDefinition(proposal.RuleId, proposal.RuleVersion, out var definition)
            && definition is not null && definition.Kind == proposal.Kind
            && definition.PlanVersion == proposal.PlanVersion
            && definition.OperationVersion == proposal.OperationVersion;
        var findingMatchesRule = false;
        if (runIsCurrent && exactRuleVersion && latest!.Report is not null)
        {
            var currentReport = DiagnosticPrivacyRedactor.RedactReport(latest.Report, latest.Inventory)!;
            var recommendations = recommendationEngine.Recommend(
                currentReport, rules, DiagnosticPrivacyRedactor.RedactInventory(latest.Inventory));
            foreach (var recommendation in recommendations.Where(recommendation =>
                         string.Equals(recommendation.RuleId, proposal.RuleId, StringComparison.Ordinal)
                         && recommendation.RuleVersion == proposal.RuleVersion))
            {
                foreach (var evidence in recommendation.Evidence.Where(evidence =>
                             string.Equals(DiagnosticFindingIdentity.Create(evidence), proposal.FindingIdentity, StringComparison.Ordinal)))
                {
                    var currentEvidence = DiagnosticPrivacyRedactor.RedactText(
                        $"{evidence.ScannerName} · {evidence.Category} · {evidence.Title} · {evidence.Evidence}", latest.Inventory);
                    var currentEvidenceFingerprint = Convert.ToHexString(
                        SHA256.HashData(Encoding.UTF8.GetBytes(currentEvidence))).ToLowerInvariant();
                    if (string.Equals(currentEvidence, proposal.RedactedEvidence, StringComparison.Ordinal)
                        && string.Equals(currentEvidenceFingerprint, proposal.EvidenceFingerprint, StringComparison.Ordinal))
                    {
                        findingMatchesRule = true;
                        break;
                    }
                }
                if (findingMatchesRule) break;
            }
        }

        return conditions.Select(condition => new RepairConditionResult(condition.Kind, condition.Kind switch
        {
            RepairConditionKind.DiagnosticRunIsCurrent => runIsCurrent ? RepairConditionStatus.Verified : RepairConditionStatus.Failed,
            RepairConditionKind.FindingIsPresent => findingIsPresent ? RepairConditionStatus.Verified : RepairConditionStatus.Failed,
            RepairConditionKind.RuleVersionIsCurrent => exactRuleVersion ? RepairConditionStatus.Verified : RepairConditionStatus.Failed,
            RepairConditionKind.FindingMatchesRule => findingMatchesRule ? RepairConditionStatus.Verified : RepairConditionStatus.Failed,
            RepairConditionKind.RuleIsAllowlisted => allowlisted ? RepairConditionStatus.Verified : RepairConditionStatus.Failed,
            RepairConditionKind.OriginalExecutionSucceeded => RepairConditionStatus.Failed,
            _ => RepairConditionStatus.Failed
        })).ToArray();
    }
}
