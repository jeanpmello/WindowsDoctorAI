using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        var engine = new RepairEngine([plugin], audit);

        var result = await engine.ExecuteAsync(plugin.Proposal.Id, consent: null);

        Assert.Equal(RepairExecutionStatus.Declined, result.Status);
        Assert.NotEqual(Guid.Empty, result.RepairExecutionId);
        Assert.False(result.ExecutionStarted);
        Assert.Equal(0, plugin.ExecuteCount);
        Assert.Same(result, Assert.Single(audit.Records));
    }

    [Fact]
    public async Task SuccessPersistsActionBoundConsentAndExplicitUnknownVerification()
    {
        var proposal = CreateProposal();
        var audit = new InMemoryRepairAuditLog();
        var plugin = new DeterministicRepairPlugin(proposal)
        {
            Execute = async (context, cancellationToken) =>
            {
                Assert.Equal(RepairExecutionStatus.Prepared, Assert.Single(audit.Records).Status);
                await context.MarkStartedAsync(cancellationToken);
                return new RepairPluginResult("fake determinístico concluído");
            }
        };
        var engine = new RepairEngine([plugin], audit);
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
        Assert.Equal(RepairPostconditionStatus.NotEvaluated, result.PostconditionStatus);
        Assert.Equal(RepairAction.Execute, result.Action);
        Assert.Equal(1, plugin.VerifyCount);
        Assert.Contains("não avaliadas", result.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Same(result, Assert.Single(audit.Records));
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
        var engine = new RepairEngine([plugin], audit);

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
        var engine = new RepairEngine([plugin], audit);

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
        var engine = new RepairEngine([plugin], audit);

        var result = await engine.ExecuteAsync(plugin.Proposal.Id, RepairConsent.Confirm(plugin.Proposal));

        Assert.Equal(RepairExecutionStatus.Failed, result.Status);
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
        var engine = new RepairEngine([plugin], audit);

        var result = await engine.ExecuteAsync(plugin.Proposal.Id,
            RepairConsent.Confirm(plugin.Proposal), cancellation.Token);

        Assert.Equal(RepairExecutionStatus.Cancelled, result.Status);
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
        var engine = new RepairEngine([plugin], audit);
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
        var engine = new RepairEngine([plugin], audit);
        var executed = await engine.ExecuteAsync(proposal.Id, RepairConsent.Confirm(proposal));

        var result = await engine.RollbackAsync(executed.RepairExecutionId,
            RepairConsent.ConfirmRollback(proposal, executed.RepairExecutionId));

        Assert.Equal(RepairExecutionStatus.RollbackFailed, result.Status);
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
        var engine = new RepairEngine([plugin], audit);

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

        Assert.Equal(3, WindowsDoctorDatabaseMigrator.CurrentVersion);
        Assert.Equal("legacy.item", loaded.RepairId);
        Assert.Equal("legacy record", loaded.Details);
        Assert.Equal(1, loaded.PlanVersion);
        Assert.Equal(RepairPostconditionStatus.NotEvaluated, loaded.PostconditionStatus);
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
            ConsentConfirmedAtUtc = consent.ConfirmedAtUtc
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
        Assert.Equal(proposal.Preconditions, loaded.Preconditions);
        Assert.Equal("started", loaded.Details);
        Assert.Single(await audit.GetRecentAsync(10));

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
        Preconditions: ["precondição declarativa de fixture"],
        Postconditions: ["pós-condição declarativa de fixture"],
        RollbackPreconditions: ["rollback de execução fake específica"],
        RollbackPostconditions: ["pós-condição declarativa do fake"]);

    private sealed class InMemoryRepairAuditLog : IRepairAuditLog
    {
        public List<RepairHistoryRecord> Records { get; } = [];
        public bool RejectCancelledWrites { get; init; }

        public Task SaveAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default)
        {
            if (RejectCancelledWrites) cancellationToken.ThrowIfCancellationRequested();
            Records.RemoveAll(existing => existing.RepairExecutionId == record.RepairExecutionId);
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RepairHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepairHistoryRecord>>(Records.Take(count).ToArray());

        public Task<RepairHistoryRecord?> GetByExecutionIdAsync(Guid repairExecutionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Records.FirstOrDefault(record => record.RepairExecutionId == repairExecutionId));
    }

    private sealed class DeterministicRepairPlugin(RepairProposal proposal) : IRepairPlugin
    {
        public RepairProposal Proposal { get; } = proposal;
        public int ExecuteCount { get; private set; }
        public int VerifyCount { get; private set; }
        public Guid? LastRollbackExecutionId { get; private set; }
        public Func<RepairExecutionContext, CancellationToken, Task<RepairPluginResult>>? Execute { get; init; }
        public Func<Guid, RepairExecutionContext, CancellationToken, Task<RepairPluginResult>>? Rollback { get; init; }

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
            return Task.FromResult(RepairPostconditionReport.NotEvaluated);
        }
    }
}
