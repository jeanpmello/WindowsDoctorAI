using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

public sealed record RepairProposalBuildResult(
    IReadOnlyList<RepairProposal> Proposals,
    IReadOnlyList<string> AmbiguousFindingIdentities,
    bool EvidenceChangedDuringBuild = false,
    bool DiagnosticRunNotCurrent = false);

/// <summary>Gera planos somente quando uma regra/versão e um tipo aparecem na allowlist compilada.</summary>
public sealed class RepairProposalBuilder(
    RecommendationEngine recommendationEngine,
    IRepairProposalAllowlist allowlist,
    IRepairEvidenceGate evidenceGate,
    ILogger<RepairProposalBuilder>? logger = null)
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

    public RepairProposalBuildResult Build(DiagnosticRun? run, IReadOnlyList<KnowledgeRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        
        if (run is null)
            return new RepairProposalBuildResult(Array.Empty<RepairProposal>(), Array.Empty<string>());
        
        if (run.Report is null || run.Id == Guid.Empty)
            return new RepairProposalBuildResult(Array.Empty<RepairProposal>(), Array.Empty<string>());

        var generation = evidenceGate.CurrentGeneration;
        
        if (run.EvidenceGeneration != generation)
        {
            logger?.LogWarning("Geração de evidência mudou durante a construção de propostas; evidência obsoleta detectada.");
            return new RepairProposalBuildResult(Array.Empty<RepairProposal>(), Array.Empty<string>(), EvidenceChangedDuringBuild: true);
        }
        
        if (evidenceGate.CurrentDiagnosticRunId != run.Id)
        {
            logger?.LogWarning("Diagnóstico atual mudou durante a construção de propostas; execução não é atual.");
            return new RepairProposalBuildResult(Array.Empty<RepairProposal>(), Array.Empty<string>(), DiagnosticRunNotCurrent: true);
        }

        try
        {
            var report = DiagnosticPrivacyRedactor.RedactReport(run.Report, run.Inventory)!;
            var recommendations = recommendationEngine.Recommend(report, rules, DiagnosticPrivacyRedactor.RedactInventory(run.Inventory));
            var rulesByVersion = rules
                .GroupBy(rule => (rule.Id, rule.Version))
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());
            
            var evidenceRows = recommendations
                .SelectMany(recommendation => recommendation.Evidence.Select(evidence => new
                {
                    Recommendation = recommendation,
                    Evidence = evidence,
                    Identity = DiagnosticFindingIdentity.Create(evidence)
                }))
                .ToArray();
            
            var ambiguousIdentities = evidenceRows
                .GroupBy(row => row.Identity, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            
            var proposals = BuildProposals(run, report, evidenceRows, ambiguousIdentities, rulesByVersion);
            
            // Revalidar condições após construção
            var finalGeneration = evidenceGate.CurrentGeneration;
            if (finalGeneration != generation)
            {
                logger?.LogWarning("Geração de evidência mudou durante iteração de propostas.");
                return new RepairProposalBuildResult(Array.Empty<RepairProposal>(), ambiguousIdentities.Order(StringComparer.Ordinal).ToArray(), EvidenceChangedDuringBuild: true);
            }
            
            if (evidenceGate.CurrentDiagnosticRunId != run.Id)
            {
                logger?.LogWarning("Diagnóstico atual mudou durante iteração de propostas.");
                return new RepairProposalBuildResult(Array.Empty<RepairProposal>(), ambiguousIdentities.Order(StringComparer.Ordinal).ToArray(), DiagnosticRunNotCurrent: true);
            }
            
            var uniqueProposals = proposals
                .Where(proposal => !ambiguousIdentities.Contains(proposal.FindingIdentity))
                .OrderBy(proposal => proposal.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(proposal => proposal.FindingIdentity, StringComparer.Ordinal)
                .ToArray();
            
            return new RepairProposalBuildResult(uniqueProposals, ambiguousIdentities.Order(StringComparer.Ordinal).ToArray());
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Erro ao construir propostas de reparo; tipo de exceção: {ExceptionType}", exception.GetType().Name);
            throw;
        }
    }

    private List<RepairProposal> BuildProposals(
        DiagnosticRun run,
        DiagnosticReport report,
        IReadOnlyCollection<dynamic> evidenceRows,
        HashSet<string> ambiguousIdentities,
        Dictionary<(string, int), KnowledgeRule> rulesByVersion)
    {
        var proposals = new List<RepairProposal>();

        foreach (var row in evidenceRows)
        {
            var recommendation = row.Recommendation;
            var identity = row.Identity;

            if (ambiguousIdentities.Contains(identity)
                || !rulesByVersion.TryGetValue((recommendation.RuleId, recommendation.RuleVersion), out var rule))
                continue;

            if (rule.Procedure is not { ManualOnly: false, IsModifying: false, RequiresElevation: false, RequiresUserConfirmation: true })
                continue;

            if (!allowlist.TryGetDefinition(rule.Id, rule.Version, out var definition)
                || !IsValidDefinition(definition, rule))
                continue;

            var matchingReportFindings = report.Results.Count(result => result.Status == DiagnosticStatus.Finding
                && string.Equals(DiagnosticFindingIdentity.Create(result), identity, StringComparison.Ordinal));
            
            if (matchingReportFindings != 1)
            {
                ambiguousIdentities.Add(identity);
                continue;
            }

            var redactedEvidence = DiagnosticPrivacyRedactor.RedactText(
                $"{row.Evidence.ScannerName} · {row.Evidence.Category} · {row.Evidence.Title} · {row.Evidence.Evidence}",
                run.Inventory);
            
            var evidenceFingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(redactedEvidence))).ToLowerInvariant();
            
            var stableProposalKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{definition!.ProposalId}\n{rule.Id}\n{rule.Version}\n{identity}"))).ToLowerInvariant();
            
            var proposalId = $"{definition.ProposalId}.{stableProposalKey}";
            
            proposals.Add(CreateProposal(definition, rule, run, proposalId, identity, redactedEvidence, evidenceFingerprint));
        }

        // Verificar duplicatas e adicionar identidades ambíguas
        var duplicateProposalIds = proposals
            .GroupBy(proposal => proposal.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToArray();
        
        foreach (var duplicate in duplicateProposalIds)
        {
            foreach (var proposal in duplicate)
                ambiguousIdentities.Add(proposal.FindingIdentity);
        }

        return proposals;
    }

    private static RepairProposal CreateProposal(
        RepairProposalDefinition definition,
        KnowledgeRule rule,
        DiagnosticRun run,
        string proposalId,
        string findingIdentity,
        string redactedEvidence,
        string evidenceFingerprint)
    {
        return new RepairProposal(
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
            EvidenceGeneration = run.EvidenceGeneration,
            FindingIdentity = findingIdentity,
            RuleId = rule.Id,
            RuleVersion = rule.Version,
            EvidenceFingerprint = evidenceFingerprint,
            RedactedEvidence = redactedEvidence,
            StructuredPreconditions = definition.Preconditions.ToArray(),
            StructuredPostconditions = definition.Postconditions.ToArray(),
            StructuredRollbackPreconditions = (definition.RollbackPreconditions ?? Array.Empty<RepairPlanCondition>()).ToArray(),
            StructuredRollbackPostconditions = (definition.RollbackPostconditions ?? Array.Empty<RepairPlanCondition>()).ToArray()
        };
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
    IRepairProposalAllowlist allowlist,
    IRepairEvidenceGate evidenceGate,
    ILogger<DiagnosticRepairPreconditionEvaluator>? logger = null) : IRepairPreconditionEvaluator
{
    public async Task<IReadOnlyList<RepairConditionResult>> EvaluateAsync(
        RepairProposal proposal,
        RepairAction action,
        Guid? relatedRepairExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        cancellationToken.ThrowIfCancellationRequested();
        
        var conditions = action == RepairAction.Rollback
            ? proposal.StructuredRollbackPreconditions
            : proposal.StructuredPreconditions;
        
        if (conditions.Count == 0) 
            return Array.Empty<RepairConditionResult>();

        try
        {
            var generationAtStart = evidenceGate.CurrentGeneration;
            var latest = await history.GetLatestAsync(cancellationToken).ConfigureAwait(false);
            var rules = await knowledgeRepository.GetLatestRulesAsync(cancellationToken).ConfigureAwait(false);
            
            var matchingRules = rules
                .Where(rule => string.Equals(rule.Id, proposal.RuleId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            
            var currentRule = matchingRules.OrderByDescending(rule => rule.Version).FirstOrDefault();
            
            var runIsCurrent = latest?.Id == proposal.DiagnosticRunId && latest.Report is not null
                && proposal.EvidenceGeneration == generationAtStart
                && evidenceGate.CurrentDiagnosticRunId == proposal.DiagnosticRunId;
            
            var matchingFindings = runIsCurrent
                ? latest!.Report!.Results
                    .Where(result => result.Status == DiagnosticStatus.Finding
                    && string.Equals(DiagnosticFindingIdentity.Create(result), proposal.FindingIdentity, StringComparison.Ordinal))
                    .ToArray()
                : Array.Empty<DiagnosticResult>();
            
            var findingIsPresent = matchingFindings.Length == 1;
            var exactRuleVersion = currentRule is not null && currentRule.Version == proposal.RuleVersion;
            var ruleIsSafe = currentRule?.Procedure is
                { ManualOnly: false, IsModifying: false, RequiresElevation: false, RequiresUserConfirmation: true };
            
            var allowlisted = exactRuleVersion && ruleIsSafe
                && allowlist.TryGetDefinition(proposal.RuleId, proposal.RuleVersion, out var definition)
                && definition is not null && definition.Kind == proposal.Kind
                && definition.PlanVersion == proposal.PlanVersion
                && definition.OperationVersion == proposal.OperationVersion;
            
            var findingMatchesRule = await EvaluateFindingMatchAsync(findingIsPresent, exactRuleVersion, latest, proposal, rules).
 ConfigureAwait(false);
            
            var generationStillCurrent = evidenceGate.CurrentGeneration == generationAtStart;
            
            return conditions.Select(condition => new RepairConditionResult(condition.Kind, condition.Kind switch
            {
                RepairConditionKind.DiagnosticRunIsCurrent => runIsCurrent && generationStillCurrent
                    ? RepairConditionStatus.Verified : RepairConditionStatus.Failed,
                RepairConditionKind.FindingIsPresent => findingIsPresent && generationStillCurrent
                    ? RepairConditionStatus.Verified : RepairConditionStatus.Failed,
                RepairConditionKind.RuleVersionIsCurrent => exactRuleVersion && generationStillCurrent
                    ? RepairConditionStatus.Verified : RepairConditionStatus.Failed,
                RepairConditionKind.FindingMatchesRule => findingMatchesRule && generationStillCurrent
                    ? RepairConditionStatus.Verified : RepairConditionStatus.Failed,
                RepairConditionKind.RuleIsAllowlisted => allowlisted && generationStillCurrent
                    ? RepairConditionStatus.Verified : RepairConditionStatus.Failed,
                RepairConditionKind.OriginalExecutionSucceeded => RepairConditionStatus.Failed,
                _ => RepairConditionStatus.Failed
            })).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Falha ao avaliar pré-condições de reparo; tipo: {ExceptionType}", exception.GetType().Name);
            return Array.Empty<RepairConditionResult>();
        }
    }

    private async Task<bool> EvaluateFindingMatchAsync(
        bool findingIsPresent,
        bool exactRuleVersion,
        DiagnosticRun? latest,
        RepairProposal proposal,
        IReadOnlyList<KnowledgeRule> rules)
    {
        if (!findingIsPresent || !exactRuleVersion || latest?.Report is null)
            return false;

        try
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
                        return true;
                }
            }
            
            return false;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Erro ao avaliar matching de finding; tipo: {ExceptionType}", exception.GetType().Name);
            return false;
        }
    }
}
