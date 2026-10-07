namespace WindowsDoctorAI.Domain;

/// <summary>Estado geral da projeção read-only compartilhada por Home e relatório HTML.</summary>
public enum ManualGuidanceAssessmentStatus
{
    NoDiagnosticReport,
    NoFindings,
    KnowledgeBaseEmpty,
    NoMatchingRule,
    GuidanceAvailable,
    Incomplete,
    AmbiguousFindings
}

/// <summary>Estado de um achado redigido, sem seleção ou execução de reparo.</summary>
public enum ManualGuidanceFindingStatus
{
    NoMatch,
    SingleCandidate,
    MultipleCandidates,
    Incomplete,
    KnowledgeBaseEmpty,
    AmbiguousDuplicate
}

/// <summary>Resultado temporário de avaliação manual; não é persistido com evidências.</summary>
public sealed record ManualGuidanceAssessment(
    ManualGuidanceAssessmentStatus Status,
    string StatusText,
    bool KnowledgeBaseIsEmpty,
    IReadOnlyList<ManualGuidanceFinding> Findings);

/// <summary>Cartão redigido por achado, vinculado a um run real e à identidade estável compartilhada do finding.</summary>
public sealed record ManualGuidanceFinding(
    string RunReferenceText,
    string FindingIdentityText,
    string FindingHeading,
    bool IsAmbiguousDuplicate,
    string EvidenceText,
    string ProviderText,
    ManualGuidanceFindingStatus Status,
    string StatusText,
    IReadOnlyList<ManualGuidanceRecommendation> Recommendations,
    IReadOnlyList<ManualGuidanceIncompleteCandidate> IncompleteCandidates)
{
    /// <summary>Run real ao qual este cartão foi projetado; usado para rejeitar snapshots de outra execução.</summary>
    public required Guid DiagnosticRunId { get; init; }

    /// <summary>Identidade estável compatível com DiagnosticFindingIdentity.Create para o achado redigido.</summary>
    public required string FindingIdentity { get; init; }
}

/// <summary>Orientação declarada, sempre ManualOnly e jamais executada por esta projeção.</summary>
public sealed record ManualGuidanceRecommendation(
    string ManualOnlyStatusText,
    string RuleIdentityText,
    string Title,
    string Domain,
    string MatchStrengthText,
    string MatchExplanation,
    string Explanation,
    string ApplicabilityText,
    string DeclaredApplicability,
    string DeclaredPackageSourceText,
    string PackageVersionText,
    string PackageSha256Text,
    string DiagnosticAction,
    string CorrectiveAction,
    string SolutionsDisclosureText,
    IReadOnlyList<string> Solutions,
    string ReferencesDisclosureText,
    IReadOnlyList<ManualGuidanceReference> References,
    string Risk,
    string RequiredPrivilege,
    string ElevationText,
    string Backup,
    string Rollback,
    string SourceLimitation);

/// <summary>Candidato não confirmado porque faltam requisitos estruturados ou dados de inventário.</summary>
public sealed record ManualGuidanceIncompleteCandidate(
    string RuleIdentityText,
    string Title,
    string MatchStrengthText,
    string ApplicabilityText,
    string Reason,
    string DeclaredPackageSourceText,
    string PackageVersionText,
    string PackageSha256Text);

/// <summary>Link declarativo que passou validação HTTPS; não autentica a fonte referenciada.</summary>
public sealed record ManualGuidanceReference(string Title, string HttpsUrl);
