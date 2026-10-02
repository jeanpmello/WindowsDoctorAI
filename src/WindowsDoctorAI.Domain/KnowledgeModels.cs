namespace WindowsDoctorAI.Domain;

/// <summary>Impacto editorial informado pela regra importada; não representa probabilidade nem severidade medida.</summary>
public enum KnowledgeImpact
{
    Low,
    Moderate,
    High,
    Critical
}

/// <summary>Força da correspondência observável. Não é probabilidade de causa ou de sucesso de uma solução.</summary>
public enum MatchConfidence
{
    Indeterminate,
    Low,
    Moderate,
    High
}

/// <summary>Referência declarada pela origem do pacote. O app não verifica automaticamente sua autenticidade.</summary>
public sealed record KnowledgeReference(string Title, string Url);

/// <summary>Condição estrita: código exato e contexto literal no mesmo achado do scanner indicado.</summary>
public sealed record KnowledgeMatchCondition(
    string ExactErrorCode,
    IReadOnlyList<string> ScannerNames,
    IReadOnlyList<string> RequiredContextTerms);

/// <summary>Orientação declarativa; nunca é executada pela aplicação.</summary>
public sealed record KnowledgeProcedure(
    string DiagnosticAction,
    string CorrectiveAction,
    string RequiredPrivilege,
    bool RequiresElevation,
    string Risk,
    string Backup,
    string Rollback,
    string SourceLimitation,
    bool IsModifying,
    bool RequiresUserConfirmation,
    bool ManualOnly);

/// <summary>Regra textual, inerte e versionada. Nenhum campo é tratado como código ou comando.</summary>
public sealed record KnowledgeRule(
    string Id,
    int Version,
    string Domain,
    string Title,
    KnowledgeImpact Impact,
    IReadOnlyList<string> ErrorCodes,
    IReadOnlyList<string> Symptoms,
    IReadOnlyList<string> Causes,
    IReadOnlyList<string> Solutions,
    IReadOnlyList<KnowledgeReference> References,
    string? Applicability = null,
    KnowledgeMatchCondition? Match = null,
    IReadOnlyList<string>? RequiredEvidence = null,
    KnowledgeProcedure? Procedure = null);

/// <summary>Envelope JSON suportado pelo importador (schemas 1.0 e 1.1).</summary>
public sealed record KnowledgePackage(
    string SchemaVersion,
    string Version,
    string Source,
    IReadOnlyList<KnowledgeRule> Rules);

public sealed record RecommendationEvidence(
    string ScannerName,
    string Category,
    string Title,
    string Evidence,
    DateTimeOffset TimestampUtc,
    string MatchedIndicator);

/// <summary>Recomendação explicável ligada à regra importada e a resultados efetivamente observados.</summary>
public sealed record DiagnosticRecommendation(
    string RuleId,
    int RuleVersion,
    string Title,
    string Domain,
    KnowledgeImpact Impact,
    MatchConfidence Confidence,
    string ConfidenceExplanation,
    string Explanation,
    IReadOnlyList<string> Causes,
    IReadOnlyList<string> Solutions,
    IReadOnlyList<RecommendationEvidence> Evidence,
    IReadOnlyList<KnowledgeReference> References,
    string? Applicability = null,
    IReadOnlyList<string>? RequiredEvidence = null,
    KnowledgeProcedure? Procedure = null);

public sealed record RootCauseEvidence(
    string ScannerName,
    string Category,
    string Title,
    string Evidence,
    DateTimeOffset TimestampUtc);

/// <summary>Associação observacional; deliberadamente não afirma causalidade.</summary>
public sealed record CorrelationObservation(
    string SharedIdentifier,
    MatchConfidence AssociationStrength,
    string Explanation,
    IReadOnlyList<RootCauseEvidence> Evidence);

public sealed record RootCauseAnalysis(
    string Summary,
    IReadOnlyList<CorrelationObservation> Correlations,
    bool CauseDetermined = false);

public enum RepairRiskLevel
{
    Unknown,
    Low,
    Moderate,
    High
}

public enum RepairExecutionStatus
{
    Declined,
    NotImplemented,
    Succeeded,
    Failed,
    RolledBack
}

/// <summary>Metadados para uma proposta; este milestone não inclui ações que alterem o Windows.</summary>
public sealed record RepairProposal(
    string Id,
    string Title,
    string Description,
    RepairRiskLevel Risk,
    string Impact,
    bool RequiresExplicitApproval = true,
    bool SupportsRollback = false);

/// <summary>Registro auditável sem comandos, segredos ou dados integrais do inventário.</summary>
public sealed record RepairHistoryRecord(
    Guid Id,
    string ProposalId,
    string Title,
    RepairExecutionStatus Status,
    RepairRiskLevel Risk,
    bool UserConfirmed,
    bool RollbackSupported,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string Details);
