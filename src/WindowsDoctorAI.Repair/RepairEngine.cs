using System.Security.Cryptography;
using System.Text;
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
public sealed class RepairEngine
{
    private readonly IRepairAuditLog auditLog;
    private readonly IRepairPreconditionEvaluator preconditionEvaluator;
    private readonly IRepairProposalAllowlist allowlist;
    private readonly IRepairEvidenceGate evidenceGate;
    private readonly HashSet<string> _ambiguousProposalIds;
    private readonly IReadOnlyDictionary<string, IRepairPlugin> _plugins;

    public RepairEngine(
        IEnumerable<IRepairPlugin> plugins,
        IRepairAuditLog auditLog,
        IRepairPreconditionEvaluator preconditionEvaluator,
        IRepairProposalAllowlist allowlist,
        IRepairEvidenceGate evidenceGate)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        this.auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        this.preconditionEvaluator = preconditionEvaluator ?? throw new ArgumentNullException(nameof(preconditionEvaluator));
        this.allowlist = allowlist ?? throw new ArgumentNullException(nameof(allowlist));
        this.evidenceGate = evidenceGate ?? throw new ArgumentNullException(nameof(evidenceGate));

        var allPlugins = plugins.ToArray();
        _ambiguousProposalIds = allPlugins
            .GroupBy(plugin => plugin.Proposal.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _plugins = allPlugins
            .Where(plugin => !_ambiguousProposalIds.Contains(plugin.Proposal.Id))
            .ToDictionary(plugin => plugin.Proposal.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<RepairProposal> GetProposals() => _plugins.Values
        .Select(plugin => plugin.Proposal)
        .Where(IsWellFormedAllowlistedPlan)
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
        if (_ambiguousProposalIds.Contains(proposalId))
            return await SaveUnknownAttemptAsync(executionId, proposalId, RepairAction.Execute, null,
                "ID da proposta ambíguo por duplicidade no catálogo; nenhuma ação foi iniciada.",
                RepairExecutionStatus.Declined).ConfigureAwait(false);
        if (!_plugins.TryGetValue(proposalId, out var plugin))
            return await SaveUnknownAttemptAsync(executionId, proposalId, RepairAction.Execute, null,
                "A proposta não está catalogada por um plugin confiável registrado.").ConfigureAwait(false);

        var proposal = plugin.Proposal;
        if (!IsWellFormedAllowlistedPlan(proposal))
        {
            var rejectedPlan = CreateRecord(executionId, proposal, RepairExecutionStatus.Declined, false,
                DateTimeOffset.UtcNow, "O plano não corresponde a uma operação tipada permitida; nenhuma ação foi iniciada.",
                RepairAction.Execute, null, consent);
            await auditLog.SaveAsync(rejectedPlan, CancellationToken.None).ConfigureAwait(false);
            return rejectedPlan;
        }

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

        IReadOnlyList<RepairConditionResult> preconditionResults;
        try
        {
            preconditionResults = await preconditionEvaluator.EvaluateAsync(
                proposal, RepairAction.Execute, null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            preconditionResults = NotEvaluated(proposal.StructuredPreconditions);
            var cancelledBeforePreparation = CreateRecord(executionId, proposal, RepairExecutionStatus.Cancelled, true,
                DateTimeOffset.UtcNow, "Validação cancelada antes do início; nenhum efeito foi iniciado.",
                RepairAction.Execute, null, consent, preconditionResults);
            var rejectedAttempt = await SaveAttemptOrReplayOrQuarantineAsync(
                cancelledBeforePreparation, proposal, RepairAction.Execute, null, consent).ConfigureAwait(false);
            if (rejectedAttempt is not null) return rejectedAttempt;
            return cancelledBeforePreparation;
        }
        catch (Exception)
        {
            preconditionResults = NotEvaluated(proposal.StructuredPreconditions);
        }

        if (!AreConditionsVerified(proposal.StructuredPreconditions, preconditionResults))
        {
            var blocked = CreateRecord(executionId, proposal, RepairExecutionStatus.Declined, true,
                DateTimeOffset.UtcNow, "Pré-condições estruturadas ausentes, falsas ou não verificadas; nenhuma ação foi iniciada.",
                RepairAction.Execute, null, consent, preconditionResults);
            var rejectedAttempt = await SaveAttemptOrReplayOrQuarantineAsync(
                blocked, proposal, RepairAction.Execute, null, consent).ConfigureAwait(false);
            if (rejectedAttempt is not null) return rejectedAttempt;
            return blocked;
        }

        var now = DateTimeOffset.UtcNow;
        var entry = CreateRecord(executionId, proposal, RepairExecutionStatus.Prepared, true, now,
            "Consentimento validado; aguardando o plugin sinalizar início antes de qualquer efeito.",
            RepairAction.Execute, null, consent, preconditionResults);

        // Reserva de uso único e estado Prepared são atômicos; se a persistência falhar, o plugin não é chamado.
        var preparedFailure = await SaveAttemptOrReplayOrQuarantineAsync(
            entry, proposal, RepairAction.Execute, null, consent).ConfigureAwait(false);
        if (preparedFailure is not null) return preparedFailure;
        var context = new RepairExecutionContext(executionId, RepairAction.Execute, null, async () =>
        {
            await using var evidenceLease = await evidenceGate.AcquireAsync(CancellationToken.None).ConfigureAwait(false);
            IReadOnlyList<RepairConditionResult> startConditions;
            try
            {
                startConditions = await preconditionEvaluator.EvaluateAsync(
                    proposal, RepairAction.Execute, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                startConditions = NotEvaluated(proposal.StructuredPreconditions);
            }
            if (proposal.EvidenceGeneration != evidenceLease.CurrentGeneration
                || evidenceGate.CurrentDiagnosticRunId != proposal.DiagnosticRunId)
                startConditions = MarkRunConditionFailed(startConditions);
            if (!AreConditionsVerified(proposal.StructuredPreconditions, startConditions))
            {
                entry = Finish(entry, RepairExecutionStatus.Declined,
                    "Pré-condições tornaram-se ausentes, falsas ou obsoletas depois de Prepared e antes de Started; nenhum efeito foi iniciado.", false)
                    with { PreconditionResults = startConditions };
                await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
                throw new PreconditionsRejectedException();
            }
            entry = entry with
            {
                Status = RepairExecutionStatus.Started,
                ExecutionStarted = true,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Details = "O plugin sinalizou início após revalidar as pré-condições; o resultado ainda não está concluído.",
                PreconditionResults = startConditions
            };
            if (!await auditLog.TryMarkStartedAsync(entry, CancellationToken.None).ConfigureAwait(false))
            {
                entry = Finish(entry, RepairExecutionStatus.Declined,
                    "Finding em quarentena: outra tentativa já alcançou Started neste DiagnosticRunId; nenhum efeito foi iniciado por esta tentativa.", false);
                await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
                throw new PreconditionsRejectedException();
            }
        });

        IRepairEvidenceLease? terminalEvidenceLease = null;
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
                terminalEvidenceLease = await evidenceGate.AcquireAsync(CancellationToken.None).ConfigureAwait(false);
                var postconditions = await plugin.VerifyPostconditionsAsync(
                    RepairAction.Execute, executionId, null, cancellationToken).ConfigureAwait(false);
                if (proposal.EvidenceGeneration != terminalEvidenceLease.CurrentGeneration
                    || evidenceGate.CurrentDiagnosticRunId != proposal.DiagnosticRunId)
                {
                    var stalePostconditions = new RepairPostconditionReport(RepairPostconditionStatus.NotEvaluated,
                        "A evidência mudou depois de Started; sucesso não pode ser confirmado.")
                    {
                        Conditions = NotEvaluated(proposal.StructuredPostconditions)
                    };
                    entry = Finish(entry, RepairExecutionStatus.Inconclusive,
                        "O snapshot mudou após Started; o estado pode ser parcial ou inconclusivo. Nenhum rollback automático foi tentado.", true,
                        stalePostconditions);
                }
                else
                {
                    entry = FinishFromPluginResult(entry, result, postconditions, RepairAction.Execute);
                }
            }
        }
        catch (PreconditionsRejectedException)
        {
            // A tentativa Prepared já foi atualizada para Declined; nenhum executor foi iniciado.
        }
        catch (OperationCanceledException)
        {
            entry = Finish(entry, context.ExecutionStarted ? RepairExecutionStatus.Inconclusive : RepairExecutionStatus.Cancelled,
                context.ExecutionStarted
                    ? "A execução foi cancelada após Started; o efeito pode ser parcial ou inconclusivo. Nenhum rollback automático foi tentado."
                    : "A tentativa foi cancelada antes do início do plugin; nenhum efeito foi confirmado.",
                context.ExecutionStarted);
        }
        catch (NotSupportedException exception)
        {
            entry = Finish(entry,
                context.ExecutionStarted ? RepairExecutionStatus.Inconclusive : RepairExecutionStatus.NotImplemented,
                SafeExceptionDetails(exception, context.ExecutionStarted ? "após o início" : "antes do início"),
                context.ExecutionStarted);
        }
        catch (Exception exception)
        {
            entry = Finish(entry, context.ExecutionStarted ? RepairExecutionStatus.Inconclusive : RepairExecutionStatus.Failed,
                SafeExceptionDetails(exception, context.ExecutionStarted ? "após o início" : "antes do início"),
                context.ExecutionStarted);
        }

        try
        {
            await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (terminalEvidenceLease is not null)
                await terminalEvidenceLease.DisposeAsync().ConfigureAwait(false);
        }
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

        if (_ambiguousProposalIds.Contains(source.ProposalId))
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, source.ProposalId,
                "ID da proposta ambíguo por duplicidade no catálogo; rollback não iniciado.",
                repairExecutionId, consent, source, RepairExecutionStatus.Declined).ConfigureAwait(false);

        if (source.Action != RepairAction.Execute || source.Status != RepairExecutionStatus.Succeeded || !source.ExecutionStarted)
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, source.ProposalId,
                "Rollback permitido somente para uma execução identificada, iniciada e concluída com sucesso.",
                repairExecutionId, consent, source).ConfigureAwait(false);

        if (!_plugins.TryGetValue(source.ProposalId, out var plugin))
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, source.ProposalId,
                "O plugin da execução original não está registrado.", repairExecutionId, consent, source)
                .ConfigureAwait(false);

        var proposal = plugin.Proposal;
        if (!IsWellFormedAllowlistedPlan(proposal))
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, proposal.Id,
                "O plano de rollback não corresponde a uma operação tipada permitida.", repairExecutionId,
                consent, source, RepairExecutionStatus.Declined).ConfigureAwait(false);

        if (!proposal.SupportsRollback)
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, proposal.Id,
                "O plano não declara suporte a rollback; nenhuma ação foi iniciada.", repairExecutionId,
                consent, source, RepairExecutionStatus.NotImplemented).ConfigureAwait(false);

        if (string.IsNullOrEmpty(source.PlanFingerprint)
            || !string.Equals(source.PlanFingerprint, RepairConsent.FingerprintFor(proposal, RepairAction.Execute), StringComparison.Ordinal)
            || consent is null
            || !consent.IsBoundTo(proposal, RepairAction.Rollback, repairExecutionId))
        {
            return await SaveUnqualifiedRollbackAsync(rollbackAttemptId, proposal.Id,
                "O consentimento de rollback não corresponde ao plano atual e à execução original específica.",
                repairExecutionId, consent, source).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<RepairConditionResult> preconditionResults;
        try
        {
            preconditionResults = await preconditionEvaluator.EvaluateAsync(
                proposal, RepairAction.Rollback, repairExecutionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            preconditionResults = NotEvaluated(proposal.StructuredRollbackPreconditions);
        }

        if (!AreConditionsVerified(proposal.StructuredRollbackPreconditions, preconditionResults))
        {
            var blocked = CreateRecord(rollbackAttemptId, proposal, RepairExecutionStatus.Declined, true, now,
                "Pré-condições estruturadas de rollback ausentes, falsas ou não verificadas; nenhuma ação foi iniciada.",
                RepairAction.Rollback, repairExecutionId, consent, preconditionResults);
            var rejectedAttempt = await SaveAttemptOrReplayOrQuarantineAsync(
                blocked, proposal, RepairAction.Rollback, repairExecutionId, consent).ConfigureAwait(false);
            if (rejectedAttempt is not null) return rejectedAttempt;
            return blocked;
        }

        var entry = CreateRecord(rollbackAttemptId, proposal, RepairExecutionStatus.Prepared, true, now,
            "Consentimento de rollback validado para a execução original; aguardando início do plugin.",
            RepairAction.Rollback, repairExecutionId, consent, preconditionResults);
        var preparedFailure = await SaveAttemptOrReplayOrQuarantineAsync(
            entry, proposal, RepairAction.Rollback, repairExecutionId, consent).ConfigureAwait(false);
        if (preparedFailure is not null) return preparedFailure;
        var context = new RepairExecutionContext(rollbackAttemptId, RepairAction.Rollback, repairExecutionId, async () =>
        {
            await using var evidenceLease = await evidenceGate.AcquireAsync(CancellationToken.None).ConfigureAwait(false);
            IReadOnlyList<RepairConditionResult> startConditions;
            try
            {
                startConditions = await preconditionEvaluator.EvaluateAsync(
                    proposal, RepairAction.Rollback, repairExecutionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                startConditions = NotEvaluated(proposal.StructuredRollbackPreconditions);
            }
            if (proposal.EvidenceGeneration != evidenceLease.CurrentGeneration
                || evidenceGate.CurrentDiagnosticRunId != proposal.DiagnosticRunId)
                startConditions = MarkRunConditionFailed(startConditions);
            if (!AreConditionsVerified(proposal.StructuredRollbackPreconditions, startConditions))
            {
                entry = Finish(entry, RepairExecutionStatus.Declined,
                    "Pré-condições de rollback tornaram-se ausentes, falsas ou obsoletas antes de Started; nenhum efeito foi iniciado.", false)
                    with { PreconditionResults = startConditions };
                await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
                throw new PreconditionsRejectedException();
            }
            entry = entry with
            {
                Status = RepairExecutionStatus.Started,
                ExecutionStarted = true,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Details = $"O plugin sinalizou início do rollback da execução {repairExecutionId:D} após revalidar pré-condições; resultado pendente.",
                PreconditionResults = startConditions
            };
            if (!await auditLog.TryMarkStartedAsync(entry, CancellationToken.None).ConfigureAwait(false))
            {
                entry = Finish(entry, RepairExecutionStatus.Declined,
                    "Finding em quarentena: outra tentativa já alcançou Started neste DiagnosticRunId; nenhum efeito foi iniciado por este rollback.", false);
                await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
                throw new PreconditionsRejectedException();
            }
        });

        IRepairEvidenceLease? terminalEvidenceLease = null;
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
                terminalEvidenceLease = await evidenceGate.AcquireAsync(CancellationToken.None).ConfigureAwait(false);
                var postconditions = await plugin.VerifyPostconditionsAsync(
                    RepairAction.Rollback, rollbackAttemptId, repairExecutionId, cancellationToken).ConfigureAwait(false);
                if (proposal.EvidenceGeneration != terminalEvidenceLease.CurrentGeneration
                    || evidenceGate.CurrentDiagnosticRunId != proposal.DiagnosticRunId)
                {
                    var stalePostconditions = new RepairPostconditionReport(RepairPostconditionStatus.NotEvaluated,
                        "A evidência mudou depois de Started; sucesso do rollback não pode ser confirmado.")
                    {
                        Conditions = NotEvaluated(proposal.StructuredRollbackPostconditions)
                    };
                    entry = Finish(entry, RepairExecutionStatus.Inconclusive,
                        "O snapshot mudou após Started; o rollback pode estar parcial ou inconclusivo. Nenhum rollback adicional foi tentado.", true,
                        stalePostconditions);
                }
                else
                {
                    entry = FinishFromPluginResult(entry, result, postconditions, RepairAction.Rollback);
                }
            }
        }
        catch (PreconditionsRejectedException)
        {
            // A tentativa Prepared já foi atualizada para Declined; nenhum executor foi iniciado.
        }
        catch (OperationCanceledException)
        {
            entry = Finish(entry, context.ExecutionStarted ? RepairExecutionStatus.Inconclusive : RepairExecutionStatus.Cancelled,
                context.ExecutionStarted
                    ? "O rollback foi cancelado após Started; o estado pode ser parcial ou inconclusivo. Nenhum rollback adicional foi tentado."
                    : "O rollback foi cancelado antes do início do plugin.", context.ExecutionStarted);
        }
        catch (NotSupportedException exception)
        {
            entry = Finish(entry,
                context.ExecutionStarted ? RepairExecutionStatus.Inconclusive : RepairExecutionStatus.NotImplemented,
                SafeExceptionDetails(exception, context.ExecutionStarted ? "durante rollback já iniciado" : "antes do início do rollback"),
                context.ExecutionStarted);
        }
        catch (Exception exception)
        {
            entry = Finish(entry, context.ExecutionStarted ? RepairExecutionStatus.Inconclusive : RepairExecutionStatus.RollbackFailed,
                SafeExceptionDetails(exception, context.ExecutionStarted ? "durante rollback já iniciado" : "antes do início do rollback"),
                context.ExecutionStarted);
        }

        try
        {
            await auditLog.SaveAsync(entry, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (terminalEvidenceLease is not null)
                await terminalEvidenceLease.DisposeAsync().ConfigureAwait(false);
        }
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
        var entry = new RepairHistoryRecord(executionId, DiagnosticPrivacyRedactor.RedactText(proposalId), "Proposta não catalogada",
            status, RepairRiskLevel.Unknown, false, false, now, now, DiagnosticPrivacyRedactor.RedactText(details))
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

    private async Task<RepairHistoryRecord> SaveReplayAttemptAsync(
        Guid executionId,
        RepairProposal proposal,
        RepairAction action,
        Guid? relatedExecutionId,
        RepairConsent consent)
    {
        var replay = CreateRecord(executionId, proposal, RepairExecutionStatus.Declined, true,
            DateTimeOffset.UtcNow,
            "Este consentimento já foi consumido por outra tentativa; replay bloqueado antes de qualquer efeito.",
            action, relatedExecutionId, consent);
        await auditLog.SaveAsync(replay, CancellationToken.None).ConfigureAwait(false);
        return replay;
    }

    private async Task<RepairHistoryRecord?> SaveAttemptOrReplayOrQuarantineAsync(
        RepairHistoryRecord record,
        RepairProposal proposal,
        RepairAction action,
        Guid? relatedExecutionId,
        RepairConsent consent)
    {
        var result = await auditLog.TrySaveConsentAttemptAsync(record, CancellationToken.None).ConfigureAwait(false);
        return result.Status switch
        {
            RepairConsentAttemptStatus.Saved => null,
            RepairConsentAttemptStatus.Replay => await SaveReplayAttemptAsync(
                record.RepairExecutionId, proposal, action, relatedExecutionId, consent).ConfigureAwait(false),
            RepairConsentAttemptStatus.Quarantined => result.Record ?? await SaveFallbackQuarantineAttemptAsync(record).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Resultado de claim de consentimento desconhecido.")
        };
    }

    private async Task<RepairHistoryRecord> SaveFallbackQuarantineAttemptAsync(RepairHistoryRecord record)
    {
        var blocked = record with
        {
            Status = RepairExecutionStatus.Declined,
            ExecutionStarted = false,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Details = "Finding em quarentena após tentativa Started no mesmo DiagnosticRunId; execute novo diagnóstico."
        };
        await auditLog.SaveAsync(blocked, CancellationToken.None).ConfigureAwait(false);
        return blocked;
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
        RepairConsent? consent,
        IReadOnlyList<RepairConditionResult>? preconditionResults = null) =>
        new(executionId, proposal.Id, DiagnosticPrivacyRedactor.RedactText(
                action == RepairAction.Rollback ? $"Rollback: {proposal.Title}" : proposal.Title),
            status, proposal.Risk, userConfirmed, proposal.SupportsRollback, now, now, details)
        {
            PlanVersion = proposal.PlanVersion,
            Target = DiagnosticPrivacyRedactor.RedactText(proposal.Target),
            Preconditions = (proposal.Preconditions ?? Array.Empty<string>()).Select(value => DiagnosticPrivacyRedactor.RedactText(value)).ToArray(),
            Postconditions = (proposal.Postconditions ?? Array.Empty<string>()).Select(value => DiagnosticPrivacyRedactor.RedactText(value)).ToArray(),
            RollbackPreconditions = (proposal.RollbackPreconditions ?? Array.Empty<string>()).Select(value => DiagnosticPrivacyRedactor.RedactText(value)).ToArray(),
            RollbackPostconditions = (proposal.RollbackPostconditions ?? Array.Empty<string>()).Select(value => DiagnosticPrivacyRedactor.RedactText(value)).ToArray(),
            Action = action,
            RelatedRepairExecutionId = relatedExecutionId,
            ConsentId = consent?.ConsentId,
            ConsentConfirmedAtUtc = consent?.ConfirmedAtUtc,
            PlanFingerprint = consent?.PlanFingerprint ?? string.Empty,
            ProposalKind = proposal.Kind,
            OperationVersion = proposal.OperationVersion,
            DiagnosticRunId = proposal.DiagnosticRunId,
            EvidenceGeneration = proposal.EvidenceGeneration,
            FindingIdentity = proposal.FindingIdentity,
            RuleId = proposal.RuleId,
            RuleVersion = proposal.RuleVersion,
            EvidenceFingerprint = proposal.EvidenceFingerprint,
            RedactedEvidence = DiagnosticPrivacyRedactor.RedactText(proposal.RedactedEvidence),
            StructuredPreconditions = proposal.StructuredPreconditions.ToArray(),
            StructuredPostconditions = proposal.StructuredPostconditions.ToArray(),
            StructuredRollbackPreconditions = proposal.StructuredRollbackPreconditions.ToArray(),
            StructuredRollbackPostconditions = proposal.StructuredRollbackPostconditions.ToArray(),
            PreconditionResults = (preconditionResults ?? Array.Empty<RepairConditionResult>()).ToArray()
        };

    private bool IsWellFormedAllowlistedPlan(RepairProposal proposal)
    {
        if (proposal is null || proposal.Kind != RepairProposalKind.NoOpSimulation
            || !Enum.IsDefined(proposal.Kind) || proposal.OperationVersion < 1 || proposal.PlanVersion < 1
            || proposal.EvidenceGeneration < 0
            || proposal.DiagnosticRunId == Guid.Empty || proposal.RuleVersion < 1
            || proposal.Risk == RepairRiskLevel.Unknown || string.IsNullOrWhiteSpace(proposal.Target)
            || string.IsNullOrWhiteSpace(proposal.RuleId) || !IsSha256Fingerprint(proposal.FindingIdentity)
            || !IsValidEvidenceFingerprint(proposal.RedactedEvidence, proposal.EvidenceFingerprint)
            || !allowlist.TryGetDefinition(proposal.RuleId, proposal.RuleVersion, out var definition)
            || definition is null || definition.Kind != proposal.Kind
            || definition.PlanVersion != proposal.PlanVersion || definition.OperationVersion != proposal.OperationVersion
            || !(string.Equals(proposal.Id, definition.ProposalId, StringComparison.Ordinal)
                || proposal.Id.StartsWith(definition.ProposalId + ".", StringComparison.Ordinal))
            || !string.Equals(proposal.Title, definition.Title, StringComparison.Ordinal)
            || !string.Equals(proposal.Description, definition.Description, StringComparison.Ordinal)
            || !string.Equals(proposal.Impact, definition.Impact, StringComparison.Ordinal)
            || proposal.Risk != definition.Risk || !proposal.RequiresExplicitApproval
            || proposal.SupportsRollback != definition.SupportsRollback
            || !string.Equals(proposal.Target, definition.Target, StringComparison.Ordinal)
            || !(proposal.Preconditions ?? Array.Empty<string>()).SequenceEqual(
                definition.Preconditions.Select(condition => condition.DisplayText), StringComparer.Ordinal)
            || !(proposal.Postconditions ?? Array.Empty<string>()).SequenceEqual(
                definition.Postconditions.Select(condition => condition.DisplayText), StringComparer.Ordinal)
            || !(proposal.RollbackPreconditions ?? Array.Empty<string>()).SequenceEqual(
                (definition.RollbackPreconditions ?? Array.Empty<RepairPlanCondition>()).Select(condition => condition.DisplayText), StringComparer.Ordinal)
            || !(proposal.RollbackPostconditions ?? Array.Empty<string>()).SequenceEqual(
                (definition.RollbackPostconditions ?? Array.Empty<RepairPlanCondition>()).Select(condition => condition.DisplayText), StringComparer.Ordinal)
            || !(proposal.StructuredPreconditions ?? Array.Empty<RepairPlanCondition>()).SequenceEqual(definition.Preconditions)
            || !(proposal.StructuredPostconditions ?? Array.Empty<RepairPlanCondition>()).SequenceEqual(definition.Postconditions)
            || !(proposal.StructuredRollbackPreconditions ?? Array.Empty<RepairPlanCondition>()).SequenceEqual(
                definition.RollbackPreconditions ?? Array.Empty<RepairPlanCondition>())
            || !(proposal.StructuredRollbackPostconditions ?? Array.Empty<RepairPlanCondition>()).SequenceEqual(
                definition.RollbackPostconditions ?? Array.Empty<RepairPlanCondition>()))
            return false;

        var expectedPreconditions = new[]
        {
            RepairConditionKind.DiagnosticRunIsCurrent,
            RepairConditionKind.FindingIsPresent,
            RepairConditionKind.RuleVersionIsCurrent,
            RepairConditionKind.FindingMatchesRule,
            RepairConditionKind.RuleIsAllowlisted
        };
        return AreConditionKindsEqual(proposal.StructuredPreconditions, expectedPreconditions)
            && AreConditionKindsEqual(proposal.StructuredPostconditions,
                [RepairConditionKind.SimulationCompletedWithoutSystemChanges]);
    }

    private static bool IsSha256Fingerprint(string? fingerprint) =>
        fingerprint is { Length: 64 } && fingerprint.All(Uri.IsHexDigit);

    private static bool IsValidEvidenceFingerprint(string? redactedEvidence, string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(redactedEvidence) || !IsSha256Fingerprint(fingerprint)) return false;
        var computed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(redactedEvidence))).ToLowerInvariant();
        return string.Equals(computed, fingerprint, StringComparison.Ordinal);
    }

    private static bool AreConditionKindsEqual(
        IReadOnlyList<RepairPlanCondition>? conditions,
        IReadOnlyCollection<RepairConditionKind> expected) =>
        conditions is { Count: > 0 }
        && conditions.All(condition => condition is not null && Enum.IsDefined(condition.Kind))
        && conditions.Select(condition => condition.Kind).Distinct().Count() == conditions.Count
        && conditions.Count == expected.Count
        && expected.All(kind => conditions.Any(condition => condition.Kind == kind));

    private static bool AreConditionsVerified(
        IReadOnlyList<RepairPlanCondition>? expected,
        IReadOnlyList<RepairConditionResult>? results) =>
        expected is { Count: > 0 }
        && results is not null
        && results.Count == expected.Count
        && expected.All(condition => results.Count(result => result.Kind == condition.Kind
            && result.Status == RepairConditionStatus.Verified) == 1);

    private static IReadOnlyList<RepairConditionResult> NotEvaluated(IReadOnlyList<RepairPlanCondition> conditions) =>
        conditions.Select(condition => new RepairConditionResult(condition.Kind, RepairConditionStatus.NotEvaluated)).ToArray();

    private static IReadOnlyList<RepairConditionResult> MarkRunConditionFailed(
        IReadOnlyList<RepairConditionResult> results) => results.Select(result =>
            result.Kind == RepairConditionKind.DiagnosticRunIsCurrent
                ? result with { Status = RepairConditionStatus.Failed }
                : result).ToArray();

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
            Details = Limit(DiagnosticPrivacyRedactor.RedactText(details)),
            PostconditionStatus = postconditions?.Status ?? RepairPostconditionStatus.NotEvaluated,
            PostconditionDetails = Limit(DiagnosticPrivacyRedactor.RedactText(postconditions?.Details ?? string.Empty)),
            PostconditionResults = (postconditions?.Conditions ?? Array.Empty<RepairConditionResult>()).ToArray()
        };

    private static RepairHistoryRecord FinishFromPluginResult(
        RepairHistoryRecord entry,
        RepairPluginResult result,
        RepairPostconditionReport postconditions,
        RepairAction action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(postconditions);
        var expectedPostconditions = action == RepairAction.Rollback
            ? entry.StructuredRollbackPostconditions
            : entry.StructuredPostconditions;
        var allPostconditionsVerified = AreConditionsVerified(expectedPostconditions, postconditions.Conditions);
        var status = postconditions.Status == RepairPostconditionStatus.Verified && allPostconditionsVerified
            ? action == RepairAction.Rollback ? RepairExecutionStatus.RolledBack : RepairExecutionStatus.Succeeded
            : RepairExecutionStatus.Inconclusive;
        var auditedPostconditions = postconditions.Status == RepairPostconditionStatus.Verified && !allPostconditionsVerified
            ? postconditions with
            {
                Status = RepairPostconditionStatus.NotEvaluated,
                Details = "O verificador não forneceu resultados Verified para todas as pós-condições estruturadas."
            }
            : postconditions;
        var details = string.IsNullOrWhiteSpace(result.Details)
            ? "O plugin retornou sem detalhes adicionais."
            : DiagnosticPrivacyRedactor.RedactText(result.Details);
        if (postconditions.Status == RepairPostconditionStatus.Failed)
            details = $"{details} Pós-condições declaradas falharam; após Started, o efeito pode ser parcial ou inconclusivo. Detalhes: {postconditions.Details}";
        else if (postconditions.Status == RepairPostconditionStatus.NotEvaluated)
            details = $"{details} Pós-condições não avaliadas; sucesso não confirmado e estado inconclusivo.";
        else if (!allPostconditionsVerified)
            details = $"{details} Resultado de pós-condições incompleto; sucesso não confirmado.";
        return Finish(entry, status, details, true, auditedPostconditions);
    }

    private static string SafeExceptionDetails(Exception exception, string stage) =>
        $"{exception.GetType().Name} {stage}; a mensagem e dados internos da exceção foram omitidos da auditoria.";

    private sealed class PreconditionsRejectedException : Exception { }

    private static string Limit(string value) => value.Length <= 2000 ? value : value[..2000];
}
