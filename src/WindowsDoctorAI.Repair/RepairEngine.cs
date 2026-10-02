using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Repair;

/// <summary>Extensão confiável registrada em código; nenhum comando, script ou plugin é carregado de JSON/disco.</summary>
public interface IRepairPlugin
{
    RepairProposal Proposal { get; }
    Task<string> ExecuteAsync(CancellationToken cancellationToken = default);
    Task<string> RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>Orquestrador com barreira de confirmação e auditoria. A aplicação não registra plugin que altere o sistema.</summary>
public sealed class RepairEngine(IEnumerable<IRepairPlugin> plugins, IRepairAuditLog auditLog)
{
    private readonly IReadOnlyDictionary<string, IRepairPlugin> _plugins = plugins
        .ToDictionary(plugin => plugin.Proposal.Id, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RepairProposal> GetProposals() => _plugins.Values
        .Select(plugin => plugin.Proposal)
        .OrderBy(proposal => proposal.Title, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public async Task<RepairHistoryRecord> ExecuteAsync(
        string proposalId,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(proposalId, out var plugin))
            throw new KeyNotFoundException("A proposta não está catalogada por um plugin confiável registrado.");
        var proposal = plugin.Proposal;
        var started = DateTimeOffset.UtcNow;
        var status = RepairExecutionStatus.Declined;
        var details = "A proposta foi recusada; o plugin não foi executado e nenhuma alteração foi aplicada.";
        if (userConfirmed)
        {
            try
            {
                details = await plugin.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                status = RepairExecutionStatus.Succeeded;
            }
            catch (NotSupportedException exception)
            {
                details = exception.Message;
                status = RepairExecutionStatus.NotImplemented;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                details = $"Falha do plugin {plugin.GetType().Name}: {exception.GetType().Name}.";
                status = RepairExecutionStatus.Failed;
            }
        }

        var completed = DateTimeOffset.UtcNow;
        var entry = new RepairHistoryRecord(Guid.NewGuid(), proposal.Id, proposal.Title, status, proposal.Risk,
            userConfirmed, proposal.SupportsRollback, started, completed, details);
        await auditLog.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public async Task<RepairHistoryRecord> RollbackAsync(
        string proposalId,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(proposalId, out var plugin))
            throw new KeyNotFoundException("A proposta não está catalogada por um plugin confiável registrado.");
        var proposal = plugin.Proposal;
        var started = DateTimeOffset.UtcNow;
        var status = RepairExecutionStatus.Declined;
        var details = "O rollback foi recusado; o plugin não foi executado.";
        if (userConfirmed)
        {
            if (!proposal.SupportsRollback)
            {
                status = RepairExecutionStatus.NotImplemented;
                details = "Este plugin não declara suporte a rollback; nenhuma ação foi executada.";
            }
            else
            {
                try
                {
                    details = await plugin.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    status = RepairExecutionStatus.RolledBack;
                }
                catch (NotSupportedException exception)
                {
                    details = exception.Message;
                    status = RepairExecutionStatus.NotImplemented;
                }
            }
        }

        var completed = DateTimeOffset.UtcNow;
        var entry = new RepairHistoryRecord(Guid.NewGuid(), proposal.Id, $"Rollback: {proposal.Title}", status, proposal.Risk,
            userConfirmed, proposal.SupportsRollback, started, completed, details);
        await auditLog.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry;
    }
}

/// <summary>Plugin demonstrativo inerte, disponível para harnesses e testes; nunca chama processo ou API do Windows.</summary>
public sealed class InertDemonstrationRepairPlugin : IRepairPlugin
{
    public RepairProposal Proposal { get; } = new(
        "demo.inert-preview",
        "Demonstração inerte (nenhum reparo)",
        "Exercita confirmação e logging sem alterar o computador.",
        RepairRiskLevel.Low,
        "Nenhum impacto no sistema; apenas registra a simulação.",
        RequiresExplicitApproval: true,
        SupportsRollback: false);

    public Task<string> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("Demonstração concluída: nenhum comando foi executado e nenhuma alteração foi aplicada.");
    }

    public Task<string> RollbackAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new NotSupportedException("A demonstração inerte não tem estado para reverter."));
}
