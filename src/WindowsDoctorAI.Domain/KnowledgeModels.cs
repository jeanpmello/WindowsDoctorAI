using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>Famílias Windows aceitas no alvo estruturado de uma regra de conhecimento.</summary>
[JsonConverter(typeof(KnowledgeOperatingSystemFamilyJsonConverter))]
public enum KnowledgeOperatingSystemFamily
{
    WindowsClient,
    WindowsServer
}

/// <summary>Conversor estrito: o contrato JSON usa os nomes PascalCase exatos e não aceita números.</summary>
public sealed class KnowledgeOperatingSystemFamilyJsonConverter : JsonConverter<KnowledgeOperatingSystemFamily>
{
    public override KnowledgeOperatingSystemFamily Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A família de sistema operacional deve ser textual.");

        return reader.GetString() switch
        {
            nameof(KnowledgeOperatingSystemFamily.WindowsClient) => KnowledgeOperatingSystemFamily.WindowsClient,
            nameof(KnowledgeOperatingSystemFamily.WindowsServer) => KnowledgeOperatingSystemFamily.WindowsServer,
            _ => throw new JsonException("Família de sistema operacional desconhecida.")
        };
    }

    public override void Write(Utf8JsonWriter writer, KnowledgeOperatingSystemFamily value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value)) throw new JsonException("Família de sistema operacional desconhecida.");
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Referência declarada pela origem do pacote. O app não verifica automaticamente sua autenticidade.</summary>
public sealed record KnowledgeReference(string Title, string Url);

/// <summary>Condição estrita: código exato e contexto literal no mesmo achado do scanner indicado.</summary>
public sealed record KnowledgeMatchCondition(
    string ExactErrorCode,
    IReadOnlyList<string> ScannerNames,
    IReadOnlyList<string> RequiredContextTerms)
{
    /// <summary>Providers canônicos requeridos na origem estruturada do achado, nunca no texto livre.</summary>
    public IReadOnlyList<string> RequiredSourceProviders { get; init; } = Array.Empty<string>();

    /// <summary>Tipos de evidência estruturada exigidos; quando há vários, qualquer um deles satisfaz a condição.</summary>
    public IReadOnlyList<string> RequiredEvidenceTypes { get; init; } = Array.Empty<string>();
}

/// <summary>Alvo explícito e inclusivo de família Windows e intervalo de builds para schema 1.4.</summary>
public sealed record KnowledgeOperatingSystemTarget(
    IReadOnlyList<KnowledgeOperatingSystemFamily> Families,
    long? MinimumBuild,
    long? MaximumBuild);

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
    KnowledgeProcedure? Procedure = null)
{
    /// <summary>Disponível somente em schema 1.4; Applicability textual permanece explicativa.</summary>
    public KnowledgeOperatingSystemTarget? OsTarget { get; init; }
}

/// <summary>Envelope JSON suportado pelo importador (schemas 1.0 a 1.4).</summary>
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
    KnowledgeProcedure? Procedure = null)
{
    /// <summary>Não nulo somente quando o matcher verificou o alvo estruturado contra inventário válido.</summary>
    public KnowledgeOperatingSystemTarget? OsTarget { get; init; }
}

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
    RolledBack,
    Prepared,
    Started,
    Cancelled,
    RollbackFailed
}

public enum RepairAction
{
    Execute,
    Rollback
}

public enum RepairPostconditionStatus
{
    NotEvaluated,
    Verified,
    Failed
}

/// <summary>Resultado declarado pelo plugin; NotEvaluated é o padrão e não afirma estado do Windows.</summary>
public sealed record RepairPostconditionReport(RepairPostconditionStatus Status, string Details)
{
    public static RepairPostconditionReport NotEvaluated { get; } =
        new(RepairPostconditionStatus.NotEvaluated, "Nenhuma verificação de pós-condições foi realizada.");
}

/// <summary>Metadados para uma proposta; este milestone não inclui ações que alterem o Windows.</summary>
public sealed record RepairProposal(
    string Id,
    string Title,
    string Description,
    RepairRiskLevel Risk,
    string Impact,
    bool RequiresExplicitApproval = true,
    bool SupportsRollback = false,
    int PlanVersion = 1,
    string Target = "",
    IReadOnlyList<string>? Preconditions = null,
    IReadOnlyList<string>? Postconditions = null,
    IReadOnlyList<string>? RollbackPreconditions = null,
    IReadOnlyList<string>? RollbackPostconditions = null);

/// <summary>Consentimento imutável para exatamente uma ação, versão do plano, risco e alvo.</summary>
public sealed class RepairConsent
{
    private RepairConsent(
        Guid consentId,
        string repairId,
        int planVersion,
        RepairRiskLevel risk,
        string target,
        RepairAction action,
        Guid? relatedRepairExecutionId,
        DateTimeOffset confirmedAtUtc,
        string planFingerprint)
    {
        ConsentId = consentId;
        RepairId = repairId;
        PlanVersion = planVersion;
        Risk = risk;
        Target = target;
        Action = action;
        RelatedRepairExecutionId = relatedRepairExecutionId;
        ConfirmedAtUtc = confirmedAtUtc;
        PlanFingerprint = planFingerprint;
    }

    public Guid ConsentId { get; }
    public string RepairId { get; }
    public int PlanVersion { get; }
    public RepairRiskLevel Risk { get; }
    public string Target { get; }
    public RepairAction Action { get; }
    public Guid? RelatedRepairExecutionId { get; }
    public DateTimeOffset ConfirmedAtUtc { get; }
    public string PlanFingerprint { get; }

    /// <summary>Crie somente depois de apresentar e confirmar a proposta exata ao usuário.</summary>
    public static RepairConsent Confirm(RepairProposal proposal, DateTimeOffset? confirmedAtUtc = null) =>
        Create(proposal, RepairAction.Execute, null, confirmedAtUtc);

    /// <summary>Rollback requer confirmação separada e referência à execução que será revertida.</summary>
    public static RepairConsent ConfirmRollback(
        RepairProposal proposal,
        Guid repairExecutionId,
        DateTimeOffset? confirmedAtUtc = null)
    {
        if (repairExecutionId == Guid.Empty)
            throw new ArgumentException("A execução original deve ter um ID válido.", nameof(repairExecutionId));
        return Create(proposal, RepairAction.Rollback, repairExecutionId, confirmedAtUtc);
    }

    public bool IsBoundTo(RepairProposal proposal, RepairAction action, Guid? relatedRepairExecutionId = null) =>
        action == Action
        && relatedRepairExecutionId == RelatedRepairExecutionId
        && string.Equals(RepairId, proposal.Id, StringComparison.Ordinal)
        && PlanVersion == proposal.PlanVersion
        && Risk == proposal.Risk
        && string.Equals(Target, proposal.Target, StringComparison.Ordinal)
        && string.Equals(PlanFingerprint, CalculatePlanFingerprint(proposal), StringComparison.Ordinal);

    internal static string CalculatePlanFingerprint(RepairProposal proposal)
    {
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            proposal.Id,
            proposal.PlanVersion,
            proposal.Title,
            proposal.Description,
            proposal.Risk,
            proposal.Impact,
            proposal.RequiresExplicitApproval,
            proposal.SupportsRollback,
            proposal.Target,
            Preconditions = proposal.Preconditions ?? Array.Empty<string>(),
            Postconditions = proposal.Postconditions ?? Array.Empty<string>(),
            RollbackPreconditions = proposal.RollbackPreconditions ?? Array.Empty<string>(),
            RollbackPostconditions = proposal.RollbackPostconditions ?? Array.Empty<string>()
        });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
    }

    private static RepairConsent Create(
        RepairProposal proposal,
        RepairAction action,
        Guid? relatedRepairExecutionId,
        DateTimeOffset? confirmedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposal.Id);
        if (proposal.PlanVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(proposal), "A versão do plano deve ser positiva.");
        if (proposal.Risk == RepairRiskLevel.Unknown)
            throw new InvalidOperationException("Não é possível consentir com um plano cujo risco não foi declarado.");
        ArgumentException.ThrowIfNullOrWhiteSpace(proposal.Target);

        return new RepairConsent(Guid.NewGuid(), proposal.Id, proposal.PlanVersion, proposal.Risk, proposal.Target,
            action, relatedRepairExecutionId, confirmedAtUtc ?? DateTimeOffset.UtcNow,
            CalculatePlanFingerprint(proposal));
    }
}

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
    string Details)
{
    /// <summary>O ID primário do registro também identifica a tentativa de execução, inclusive recusas.</summary>
    public Guid RepairExecutionId => Id;
    public string RepairId => ProposalId;
    public int PlanVersion { get; init; } = 1;
    public string Target { get; init; } = string.Empty;
    public IReadOnlyList<string> Preconditions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Postconditions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RollbackPreconditions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RollbackPostconditions { get; init; } = Array.Empty<string>();
    public RepairAction Action { get; init; } = RepairAction.Execute;
    public Guid? RelatedRepairExecutionId { get; init; }
    public Guid? ConsentId { get; init; }
    public DateTimeOffset? ConsentConfirmedAtUtc { get; init; }
    public string PlanFingerprint { get; init; } = string.Empty;
    public bool ExecutionStarted { get; init; }
    public RepairPostconditionStatus PostconditionStatus { get; init; } = RepairPostconditionStatus.NotEvaluated;
    public string PostconditionDetails { get; init; } = "";
}
