using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Repair;

/// <summary>Resultado declarado pelo plugin. Detalhes não devem conter segredos ou logs integrais do sistema.</summary>
public sealed record RepairPluginResult(string Details);

/// <summary>
/// Contexto de uma tentativa. O plugin deve aguardar MarkStartedAsync antes de qualquer efeito; o engine persiste
/// esse marco sem usar o token de cancelamento, para que a trilha sobreviva ao cancelamento da ação.
/// </summary>
public sealed class RepairExecutionContext
{
    private readonly Func<Task> _markStarted;
    private readonly SemaphoreSlim _startGate = new(1, 1);

    internal RepairExecutionContext(
        Guid repairExecutionId,
        RepairAction action,
        Guid? relatedRepairExecutionId,
        Func<Task> markStarted)
    {
        RepairExecutionId = repairExecutionId;
        Action = action;
        RelatedRepairExecutionId = relatedRepairExecutionId;
        _markStarted = markStarted;
    }

    public Guid RepairExecutionId { get; }
    public RepairAction Action { get; }
    public Guid? RelatedRepairExecutionId { get; }
    public bool ExecutionStarted { get; private set; }

    public async Task MarkStartedAsync(CancellationToken cancellationToken = default)
    {
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ExecutionStarted) return;
            cancellationToken.ThrowIfCancellationRequested();
            await _markStarted().ConfigureAwait(false);
            ExecutionStarted = true;
        }
        finally
        {
            _startGate.Release();
        }
    }
}

/// <summary>
/// Plugin confiável registrado em código. A sobrecarga com contexto mantém compatibilidade com plugins antigos,
/// mas marca conservadoramente o início antes de delegar à implementação legada. Rollback legado sem ID não é usado.
/// </summary>
public interface IRepairPlugin
{
    RepairProposal Proposal { get; }

    Task<string> ExecuteAsync(CancellationToken cancellationToken = default);

    Task<string> RollbackAsync(CancellationToken cancellationToken = default);

    async Task<RepairPluginResult> ExecuteAsync(
        RepairExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        await context.MarkStartedAsync(cancellationToken).ConfigureAwait(false);
        var details = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return new RepairPluginResult(details);
    }

    Task<RepairPluginResult> RollbackAsync(
        Guid repairExecutionId,
        RepairExecutionContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromException<RepairPluginResult>(new NotSupportedException(
            "O plugin não implementa rollback vinculado a um RepairExecutionId."));

    Task<RepairPostconditionReport> VerifyPostconditionsAsync(
        RepairAction action,
        Guid repairExecutionId,
        Guid? relatedRepairExecutionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RepairPostconditionReport.NotEvaluated);
}

/// <summary>Orquestrador auditável. A composição do app não registra plugin que altere o sistema.</summary>
public sealed class RepairEngine(IEnumerable<IRepairPlugin> plugins, IRepairAuditLog auditLog)
{
    private readonly IReadOnlyDictionary<string, IRepairPlugin> _plugins = plugins
        .ToDictionary(plugin => plugin.Proposal.Id, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RepairProposal> GetProposals() => _plugins.Values
        .Select(plugin => plugin.Proposal)
        .OrderBy(proposal => proposal.Title, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    /// <summary>Executa somente quando o consentimento corresponde à versão, risco, alvo e conteúdo exatos do plano.</summary>
    public async Task<RepairHistoryRecord> ExecuteAsync(
        string proposalId,
        RepairConsent? consent,
        CancellationToken cancellationToken = default)
    {
        proposalId ??= string.Empty;
        var executionId = Guid.NewGuid();
        if (!_plugins.TryGetValue(proposalId, out var plugin))
            return await SaveUnknownAttemptAsync(executionId, proposalId, RepairAction.Execute, null,
                "A proposta não está catalogada por um plugin confiável registrado.").ConfigureAwait(false);

        var proposal = plugin.Proposal;
        if (consent is null || !consent.IsBoundTo(proposal, RepairAction.Execute))
        {
            var details = consent is null
                ? "A proposta foi recusada ou não recebeu consentimento vinculado; o plugin não foi iniciado."
                : "O consentimento não corresponde ao RepairId, versão, risco, alvo e conteúdo atuais do plano; o plugin não foi iniciado.";
            var declined = CreateRecord(executionId, proposal, RepairExecutionStatus.Declined, false,
                DateTimeOffset.UtcNow, details, RepairAction.Execute, null, consent);
            await auditLog.SaveAsync(declined, CancellationToken.None).ConfigureAwait(false);
            return declined;
        }

        var now = DateTimeOffset.UtcNow;
        var entry = CreateRecord(executionId, proposal, RepairExecutionStatus.Prepared, true, now,
            "Consentimento validado; aguardando o plugin sinalizar início antes de qualquer efeito.",
            RepairAction.Execute, null, consent);

        // Persistência prévia é uma barreira: se ela falhar, o plugin não é chamado.
        await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
        var context = new RepairExecutionContext(executionId, RepairAction.Execute, null, async () =>
        {
            entry = entry with
            {
                Status = RepairExecutionStatus.Started,
                ExecutionStarted = true,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Details = "O plugin sinalizou início; o resultado ainda não está concluído."
            };
            await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
        });

        try
        {
            var result = await plugin.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            if (!context.ExecutionStarted)
            {
                entry = Finish(entry, RepairExecutionStatus.Failed,
                    "O plugin retornou sem sinalizar início; o contrato foi violado e o resultado não foi aceito.", false);
            }
            else
            {
                var postconditions = await plugin.VerifyPostconditionsAsync(
                    RepairAction.Execute, executionId, null, cancellationToken).ConfigureAwait(false);
                entry = FinishFromPluginResult(entry, result, postconditions, RepairAction.Execute);
            }
        }
        catch (OperationCanceledException)
        {
            entry = Finish(entry, RepairExecutionStatus.Cancelled,
                context.ExecutionStarted
                    ? "A execução foi cancelada após o início; o efeito pode ser parcial ou inconclusivo."
                    : "A tentativa foi cancelada antes do início do plugin; nenhum efeito foi confirmado.",
                context.ExecutionStarted);
        }
        catch (NotSupportedException exception)
        {
            entry = Finish(entry,
                context.ExecutionStarted ? RepairExecutionStatus.Failed : RepairExecutionStatus.NotImplemented,
                SafeExceptionDetails(exception, context.ExecutionStarted ? "após o início" : "antes do início"),
                context.ExecutionStarted);
        }
        catch (Exception exception)
        {
            entry = Finish(entry, RepairExecutionStatus.Failed,
                SafeExceptionDetails(exception, context.ExecutionStarted ? "após o início" : "antes do início"),
                context.ExecutionStarted);
        }

        await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
        return entry;
    }

    /// <summary>
    /// Compatibilidade de assinatura somente: um booleano não prova qual plano foi exibido e nunca autoriza execução.
    /// </summary>
    [Obsolete("Use ExecuteAsync(proposalId, RepairConsent) para consentimento vinculado ao plano.")]
    public Task<RepairHistoryRecord> ExecuteAsync(
        string proposalId,
        bool userConfirmed,
        CancellationToken cancellationToken = default) =>
        RejectLegacyAttemptAsync(proposalId, RepairAction.Execute, null,
            userConfirmed
                ? "A confirmação booleana legada não identifica plano, versão, risco e alvo; nenhuma ação foi iniciada."
                : "A proposta foi recusada; o plugin não foi iniciado.");

    /// <summary>Rollback exige consentimento separado e o ID da execução bem-sucedida que será revertida.</summary>
    public async Task<RepairHistoryRecord> RollbackAsync(
        Guid repairExecutionId,
        RepairConsent? consent,
        CancellationToken cancellationToken = default)
    {
        var rollbackAttemptId = Guid.NewGuid();
        var source = repairExecutionId == Guid.Empty
            ? null
            : await auditLog.GetByExecutionIdAsync(repairExecutionId, CancellationToken.None).ConfigureAwait(false);

        if (source is null)
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, "",
                "Não foi encontrada uma execução para o RepairExecutionId informado.", repairExecutionId, consent)
                .ConfigureAwait(false);

        if (source.Action != RepairAction.Execute || source.Status != RepairExecutionStatus.Succeeded || !source.ExecutionStarted)
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, source.ProposalId,
                "Rollback permitido somente para uma execução identificada, iniciada e concluída com sucesso.",
                repairExecutionId, consent, source).ConfigureAwait(false);

        if (!_plugins.TryGetValue(source.ProposalId, out var plugin))
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, source.ProposalId,
                "O plugin da execução original não está registrado.", repairExecutionId, consent, source)
                .ConfigureAwait(false);

        var proposal = plugin.Proposal;
        if (!proposal.SupportsRollback)
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, proposal.Id,
                "O plano não declara suporte a rollback; nenhuma ação foi iniciada.", repairExecutionId,
                consent, source, RepairExecutionStatus.NotImplemented).ConfigureAwait(false);

        if (string.IsNullOrEmpty(source.PlanFingerprint)
            || !string.Equals(source.PlanFingerprint, consent?.PlanFingerprint, StringComparison.Ordinal)
            || consent is null
            || !consent.IsBoundTo(proposal, RepairAction.Rollback, repairExecutionId))
        {
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, proposal.Id,
                "O consentimento de rollback não corresponde ao plano atual e à execução original específica.",
                repairExecutionId, consent, source).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        var entry = CreateRecord(rollbackAttemptId, proposal, RepairExecutionStatus.Prepared, true, now,
            "Consentimento de rollback validado para a execução original; aguardando início do plugin.",
            RepairAction.Rollback, repairExecutionId, consent);
        await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
        var context = new RepairExecutionContext(rollbackAttemptId, RepairAction.Rollback, repairExecutionId, async () =>
        {
            entry = entry with
            {
                Status = RepairExecutionStatus.Started,
                ExecutionStarted = true,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Details = $"O plugin sinalizou início do rollback da execução {repairExecutionId:D}; resultado pendente."
            };
            await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
        });

        try
        {
            var result = await plugin.RollbackAsync(repairExecutionId, context, cancellationToken).ConfigureAwait(false);
            if (!context.ExecutionStarted)
            {
                entry = Finish(entry, RepairExecutionStatus.RollbackFailed,
                    "O plugin retornou sem sinalizar início; o contrato de rollback foi violado.", false);
            }
            else
            {
                var postconditions = await plugin.VerifyPostconditionsAsync(
                    RepairAction.Rollback, rollbackAttemptId, repairExecutionId, cancellationToken).ConfigureAwait(false);
                entry = FinishFromPluginResult(entry, result, postconditions, RepairAction.Rollback);
            }
        }
        catch (OperationCanceledException)
        {
            entry = Finish(entry, RepairExecutionStatus.Cancelled,
                context.ExecutionStarted
                    ? "O rollback foi cancelado após o início; o estado resultante pode ser parcial ou inconclusivo."
                    : "O rollback foi cancelado antes do início do plugin.", context.ExecutionStarted);
        }
        catch (NotSupportedException exception)
        {
            entry = Finish(entry,
                context.ExecutionStarted ? RepairExecutionStatus.RollbackFailed : RepairExecutionStatus.NotImplemented,
                SafeExceptionDetails(exception, context.ExecutionStarted ? "durante rollback já iniciado" : "antes do início do rollback"),
                context.ExecutionStarted);
        }
        catch (Exception exception)
        {
            entry = Finish(entry, RepairExecutionStatus.RollbackFailed,
                SafeExceptionDetails(exception, context.ExecutionStarted ? "durante rollback já iniciado" : "antes do início do rollback"),
                context.ExecutionStarted);
        }

        await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
        return entry;
    }

    /// <summary>
    /// Compatibilidade de assinatura somente: a API antiga não identifica execução nem consentimento para a ação rollback.
    /// </summary>
    [Obsolete("Use RollbackAsync(repairExecutionId, RepairConsent) para vincular o rollback à execução original.")]
    public Task<RepairHistoryRecord> RollbackAsync(
        string proposalId,
        bool userConfirmed,
        CancellationToken cancellationToken = default) =>
        RejectLegacyAttemptAsync(proposalId, RepairAction.Rollback, null,
            userConfirmed
                ? "A API legada não fornece RepairExecutionId nem consentimento específico de rollback; nenhuma ação foi iniciada."
                : "O rollback foi recusado; nenhum plugin foi iniciado.",
            userConfirmed ? RepairExecutionStatus.NotImplemented : RepairExecutionStatus.Declined);

    private async Task<RepairHistoryRecord> RejectLegacyAttemptAsync(
        string proposalId,
        RepairAction action,
        Guid? relatedExecutionId,
        string details,
        RepairExecutionStatus status = RepairExecutionStatus.Declined)
    {
        proposalId ??= string.Empty;
        var executionId = Guid.NewGuid();
        if (_plugins.TryGetValue(proposalId, out var plugin))
        {
            var proposal = plugin.Proposal;
            var entry = CreateRecord(executionId, proposal, status, false, DateTimeOffset.UtcNow,
                details, action, relatedExecutionId, null);
            await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
            return entry;
        }
        return await SaveUnknownAttemptAsync(executionId, proposalId, action, relatedExecutionId, details, status)
            .ConfigureAwait(false);
    }

    private async Task<RepairHistoryRecord> SaveUnqualifiedRollbackAsync(
        Guid rollbackAttemptId,
        string proposalId,
        string details,
        Guid relatedExecutionId,
        RepairConsent? consent,
        RepairHistoryRecord? source = null,
        RepairExecutionStatus status = RepairExecutionStatus.Declined)
    {
        if (_plugins.TryGetValue(proposalId, out var plugin))
        {
            var entry = CreateRecord(rollbackAttemptId, plugin.Proposal, status, false, DateTimeOffset.UtcNow,
                details, RepairAction.Rollback, relatedExecutionId, consent);
            await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
            return entry;
        }

        var unknown = await SaveUnknownAttemptAsync(rollbackAttemptId, proposalId, RepairAction.Rollback,
            relatedExecutionId, details, status, consent).ConfigureAwait(false);
        return source is null ? unknown : unknown with { Risk = source.Risk, Title = $"Rollback: {source.Title}" };
    }

    private async Task<RepairHistoryRecord> SaveUnknownAttemptAsync(
        Guid executionId,
        string proposalId,
        RepairAction action,
        Guid? relatedExecutionId,
        string details,
        RepairExecutionStatus status = RepairExecutionStatus.NotImplemented,
        RepairConsent? consent = null)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new RepairHistoryRecord(executionId, proposalId, "Proposta não catalogada",
            status, RepairRiskLevel.Unknown, false, false, now, now, details)
        {
            Action = action,
            RelatedRepairExecutionId = relatedExecutionId,
            ConsentId = consent?.ConsentId,
            ConsentConfirmedAtUtc = consent?.ConfirmedAtUtc,
            PlanFingerprint = consent?.PlanFingerprint ?? string.Empty
        };
        await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
        return entry;
    }

    private static RepairHistoryRecord CreateRecord(
        Guid executionId,
        RepairProposal proposal,
        RepairExecutionStatus status,
        bool userConfirmed,
        DateTimeOffset now,
        string details,
        RepairAction action,
        Guid? relatedExecutionId,
        RepairConsent? consent) =>
        new(executionId, proposal.Id, action == RepairAction.Rollback ? $"Rollback: {proposal.Title}" : proposal.Title,
            status, proposal.Risk, userConfirmed, proposal.SupportsRollback, now, now, details)
        {
            PlanVersion = proposal.PlanVersion,
            Target = proposal.Target,
            Preconditions = (proposal.Preconditions ?? Array.Empty<string>()).ToArray(),
            Postconditions = (proposal.Postconditions ?? Array.Empty<string>()).ToArray(),
            RollbackPreconditions = (proposal.RollbackPreconditions ?? Array.Empty<string>()).ToArray(),
            RollbackPostconditions = (proposal.RollbackPostconditions ?? Array.Empty<string>()).ToArray(),
            Action = action,
            RelatedRepairExecutionId = relatedExecutionId,
            ConsentId = consent?.ConsentId,
            ConsentConfirmedAtUtc = consent?.ConfirmedAtUtc,
            PlanFingerprint = consent?.PlanFingerprint ?? string.Empty
        };

    private static RepairHistoryRecord Finish(
        RepairHistoryRecord entry,
        RepairExecutionStatus status,
        string details,
        bool executionStarted,
        RepairPostconditionReport? postconditions = null) =>
        entry with
        {
            Status = status,
            ExecutionStarted = executionStarted,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Details = Limit(details),
            PostconditionStatus = postconditions?.Status ?? RepairPostconditionStatus.NotEvaluated,
            PostconditionDetails = Limit(postconditions?.Details ?? "")
        };

    private static RepairHistoryRecord FinishFromPluginResult(
        RepairHistoryRecord entry,
        RepairPluginResult result,
        RepairPostconditionReport postconditions,
        RepairAction action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(postconditions);
        var status = postconditions.Status == RepairPostconditionStatus.Failed
            ? action == RepairAction.Rollback ? RepairExecutionStatus.RollbackFailed : RepairExecutionStatus.Failed
            : action == RepairAction.Rollback ? RepairExecutionStatus.RolledBack : RepairExecutionStatus.Succeeded;
        var details = string.IsNullOrWhiteSpace(result.Details)
            ? "O plugin retornou sem detalhes adicionais."
            : result.Details;
        if (postconditions.Status == RepairPostconditionStatus.Failed)
            details = $"{details} Pós-condições declaradas não foram confirmadas: {postconditions.Details}";
        else if (postconditions.Status == RepairPostconditionStatus.NotEvaluated)
            details = $"{details} Pós-condições não avaliadas automaticamente.";
        return Finish(entry, status, details, true, postconditions);
    }

    private static string SafeExceptionDetails(Exception exception, string stage) =>
        $"{exception.GetType().Name} {stage}; a mensagem e dados internos da exceção foram omitidos da auditoria.";

    private static string Limit(string value) => value.Length <= 2000 ? value : value[..2000];
}

/// <summary>Plugin demonstrativo inerte; nunca chama processo, API do Windows ou altera o sistema.</summary>
public sealed class InertDemonstrationRepairPlugin : IRepairPlugin
{
    public RepairProposal Proposal { get; } = new(
        "demo.inert-preview",
        "Demonstração inerte (nenhum reparo)",
        "Exercita o contrato de consentimento e auditoria sem alterar o computador.",
        RepairRiskLevel.Low,
        "Nenhum impacto no sistema; apenas registra a demonstração.",
        RequiresExplicitApproval: true,
        SupportsRollback: false,
        PlanVersion: 1,
        Target: "Demonstração local; nenhum alvo do sistema",
        Preconditions: ["Somente o fluxo inerte de demonstração está registrado."],
        Postconditions: ["Nenhum estado do sistema deve ser alterado."]);

    public Task<string> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("Demonstração concluída: nenhum comando foi executado e nenhuma alteração foi aplicada.");
    }

    public Task<string> RollbackAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new NotSupportedException("A demonstração inerte não tem estado para reverter."));
}
