using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Core;

/// <summary>Porta do caso de uso para obter um snapshot do dispositivo atual.</summary>
public interface IComputerInventoryScanner
{
    Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>Abstrai APIs locais de inventário para permitir testes sem WMI ou hardware real.</summary>
public interface IComputerInventoryDataSource
{
    Task<ComputerInventory> CollectAsync(CancellationToken cancellationToken = default);
}

/// <summary>Plugin de diagnóstico independente; implementações são registradas no contêiner de dependências.</summary>
public interface IDiagnosticScanner
{
    string Name { get; }
    string Category { get; }

    /// <summary>Indica se esta implementação pode executar simultaneamente com outros scanners.</summary>
    bool SupportsParallelExecution => true;

    Task<IReadOnlyList<DiagnosticResult>> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>Porta para executar e consolidar todos os scanners registrados.</summary>
public interface IDiagnosticEngine
{
    Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>Abstrai o seletor manual de CBS.log; null indica cancelamento e o stream deve ser somente leitura.</summary>
public interface ICbsLogFilePicker
{
    Task<Stream?> PickCbsLogAsync(CancellationToken cancellationToken = default);
}

/// <summary>Classifica tipos genéricos de marcadores CBS sem correlacioná-los a eventos ou pacotes.</summary>
public interface ICbsLogMarkerClassifier
{
    IReadOnlyList<CbsMarkerType> Classify(string? cbsLogText);
}

public interface IDiagnosticRunRepository
{
    Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken = default);
    Task<DiagnosticRun?> GetLatestAsync(CancellationToken cancellationToken = default);
    Task<int> DeleteCompletedBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
}

public interface IUserSettingsRepository
{
    Task<UserSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>Persistência local de regras; a implementação preserva versões anteriores e origem declarada.</summary>
public interface IKnowledgeRepository
{
    Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default);
    Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default);
}

/// <summary>Registro de auditoria local das propostas confirmadas ou recusadas.</summary>
public interface IRepairAuditLog
{
    Task SaveAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default);
    /// <summary>Grava a primeira tentativa vinculada ao consentimento de forma atômica; false significa replay.</summary>
    Task<bool> TrySaveConsentAttemptAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepairHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    async Task<RepairHistoryRecord?> GetByExecutionIdAsync(Guid repairExecutionId, CancellationToken cancellationToken = default)
    {
        var recent = await GetRecentAsync(500, cancellationToken).ConfigureAwait(false);
        return recent.FirstOrDefault(record => record.RepairExecutionId == repairExecutionId);
    }
}

/// <summary>Allowlist compilada de pares exatos de regra/versão e planos tipados.</summary>
public interface IRepairProposalAllowlist
{
    bool TryGetDefinition(string ruleId, int ruleVersion, out RepairProposalDefinition? definition);
}

/// <summary>Avalia pré-condições estruturadas contra a evidência atual; resultados ausentes bloqueiam.</summary>
public interface IRepairPreconditionEvaluator
{
    Task<IReadOnlyList<RepairConditionResult>> EvaluateAsync(
        RepairProposal proposal,
        RepairAction action,
        Guid? relatedRepairExecutionId,
        CancellationToken cancellationToken = default);
}
