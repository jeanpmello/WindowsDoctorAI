using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Database;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Repair;

namespace WindowsDoctorAI.Tests;

public sealed class RepairEngineTests
{
    [Fact]
    public async Task RefusalIsAuditedBeforePluginStarts()
    {
        var plugin = new DeterministicRepairPlugin(CreateProposal());
        var audit = new InMemoryRepairAuditLog();
        var engine = CreateEngine(plugin, audit);

        var result = await engine.ExecuteAsync(plugin.Proposal.Id, consent: null);

        Assert.Equal(RepairExecutionStatus.Declined, result.Status);
        Assert.NotEqual(Guid.Empty, result.RepairExecutionId);
        Assert.False(result.ExecutionStarted);
        Assert.Equal(0, plugin.ExecuteCount);
        Assert.Same(result, Assert.Single(audit.Records));
    }

    [Fact]
    public async Task SuccessPersistsActionBoundConsentAndVerifiedPostcondition()
    {
        var proposal = CreateProposal();
        var audit = new InMemoryRepairAuditLog();
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Verification = VerifiedSimulationPostconditions(),
            Execute = async (context, cancellationToken) =>
            {
                Assert.Equal(RepairExecutionStatus.Prepared, Assert.Single(audit.Records).Status);
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("fake determinístico concluído");
            }
        };
        var engine = CreateEngine(plugin, audit);
        var consent = RepairConsent.Confirm(proposal, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        var result = await engine.ExecuteAsync(proposal.Id, consent);

        Assert.Equal(RepairExecutionStatus.Succeeded, result.Status);
        Assert.True(result.ExecutionStarted);
        Assert.Equal(result.RepairExecutionId, result.Id);
        Assert.Equal(proposal.Id, result.RepairId);
        Assert.Equal(proposal.PlanVersion, result.PlanVersion);
        Assert.Equal(proposal.Risk, result.Risk);
        Assert.Equal(proposal.Target, result.Target);
        Assert.Equal(consent.ConsentId, result.ConsentId);
        Assert.Equal(consent.ConfirmedAtUtc, result.ConsentConfirmedAtUtc);
        Assert.Equal(RepairPostconditionStatus.Verified, result.PostconditionStatus);
        Assert.Equal(RepairAction.Execute, result.Action);
        Assert.Equal(1, plugin.VerifyCount);
        Assert.DoesNotContain("não avaliadas", result.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Same(result, Assert.Single(audit.Records));
    }

    [Fact]
    public void FingerprintChangesWithActionVersionTargetRiskAndStructuredConditions()
    {
        var proposal = CreateProposal();
        var fingerprint = RepairConsent.FingerprintFor(proposal);

        Assert.NotEqual(fingerprint, RepairConsent.FingerprintFor(proposal with { OperationVersion = proposal.OperationVersion + 1 }));
        Assert.NotEqual(fingerprint, RepairConsent.FingerprintFor(proposal with { PlanVersion = proposal.PlanVersion + 1 }));
        Assert.NotEqual(fingerprint, RepairConsent.FingerprintFor(proposal with { Target = "outro alvo" }));
        Assert.NotEqual(fingerprint, RepairConsent.FingerprintFor(proposal with { Risk = RepairRiskLevel.Moderate }));
        Assert.NotEqual(fingerprint, RepairConsent.FingerprintFor(proposal with
        {
            StructuredPreconditions = [new RepairPlanCondition(RepairConditionKind.RuleVersionIsCurrent)]
        }));
        Assert.NotEqual(fingerprint, RepairConsent.FingerprintFor(proposal with
        {
            StructuredPostconditions = [new RepairPlanCondition(RepairConditionKind.FindingIsPresent)]
        }));
        Assert.NotEqual(fingerprint, RepairConsent.FingerprintFor(proposal, RepairAction.Rollback));
    }

    [Fact]
    public async Task ConsentIsOneShotAndReplayIsAuditedWithoutCallingPluginAgain()
    {
        var proposal = CreateProposal();
        var audit = new InMemoryRepairAuditLog();
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Verification = VerifiedSimulationPostconditions(),
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("simulação fake");
            }
        };
        var engine = CreateEngine(plugin, audit);
        var consent = RepairConsent.Confirm(proposal);

        var first = await engine.ExecuteAsync(proposal.Id, consent);
        var replay = await engine.ExecuteAsync(proposal.Id, consent);

        Assert.Equal(RepairExecutionStatus.Succeeded, first.Status);
        Assert.Equal(RepairExecutionStatus.Declined, replay.Status);
        Assert.Contains("replay bloqueado", replay.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, plugin.ExecuteCount);
        Assert.Equal(2, audit.Records.Count);
        Assert.All(audit.Records, record => Assert.Equal(consent.ConsentId, record.ConsentId));
    }

    [Fact]
    public async Task NewConsentCannotRestartSamePlanActionInSameDiagnosticRun()
    {
        var proposal = CreateProposal();
        var audit = new InMemoryRepairAuditLog();
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Verification = VerifiedSimulationPostconditions(),
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("simulação fake concluída");
            }
        };
        var engine = CreateEngine(plugin, audit);

        var first = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));
        var second = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.Equal(RepairExecutionStatus.Succeeded, first.Status);
        Assert.Equal(RepairExecutionStatus.Declined, second.Status);
        Assert.Contains("quarentena", second.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, plugin.ExecuteCount);
    }

    [Fact]
    public async Task NotEvaluatedPostconditionNeverProducesSucceeded()
    {
        var proposal = CreateProposal();
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("simulação sem verificador");
            }
        };
        var engine = CreateEngine(plugin, new InMemoryRepairAuditLog());

        var result = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.Equal(RepairExecutionStatus.Inconclusive, result.Status);
        Assert.Equal(RepairPostconditionStatus.NotEvaluated, result.PostconditionStatus);
        Assert.True(result.ExecutionStarted);
    }

    [Theory]
    [InlineData(RepairPostconditionStatus.Failed)]
    [InlineData(RepairPostconditionStatus.Verified)]
    public async Task FalseOrMissingStructuredPostconditionBlocksSuccess(RepairPostconditionStatus reportedStatus)
    {
        var proposal = CreateProposal();
        var conditions = reportedStatus == RepairPostconditionStatus.Failed
            ? new[] { new RepairConditionResult(RepairConditionKind.SimulationCompletedWithoutSystemChanges, RepairConditionStatus.Failed) }
            : Array.Empty<RepairConditionResult>();
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Verification = new RepairPostconditionReport(reportedStatus, "Resultado incompleto.") { Conditions = conditions },
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("simulação fake");
            }
        };

        var result = await CreateEngine(plugin, new InMemoryRepairAuditLog())
            .ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.Equal(RepairExecutionStatus.Inconclusive, result.Status);
        Assert.NotEqual(RepairExecutionStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task AuditLinksConsentActionAndEvidenceWithoutPersistingSensitiveStrings()
    {
        const string secret = "fixture-token-123";
        const string email = "person@example.invalid";
        const string path = "C:\\Users\\Jean\\private-file.txt";
        var proposal = CreateProposal() with
        {
            RedactedEvidence = $"token={secret} contact {email} path {path}",
            EvidenceFingerprint = FingerprintEvidence($"token={secret} contact {email} path {path}")
        };
        var audit = new InMemoryRepairAuditLog();
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Verification = VerifiedSimulationPostconditions(),
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult($"token={secret} contact {email} path {path}");
            }
        };
        var engine = CreateEngine(plugin, audit);
        var consent = RepairConsent.Confirm(proposal);

        var record = await engine.ExecuteAsync(proposal.Id, consent);

        Assert.Equal(consent.ConsentId, record.ConsentId);
        Assert.Equal(RepairAction.Execute, record.Action);
        Assert.Equal(proposal.DiagnosticRunId, record.DiagnosticRunId);
        Assert.Equal(proposal.FindingIdentity, record.FindingIdentity);
        Assert.Equal(proposal.RuleId, record.RuleId);
        Assert.Equal(proposal.RuleVersion, record.RuleVersion);
        Assert.Equal(proposal.EvidenceFingerprint, record.EvidenceFingerprint);
        Assert.False(string.IsNullOrWhiteSpace(record.PlanFingerprint));
        Assert.DoesNotContain(secret, record.RedactedEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain(email, record.RedactedEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain(path, record.RedactedEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, record.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(email, record.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(path, record.Details, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrFalseStructuredPreconditionBlocksBeforeExecutor(bool returnFalse)
    {
        var proposal = CreateProposal();
        var audit = new InMemoryRepairAuditLog();
        var plugin = new DeterministicRepairPlugin(proposal);
        var results = returnFalse
            ? proposal.StructuredPreconditions.Select(condition => new RepairConditionResult(condition.Kind,
                condition.Kind == RepairConditionKind.FindingIsPresent ? RepairConditionStatus.Failed : RepairConditionStatus.Verified)).ToArray()
            : Array.Empty<RepairConditionResult>();
        var engine = CreateEngine(plugin, audit, new StaticPreconditionEvaluator(results));

        var result = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.Equal(RepairExecutionStatus.Declined, result.Status);
        Assert.False(result.ExecutionStarted);
        Assert.Equal(0, plugin.ExecuteCount);
        Assert.Equal(result, Assert.Single(audit.Records));
    }

    [Fact]
    public async Task PreparedPersistenceFailurePreventsExecutorInvocation()
    {
        var proposal = CreateProposal();
        var audit = new InMemoryRepairAuditLog { FailConsentClaim = true };
        var plugin = new DeterministicRepairPlugin(proposal);
        var engine = CreateEngine(plugin, audit);

        await Assert.ThrowsAsync<IOException>(() => engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal)));

        Assert.Equal(0, plugin.ExecuteCount);
        Assert.Empty(audit.Records);
    }

    [Fact]
    public async Task StartedPersistenceFailureStopsPluginBeforeAnyEffect()
    {
        var proposal = CreateProposal();
        var audit = new InMemoryRepairAuditLog { FailStartedWrites = true };
        var effectCount = 0;
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                effectCount++;
                return new RepairPluginResult("efeito fake");
            }
        };
        var engine = CreateEngine(plugin, audit);

        var result = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.Equal(RepairExecutionStatus.Failed, result.Status);
        Assert.False(result.ExecutionStarted);
        Assert.Equal(0, effectCount);
    }

    [Fact]
    public async Task TerminalPersistenceFailureAfterStartedQuarantinesPlanAndRejectsNewConsent()
    {
        var proposal = CreateProposal();
        var audit = new InMemoryRepairAuditLog { FailVerifiedTerminalWrites = true };
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Verification = VerifiedSimulationPostconditions(),
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("fake completed, terminal save fails");
            }
        };
        var engine = CreateEngine(plugin, audit);

        await Assert.ThrowsAsync<IOException>(() => engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal)));
        var retry = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.Equal(RepairExecutionStatus.Started,
            Assert.Single(audit.Records, record => record.Status == RepairExecutionStatus.Started).Status);
        Assert.Equal(RepairExecutionStatus.Declined, retry.Status);
        Assert.Contains("quarentena", retry.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("novo diagnóstico", retry.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, plugin.ExecuteCount);
        Assert.DoesNotContain(audit.Records, record => record.Status == RepairExecutionStatus.Succeeded);
    }

    [Fact]
    public async Task DuplicateCatalogProposalIdsAreExcludedAndAuditedInsteadOfThrowing()
    {
        var proposal = CreateProposal();
        var first = new DeterministicRepairPlugin(proposal);
        var second = new DeterministicRepairPlugin(proposal);
        var audit = new InMemoryRepairAuditLog();
        var engine = new RepairEngine([first, second], audit, new VerifiedPreconditionEvaluator(),
            new FixtureRepairProposalAllowlist(proposal), new RepairEvidenceGate());

        Assert.Empty(engine.GetProposals());
        var result = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.Equal(RepairExecutionStatus.Declined, result.Status);
        Assert.Contains("ambíguo", result.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, first.ExecuteCount + second.ExecuteCount);
    }

    [Fact]
    public async Task PreconditionsAreRevalidatedAfterPreparedAndBeforeStarted()
    {
        var proposal = CreateProposal();
        var audit = new InMemoryRepairAuditLog();
        var effectCount = 0;
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                effectCount++;
                return new RepairPluginResult("não deve alcançar este ponto");
            }
        };
        var evaluator = new StaleOnSecondEvaluation(proposal.StructuredPreconditions);
        var engine = CreateEngine(plugin, audit, evaluator);

        var result = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.Equal(RepairExecutionStatus.Declined, result.Status);
        Assert.False(result.ExecutionStarted);
        Assert.Equal(0, effectCount);
        Assert.Equal(2, evaluator.CallCount);
        Assert.Contains(result.PreconditionResults, condition => condition.Status == RepairConditionStatus.Failed);
    }

    [Fact]
    public async Task EvidenceChangeAfterPreparedButBeforeStartedInvalidatesConsentBeforeEffect()
    {
        var proposal = CreateProposal();
        var gate = new RepairEvidenceGate();
        await using (var lease = await gate.AcquireAsync())
            lease.MarkDiagnosticRunCurrent(proposal.DiagnosticRunId, proposal.EvidenceGeneration);
        var audit = new InMemoryRepairAuditLog();
        var effectCount = 0;
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Execute = async (context, cancellationToken) =>
            {
                // Stand-in for a new diagnostic/import acquiring the same evidence gate after Prepared.
                await using (var lease = await gate.AcquireAsync(cancellationToken)) lease.AdvanceGeneration();
                await context.MarkStartedAsync(cancellationToken);
                effectCount++;
                return new RepairPluginResult("efeito fake proibido após invalidação");
            }
        };
        var engine = CreateEngine(plugin, audit, evidenceGate: gate);

        var result = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.Equal(RepairExecutionStatus.Declined, result.Status);
        Assert.False(result.ExecutionStarted);
        Assert.Equal(0, effectCount);
        Assert.Contains(result.PreconditionResults, condition => condition.Kind == RepairConditionKind.DiagnosticRunIsCurrent
            && condition.Status == RepairConditionStatus.Failed);
        Assert.DoesNotContain(audit.Records, record => record.Status == RepairExecutionStatus.Started);
    }

    [Fact]
    public async Task ConcurrentEvidenceMutationWaitsUntilStartedIsDurablyRecorded()
    {
        var proposal = CreateProposal();
        var gate = new RepairEvidenceGate();
        await using (var lease = await gate.AcquireAsync())
            lease.MarkDiagnosticRunCurrent(proposal.DiagnosticRunId, proposal.EvidenceGeneration);
        var audit = new InMemoryRepairAuditLog();
        var evaluator = new GateRacingPreconditionEvaluator(gate, audit, proposal.StructuredPreconditions);
        var effectCount = 0;
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Verification = VerifiedSimulationPostconditions(),
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                await evaluator.MutationTask.WaitAsync(TimeSpan.FromSeconds(5));
                effectCount++;
                return new RepairPluginResult("fake após Started");
            }
        };
        var engine = CreateEngine(plugin, audit, evaluator, gate);

        var result = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.Equal(RepairExecutionStatus.Inconclusive, result.Status);
        Assert.Equal(RepairPostconditionStatus.NotEvaluated, result.PostconditionStatus);
        Assert.Equal(1, effectCount);
        Assert.NotEmpty(audit.StartedExecutionIds);
        Assert.DoesNotContain(audit.Records, record => record.Status == RepairExecutionStatus.Succeeded);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("risk")]
    [InlineData("target")]
    [InlineData("conditions")]
    public async Task ConsentForDifferentRepairPlanIsRejectedBeforeStart(string changedField)
    {
        var original = CreateProposal();
        var changed = changedField switch
        {
            "id" => original with { Id = "fixture.changed" },
            "version" => original with { PlanVersion = original.PlanVersion + 1 },
            "risk" => original with { Risk = RepairRiskLevel.Moderate },
            "target" => original with { Target = "outro alvo fake" },
            "conditions" => original with { Preconditions = ["condição alterada"] },
            _ => throw new ArgumentOutOfRangeException(nameof(changedField))
        };
        var plugin = new DeterministicRepairPlugin(changed);
        var audit = new InMemoryRepairAuditLog();
        var engine = CreateEngine(plugin, audit);

        var result = await engine.ExecuteAsync(changed.Id, RepairConsent.Confirm(original));

        Assert.Equal(RepairExecutionStatus.Declined, result.Status);
        Assert.False(result.ExecutionStarted);
        Assert.Equal(0, plugin.ExecuteCount);
        Assert.NotEqual(Guid.Empty, result.ConsentId);
    }

    [Fact]
    public async Task ExceptionBeforePluginStartIsAuditedWithoutClaimingStart()
    {
        var plugin = new DeterministicRepairPlugin(CreateProposal())
        {
            Execute = (_, _) => Task.FromException<RepairPluginResult>(new InvalidOperationException("fixture-only"))
        };
        var audit = new InMemoryRepairAuditLog();
        var engine = CreateEngine(plugin, audit);

        var result = await engine.ExecuteAsync(plugin.Proposal.Id, RepairConsent.Confirm(plugin.Proposal));

        Assert.Equal(RepairExecutionStatus.Failed, result.Status);
        Assert.False(result.ExecutionStarted);
        Assert.Equal(1, plugin.ExecuteCount);
        Assert.Equal(result, Assert.Single(audit.Records));
        Assert.DoesNotContain("fixture-only", result.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExceptionAfterPluginStartIsAuditedAsPotentiallyPartial()
    {
        var plugin = new DeterministicRepairPlugin(CreateProposal())
        {
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                throw new InvalidOperationException("fixture-only");
            }
        };
        var audit = new InMemoryRepairAuditLog();
        var engine = CreateEngine(plugin, audit);

        var result = await engine.ExecuteAsync(plugin.Proposal.Id, RepairConsent.Confirm(plugin.Proposal));

        Assert.Equal(RepairExecutionStatus.Inconclusive, result.Status);
        Assert.True(result.ExecutionStarted);
        Assert.Contains("após o início", result.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-only", result.Details, StringComparison.Ordinal);
        Assert.Equal(result, Assert.Single(audit.Records));
    }

    [Fact]
    public async Task CancellationAfterStartStillPersistsAuditWithoutCancelledToken()
    {
        using var cancellation = new CancellationTokenSource();
        var plugin = new DeterministicRepairPlugin(CreateProposal())
        {
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                cancellation.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
        };
        var audit = new InMemoryRepairAuditLog { RejectCancelledWrites = true };
        var engine = CreateEngine(plugin, audit);

        var result = await engine.ExecuteAsync(plugin.Proposal.Id,
            RepairConsent.Confirm(plugin.Proposal), cancellation.Token);

        Assert.Equal(RepairExecutionStatus.Inconclusive, result.Status);
        Assert.True(result.ExecutionStarted);
        Assert.Contains("parcial ou inconclusivo", result.Details, StringComparison.Ordinal);
        Assert.Equal(result, Assert.Single(audit.Records));
    }

    [Fact]
    public async Task RollbackIsBoundToOriginalExecutionAndUsesSeparateConsent()
    {
        var proposal = CreateProposal(supportsRollback: true);
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Verification = VerifiedSimulationPostconditions(),
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("fake execute");
            },
            Rollback = async (executionId, context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("fake rollback");
            }
        };
        var audit = new InMemoryRepairAuditLog();
        var engine = CreateEngine(plugin, audit);
        var executed = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));
        var rollbackConsent = RepairConsent.ConfirmRollback(proposal, executed.RepairExecutionId);

        var rolledBack = await engine.RollbackAsync(executed.RepairExecutionId, rollbackConsent);

        Assert.Equal(RepairExecutionStatus.RolledBack, rolledBack.Status);
        Assert.True(rolledBack.ExecutionStarted);
        Assert.NotEqual(executed.RepairExecutionId, rolledBack.RepairExecutionId);
        Assert.Equal(executed.RepairExecutionId, rolledBack.RelatedRepairExecutionId);
        Assert.Equal(executed.RepairExecutionId, plugin.LastRollbackExecutionId);
        Assert.Equal(rollbackConsent.ConsentId, rolledBack.ConsentId);
        Assert.Equal(RepairAction.Rollback, rolledBack.Action);
        Assert.Equal(2, audit.Records.Count);
    }

    [Fact]
    public async Task RollbackExceptionIsAuditedAndKeepsOriginalExecutionReference()
    {
        var proposal = CreateProposal(supportsRollback: true);
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Verification = VerifiedSimulationPostconditions(),
            Execute = async (context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("fake execute");
            },
            Rollback = async (executionId, context, cancellationToken) =>
            {
                await context.MarkStartedAsync(cancellationToken);
                throw new InvalidOperationException("fixture-only rollback error");
            }
        };
        var audit = new InMemoryRepairAuditLog();
        var engine = CreateEngine(plugin, audit);
        var executed = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        var result = await engine.RollbackAsync(executed.RepairExecutionId,
            RepairConsent.ConfirmRollback(proposal, executed.RepairExecutionId));

        Assert.Equal(RepairExecutionStatus.Inconclusive, result.Status);
        Assert.True(result.ExecutionStarted);
        Assert.Equal(executed.RepairExecutionId, result.RelatedRepairExecutionId);
        Assert.Equal(executed.RepairExecutionId, plugin.LastRollbackExecutionId);
        Assert.DoesNotContain("fixture-only rollback error", result.Details, StringComparison.Ordinal);
        Assert.Equal(2, audit.Records.Count);
    }

    [Fact]
    public async Task EnginePersistsPreparedRowToSqliteBeforeCallingPlugin()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var audit = new SqliteRepairAuditLog(context);
        var proposal = CreateProposal();
        var observedPersistedPreparation = false;
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Verification = VerifiedSimulationPostconditions(),
            Execute = async (executionContext, cancellationToken) =>
            {
                var persisted = await audit.GetByExecutionIdAsync(executionContext.RepairExecutionId);
                Assert.NotNull(persisted);
                Assert.Equal(RepairExecutionStatus.Prepared, persisted.Status);
                Assert.False(persisted.ExecutionStarted);
                observedPersistedPreparation = true;
                await executionContext.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("SQLite fake result");
            }
        };
        var engine = CreateEngine(plugin, audit);

        var result = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        Assert.True(observedPersistedPreparation);
        Assert.Equal(RepairExecutionStatus.Succeeded, result.Status);
        Assert.Equal(RepairExecutionStatus.Succeeded,
            (await audit.GetByExecutionIdAsync(result.RepairExecutionId))!.Status);
        Assert.Single(await audit.GetRecentAsync(10));
    }

    [Fact]
    public async Task SchemaV1RepairHistoryMigratesAndPreservesExistingRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var oldId = Guid.NewGuid();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "RepairHistory" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "ProposalId" TEXT NOT NULL,
                    "Title" TEXT NOT NULL,
                    "Status" INTEGER NOT NULL,
                    "Risk" INTEGER NOT NULL,
                    "UserConfirmed" INTEGER NOT NULL,
                    "RollbackSupported" INTEGER NOT NULL,
                    "StartedAtUnixMilliseconds" INTEGER NOT NULL,
                    "CompletedAtUnixMilliseconds" INTEGER NOT NULL,
                    "Details" TEXT NOT NULL);
                INSERT INTO "RepairHistory" VALUES ($id, 'legacy.item', 'Legacy', 2, 1, 1, 0, 10, 20, 'legacy record');
                PRAGMA user_version = 1;
                """;
            command.Parameters.AddWithValue("$id", oldId.ToString("N"));
            await command.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);

        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var audit = new SqliteRepairAuditLog(context);
        var loaded = Assert.Single(await audit.GetRecentAsync(10));
        Assert.Equal(oldId, loaded.RepairExecutionId);

        await using var verifyVersion = connection.CreateCommand();
        verifyVersion.CommandText = "PRAGMA user_version;";
        Assert.Equal(WindowsDoctorDatabaseMigrator.CurrentVersion,
            Convert.ToInt32(await verifyVersion.ExecuteScalarAsync()));
        Assert.Equal("legacy.item", loaded.RepairId);
        Assert.Equal("legacy record", loaded.Details);
        Assert.Equal(1, loaded.PlanVersion);
        Assert.Equal(RepairPostconditionStatus.NotEvaluated, loaded.PostconditionStatus);
    }

    [Fact]
    public async Task SchemaV6QuarantineMigratesActionColumnWithoutDroppingRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var runId = Guid.NewGuid();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "RepairStartedPlanQuarantines" (
                    "QuarantineKey" TEXT NOT NULL PRIMARY KEY,
                    "DiagnosticRunId" TEXT NOT NULL,
                    "FindingIdentity" TEXT NOT NULL,
                    "StartedRepairExecutionId" TEXT NOT NULL,
                    "StartedAtUnixMilliseconds" INTEGER NOT NULL,
                    "PlanFingerprint" TEXT NOT NULL);
                INSERT INTO "RepairStartedPlanQuarantines"
                    VALUES ('fixture-key', $run, 'fixture-finding', $execution, 123, 'fixture-fingerprint');
                PRAGMA user_version = 6;
                """;
            command.Parameters.AddWithValue("$run", runId.ToString("D"));
            command.Parameters.AddWithValue("$execution", Guid.NewGuid().ToString("D"));
            await command.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);

        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);

        await using var inspect = connection.CreateCommand();
        inspect.CommandText = "PRAGMA table_info(\"RepairStartedPlanQuarantines\");";
        await using var reader = await inspect.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        await reader.DisposeAsync();
        Assert.Contains("Action", columns);
        Assert.Equal(RepairAction.Execute,
            await context.RepairStartedPlanQuarantines.Select(row => row.Action).SingleAsync());
        await using var verifyVersion = connection.CreateCommand();
        verifyVersion.CommandText = "PRAGMA user_version;";
        Assert.Equal(WindowsDoctorDatabaseMigrator.CurrentVersion,
            Convert.ToInt32(await verifyVersion.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SqliteAuditLogUpsertsAndLoadsByRepairExecutionId()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var audit = new SqliteRepairAuditLog(context);
        var proposal = CreateProposal();
        var consent = RepairConsent.Confirm(proposal);
        var executionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var prepared = new RepairHistoryRecord(executionId, proposal.Id, proposal.Title,
            RepairExecutionStatus.Prepared, proposal.Risk, true, proposal.SupportsRollback,
            now, now, "prepared")
        {
            PlanVersion = proposal.PlanVersion,
            Target = proposal.Target,
            Preconditions = proposal.Preconditions!,
            Postconditions = proposal.Postconditions!,
            PlanFingerprint = consent.PlanFingerprint,
            ConsentId = consent.ConsentId,
            ConsentConfirmedAtUtc = consent.ConfirmedAtUtc,
            ProposalKind = proposal.Kind,
            OperationVersion = proposal.OperationVersion,
            DiagnosticRunId = proposal.DiagnosticRunId,
            EvidenceGeneration = proposal.EvidenceGeneration,
            FindingIdentity = proposal.FindingIdentity,
            RuleId = proposal.RuleId,
            RuleVersion = proposal.RuleVersion,
            EvidenceFingerprint = proposal.EvidenceFingerprint,
            RedactedEvidence = proposal.RedactedEvidence,
            StructuredPreconditions = proposal.StructuredPreconditions,
            StructuredPostconditions = proposal.StructuredPostconditions
        };

        await audit.SaveAsync(prepared);
        await audit.SaveAsync(prepared with
        {
            Status = RepairExecutionStatus.Started,
            ExecutionStarted = true,
            Details = "started"
        });

        var loaded = await audit.GetByExecutionIdAsync(executionId);
        Assert.NotNull(loaded);
        Assert.Equal(RepairExecutionStatus.Started, loaded.Status);
        Assert.True(loaded.ExecutionStarted);
        Assert.Equal(proposal.PlanVersion, loaded.PlanVersion);
        Assert.Equal(proposal.Target, loaded.Target);
        Assert.Equal(consent.ConsentId, loaded.ConsentId);
        Assert.Equal(proposal.EvidenceGeneration, loaded.EvidenceGeneration);
        Assert.Equal(proposal.Preconditions, loaded.Preconditions);
        Assert.Equal("started", loaded.Details);
        Assert.Single(await audit.GetRecentAsync(10));

    }

    [Fact]
    public async Task SqliteConsentClaimIsUniqueAndAtomicAcrossAttempts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        await using var context = new WindowsDoctorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context);
        var audit = new SqliteRepairAuditLog(context);
        var proposal = CreateProposal();
        var consent = RepairConsent.Confirm(proposal);
        var now = DateTimeOffset.UtcNow;
        RepairHistoryRecord Record(Guid attemptId) => new(
            attemptId, proposal.Id, proposal.Title, RepairExecutionStatus.Prepared, proposal.Risk,
            true, false, now, now, "prepared")
        {
            Action = RepairAction.Execute,
            ConsentId = consent.ConsentId,
            PlanFingerprint = consent.PlanFingerprint,
            ProposalKind = proposal.Kind,
            OperationVersion = proposal.OperationVersion,
            DiagnosticRunId = proposal.DiagnosticRunId,
            FindingIdentity = proposal.FindingIdentity,
            RuleId = proposal.RuleId,
            RuleVersion = proposal.RuleVersion,
            EvidenceFingerprint = proposal.EvidenceFingerprint,
            RedactedEvidence = proposal.RedactedEvidence
        };

        var first = await audit.TrySaveConsentAttemptAsync(Record(Guid.NewGuid()));
        var replay = await audit.TrySaveConsentAttemptAsync(Record(Guid.NewGuid()));

        Assert.Equal(RepairConsentAttemptStatus.Saved, first.Status);
        Assert.Equal(RepairConsentAttemptStatus.Replay, replay.Status);
        Assert.Equal(1, await context.RepairConsentUses.CountAsync());
        Assert.Single(await audit.GetRecentAsync(10));
    }

    [Fact]
    public async Task SqliteStartedQuarantineSurvivesNewAuditContextUntilNewDiagnosticRun()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WindowsDoctorDbContext>().UseSqlite(connection).Options;
        var proposal = CreateProposal();
        var firstConsent = RepairConsent.Confirm(proposal);
        var now = DateTimeOffset.UtcNow;

        RepairHistoryRecord Attempt(Guid id, RepairProposal plan, RepairConsent consent) => new(
            id, plan.Id, plan.Title, RepairExecutionStatus.Prepared, plan.Risk, true, false, now, now, "prepared")
        {
            Action = RepairAction.Execute,
            ConsentId = consent.ConsentId,
            PlanFingerprint = consent.PlanFingerprint,
            DiagnosticRunId = plan.DiagnosticRunId,
            EvidenceGeneration = plan.EvidenceGeneration,
            FindingIdentity = plan.FindingIdentity,
            RuleId = plan.RuleId,
            RuleVersion = plan.RuleVersion,
            EvidenceFingerprint = plan.EvidenceFingerprint,
            RedactedEvidence = plan.RedactedEvidence,
            StructuredPostconditions = plan.StructuredPostconditions,
            PostconditionResults = [new RepairConditionResult(
                RepairConditionKind.SimulationCompletedWithoutSystemChanges, RepairConditionStatus.Verified)],
            PostconditionStatus = RepairPostconditionStatus.Verified
        };

        await using (var firstContext = new WindowsDoctorDbContext(options))
        {
            await firstContext.Database.EnsureCreatedAsync();
            await WindowsDoctorDatabaseMigrator.MigrateAsync(firstContext);
            var firstAudit = new SqliteRepairAuditLog(firstContext);
            var prepared = Attempt(Guid.NewGuid(), proposal, firstConsent);
            Assert.Equal(RepairConsentAttemptStatus.Saved,
                (await firstAudit.TrySaveConsentAttemptAsync(prepared)).Status);
            Assert.True(await firstAudit.TryMarkStartedAsync(prepared with
            {
                Status = RepairExecutionStatus.Started,
                ExecutionStarted = true,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Details = "started"
            }));
        }

        await using var restartedContext = new WindowsDoctorDbContext(options);
        var restartedAudit = new SqliteRepairAuditLog(restartedContext);
        var secondConsent = RepairConsent.Confirm(proposal);
        var blocked = await restartedAudit.TrySaveConsentAttemptAsync(Attempt(Guid.NewGuid(), proposal, secondConsent));

        Assert.Equal(RepairConsentAttemptStatus.Quarantined, blocked.Status);
        Assert.Equal(RepairExecutionStatus.Declined, blocked.Record!.Status);
        Assert.Equal(1, await restartedContext.RepairStartedPlanQuarantines.CountAsync());

        var newDiagnostic = proposal with { DiagnosticRunId = Guid.NewGuid() };
        var freshConsent = RepairConsent.Confirm(newDiagnostic);
        var fresh = await restartedAudit.TrySaveConsentAttemptAsync(Attempt(Guid.NewGuid(), newDiagnostic, freshConsent));
        Assert.Equal(RepairConsentAttemptStatus.Saved, fresh.Status);
    }

    private static RepairProposal CreateProposal(bool supportsRollback = false) => new(
        "fixture.safe-repair",
        "Fixture inerte",
        "Somente contrato; nenhuma operação no Windows.",
        RepairRiskLevel.Low,
        "Nenhum impacto real.",
        RequiresExplicitApproval: true,
        SupportsRollback: supportsRollback,
        PlanVersion: 3,
        Target: "alvo fake determinístico",
        Preconditions:
        [
            "A execução diagnóstica vinculada ainda é a atual.",
            "O achado identificado ainda existe na execução vinculada.",
            "A mesma versão da regra continua vigente.",
            "A evidência redigida continua correspondendo à regra vinculada.",
            "O tipo de plano e a regra estão na allowlist compilada."
        ],
        Postconditions: ["A simulação terminou sem alterar o sistema."],
        RollbackPreconditions:
        [
            "A execução diagnóstica vinculada ainda é a atual.",
            "O achado identificado ainda existe na execução vinculada.",
            "A mesma versão da regra continua vigente.",
            "A evidência redigida continua correspondendo à regra vinculada.",
            "O tipo de plano e a regra estão na allowlist compilada.",
            "A execução original vinculada foi verificada como concluída."
        ],
        RollbackPostconditions: ["A simulação terminou sem alterar o sistema."])
    {
        Kind = RepairProposalKind.NoOpSimulation,
        OperationVersion = 2,
        DiagnosticRunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        FindingIdentity = new string('a', 64),
        RuleId = "fixture.safe-rule",
        RuleVersion = 7,
        EvidenceFingerprint = FingerprintEvidence("Evidência fake redigida."),
        RedactedEvidence = "Evidência fake redigida.",
        StructuredPreconditions =
        [
            new(RepairConditionKind.DiagnosticRunIsCurrent),
            new(RepairConditionKind.FindingIsPresent),
            new(RepairConditionKind.RuleVersionIsCurrent),
            new(RepairConditionKind.FindingMatchesRule),
            new(RepairConditionKind.RuleIsAllowlisted)
        ],
        StructuredPostconditions = [new(RepairConditionKind.SimulationCompletedWithoutSystemChanges)],
        StructuredRollbackPreconditions =
        [
            new(RepairConditionKind.DiagnosticRunIsCurrent),
            new(RepairConditionKind.FindingIsPresent),
            new(RepairConditionKind.RuleVersionIsCurrent),
            new(RepairConditionKind.FindingMatchesRule),
            new(RepairConditionKind.RuleIsAllowlisted),
            new(RepairConditionKind.OriginalExecutionSucceeded)
        ],
        StructuredRollbackPostconditions = [new(RepairConditionKind.SimulationCompletedWithoutSystemChanges)]
    };

    private static string FingerprintEvidence(string evidence) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))).ToLowerInvariant();

    private static RepairEngine CreateEngine(
        IRepairPlugin plugin,
        IRepairAuditLog audit,
        IRepairPreconditionEvaluator? evaluator = null,
        IRepairEvidenceGate? evidenceGate = null)
    {
        var gate = evidenceGate ?? new RepairEvidenceGate();
        if (evidenceGate is null)
        {
            var lease = gate.AcquireAsync().AsTask().GetAwaiter().GetResult();
            lease.MarkDiagnosticRunCurrent(plugin.Proposal.DiagnosticRunId, plugin.Proposal.EvidenceGeneration);
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        return new RepairEngine([plugin], audit, evaluator ?? new VerifiedPreconditionEvaluator(),
            new FixtureRepairProposalAllowlist(plugin.Proposal), gate);
    }

    private static RepairPostconditionReport VerifiedSimulationPostconditions() =>
        new(RepairPostconditionStatus.Verified, "Pós-condição simulada verificada.")
        {
            Conditions = [new RepairConditionResult(RepairConditionKind.SimulationCompletedWithoutSystemChanges,
                RepairConditionStatus.Verified)]
        };

    private sealed class FixtureRepairProposalAllowlist(RepairProposal proposal) : IRepairProposalAllowlist
    {
        private readonly RepairProposalDefinition Definition = new(
            proposal.RuleId, proposal.RuleVersion, proposal.Kind, proposal.PlanVersion, proposal.OperationVersion,
            proposal.Id, proposal.Title, proposal.Description, proposal.Risk, proposal.Impact, proposal.Target,
            proposal.StructuredPreconditions, proposal.StructuredPostconditions, proposal.SupportsRollback,
            proposal.StructuredRollbackPreconditions, proposal.StructuredRollbackPostconditions);

        public bool TryGetDefinition(string ruleId, int ruleVersion, out RepairProposalDefinition? definition)
        {
            definition = string.Equals(ruleId, Definition.RuleId, StringComparison.Ordinal)
                && ruleVersion == Definition.RuleVersion ? Definition : null;
            return definition is not null;
        }
    }

    private sealed class VerifiedPreconditionEvaluator : IRepairPreconditionEvaluator
    {
        public Task<IReadOnlyList<RepairConditionResult>> EvaluateAsync(
            RepairProposal proposal,
            RepairAction action,
            Guid? relatedRepairExecutionId,
            CancellationToken cancellationToken = default)
        {
            var conditions = action == RepairAction.Rollback
                ? proposal.StructuredRollbackPreconditions
                : proposal.StructuredPreconditions;
            return Task.FromResult<IReadOnlyList<RepairConditionResult>>(conditions.Select(condition =>
                new RepairConditionResult(condition.Kind, RepairConditionStatus.Verified)).ToArray());
        }
    }

    private sealed class StaticPreconditionEvaluator(IReadOnlyList<RepairConditionResult> results) : IRepairPreconditionEvaluator
    {
        public Task<IReadOnlyList<RepairConditionResult>> EvaluateAsync(
            RepairProposal proposal,
            RepairAction action,
            Guid? relatedRepairExecutionId,
            CancellationToken cancellationToken = default) => Task.FromResult(results);
    }

    private sealed class StaleOnSecondEvaluation(IReadOnlyList<RepairPlanCondition> conditions) : IRepairPreconditionEvaluator
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<RepairConditionResult>> EvaluateAsync(
            RepairProposal proposal,
            RepairAction action,
            Guid? relatedRepairExecutionId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyList<RepairConditionResult> results = conditions.Select(condition =>
                new RepairConditionResult(condition.Kind,
                    CallCount == 1 ? RepairConditionStatus.Verified
                        : condition.Kind == RepairConditionKind.FindingMatchesRule
                            ? RepairConditionStatus.Failed : RepairConditionStatus.Verified)).ToArray();
            return Task.FromResult(results);
        }
    }

    private sealed class GateRacingPreconditionEvaluator(
        IRepairEvidenceGate gate,
        InMemoryRepairAuditLog audit,
        IReadOnlyList<RepairPlanCondition> conditions) : IRepairPreconditionEvaluator
    {
        private int _callCount;
        private readonly TaskCompletionSource _mutationScheduled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task MutationTask { get; private set; } = Task.CompletedTask;

        public async Task<IReadOnlyList<RepairConditionResult>> EvaluateAsync(
            RepairProposal proposal,
            RepairAction action,
            Guid? relatedRepairExecutionId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _callCount) == 2)
            {
                MutationTask = Task.Run(async () =>
                {
                    _mutationScheduled.TrySetResult();
                    await using var lease = await gate.AcquireAsync(cancellationToken);
                    Assert.NotEmpty(audit.StartedExecutionIds);
                    lease.AdvanceGeneration();
                }, cancellationToken);
                await _mutationScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return conditions.Select(condition => new RepairConditionResult(
                condition.Kind, RepairConditionStatus.Verified)).ToArray();
        }
    }

    private sealed class InMemoryRepairAuditLog : IRepairAuditLog
    {
        public List<RepairHistoryRecord> Records { get; } = [];
        public HashSet<Guid> StartedExecutionIds { get; } = [];
        public bool RejectCancelledWrites { get; init; }
        public bool FailConsentClaim { get; init; }
        public bool FailStartedWrites { get; init; }
        public bool FailVerifiedTerminalWrites { get; init; }
        private readonly HashSet<Guid> _usedConsentIds = [];
        private readonly Dictionary<string, Guid> _quarantinedFindings = new(StringComparer.Ordinal);

        public Task SaveAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default)
        {
            if (RejectCancelledWrites) cancellationToken.ThrowIfCancellationRequested();
            if (FailStartedWrites && record.Status == RepairExecutionStatus.Started)
                throw new IOException("fixture persistence failure");
            if (FailVerifiedTerminalWrites && record.ExecutionStarted
                && record.Status is RepairExecutionStatus.Succeeded or RepairExecutionStatus.RolledBack)
                throw new IOException("fixture terminal persistence failure");
            Records.RemoveAll(existing => existing.RepairExecutionId == record.RepairExecutionId);
            Records.Add(record);
            return Task.CompletedTask;
        }

        public async Task<RepairConsentAttemptResult> TrySaveConsentAttemptAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default)
        {
            if (FailConsentClaim) throw new IOException("fixture prepared persistence failure");
            if (record.ConsentId is { } consentId && !_usedConsentIds.Add(consentId))
                return new RepairConsentAttemptResult(RepairConsentAttemptStatus.Replay);
            var key = QuarantineKey(record);
            if (key is not null && _quarantinedFindings.ContainsKey(key))
            {
                var blocked = record with
                {
                    Status = RepairExecutionStatus.Declined,
                    ExecutionStarted = false,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Details = "Finding em quarentena: tentativa anterior alcançou Started. Execute novo diagnóstico; nenhuma reconciliação automática foi presumida."
                };
                await SaveAsync(blocked, cancellationToken).ConfigureAwait(false);
                return new RepairConsentAttemptResult(RepairConsentAttemptStatus.Quarantined, blocked);
            }
            await SaveAsync(record, cancellationToken).ConfigureAwait(false);
            return new RepairConsentAttemptResult(RepairConsentAttemptStatus.Saved, record);
        }

        public Task<bool> TryMarkStartedAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default)
        {
            if (FailStartedWrites) throw new IOException("fixture persistence failure");
            var key = QuarantineKey(record);
            if (key is null || _quarantinedFindings.ContainsKey(key)) return Task.FromResult(false);
            _quarantinedFindings.Add(key, record.RepairExecutionId);
            StartedExecutionIds.Add(record.RepairExecutionId);
            Records.RemoveAll(existing => existing.RepairExecutionId == record.RepairExecutionId);
            Records.Add(record);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<RepairHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepairHistoryRecord>>(Records.Take(count).ToArray());

        public Task<RepairHistoryRecord?> GetByExecutionIdAsync(Guid repairExecutionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Records.FirstOrDefault(record => record.RepairExecutionId == repairExecutionId));

        private static string? QuarantineKey(RepairHistoryRecord record) => record.DiagnosticRunId is { } runId
            && !string.IsNullOrWhiteSpace(record.FindingIdentity)
            ? $"{runId:D}:{record.FindingIdentity}:{record.Action}"
            : null;
    }

    private sealed class DeterministicRepairPlugin(RepairProposal proposal) : IRepairPlugin
    {
        public RepairProposal Proposal { get; } = proposal;
        public int ExecuteCount { get; private set; }
        public int VerifyCount { get; private set; }
        public Guid? LastRollbackExecutionId { get; private set; }
        public Func<RepairExecutionContext, CancellationToken, Task<RepairPluginResult>>? Execute { get; init; }
        public Func<Guid, RepairExecutionContext, CancellationToken, Task<RepairPluginResult>>? Rollback { get; init; }
        public RepairPostconditionReport Verification { get; init; } = RepairPostconditionReport.NotEvaluated;

        public Task<string> ExecuteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("legacy path unused by deterministic tests");

        public Task<string> RollbackAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("unqualified legacy rollback unused");

        public async Task<RepairPluginResult> ExecuteAsync(
            RepairExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Execute is null
                ? throw new InvalidOperationException("The test fake has no execution behavior.")
                : await Execute(context, cancellationToken).ConfigureAwait(false);
        }

        public async Task<RepairPluginResult> RollbackAsync(
            Guid repairExecutionId,
            RepairExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            LastRollbackExecutionId = repairExecutionId;
            return Rollback is null
                ? throw new NotSupportedException("The test fake has no rollback behavior.")
                : await Rollback(repairExecutionId, context, cancellationToken).ConfigureAwait(false);
        }

        public Task<RepairPostconditionReport> VerifyPostconditionsAsync(
            RepairAction action,
            Guid repairExecutionId,
            Guid? relatedRepairExecutionId,
            CancellationToken cancellationToken = default)
        {
            VerifyCount++;
            return Task.FromResult(Verification);
        }
    }
}
