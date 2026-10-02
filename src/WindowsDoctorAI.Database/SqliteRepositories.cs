using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Database;

internal static class InventoryJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

internal sealed class RepairHistoryAuditMetadata
{
    public int PlanVersion { get; set; } = 1;
    public string Target { get; set; } = string.Empty;
    public string[] Preconditions { get; set; } = [];
    public string[] Postconditions { get; set; } = [];
    public string[] RollbackPreconditions { get; set; } = [];
    public string[] RollbackPostconditions { get; set; } = [];
    public RepairAction Action { get; set; } = RepairAction.Execute;
    public Guid? RelatedRepairExecutionId { get; set; }
    public Guid? ConsentId { get; set; }
    public DateTimeOffset? ConsentConfirmedAtUtc { get; set; }
    public string PlanFingerprint { get; set; } = string.Empty;
    public bool ExecutionStarted { get; set; }
    public RepairPostconditionStatus PostconditionStatus { get; set; } = RepairPostconditionStatus.NotEvaluated;
    public string PostconditionDetails { get; set; } = string.Empty;
}

public sealed class SqliteDiagnosticRunRepository(WindowsDoctorDbContext dbContext) : IDiagnosticRunRepository
{
    public async Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        var payload = JsonSerializer.Serialize(run, InventoryJson.Options);
        dbContext.DiagnosticRuns.Add(new DiagnosticRunEntity
        {
            Id = run.Id,
            CompletedAtUnixMilliseconds = run.CompletedAtUtc.ToUnixTimeMilliseconds(),
            PayloadJson = payload
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DiagnosticRun?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var latest = await dbContext.DiagnosticRuns.AsNoTracking()
            .OrderByDescending(entity => entity.CompletedAtUnixMilliseconds)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return latest is null ? null : JsonSerializer.Deserialize<DiagnosticRun>(latest.PayloadJson, InventoryJson.Options);
    }
}

public sealed class SqliteUserSettingsRepository(WindowsDoctorDbContext dbContext) : IUserSettingsRepository
{
    public async Task<UserSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.UserSettings.AsNoTracking().SingleOrDefaultAsync(settings => settings.Id == 1, cancellationToken).ConfigureAwait(false);
        return entity is null ? new UserSettings() : new UserSettings { SaveDiagnosticHistory = entity.SaveDiagnosticHistory };
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var entity = await dbContext.UserSettings.SingleOrDefaultAsync(value => value.Id == 1, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new UserSettingsEntity { Id = 1 };
            dbContext.UserSettings.Add(entity);
        }
        entity.SaveDiagnosticHistory = settings.SaveDiagnosticHistory;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class SqliteKnowledgeRepository(WindowsDoctorDbContext dbContext) : IKnowledgeRepository
{
    public async Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.KnowledgeRules.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities
            .GroupBy(entity => entity.RuleId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entity => entity.RuleVersion).First())
            .Select(entity => JsonSerializer.Deserialize<KnowledgeRule>(entity.PayloadJson, InventoryJson.Options))
            .Where(rule => rule is not null)
            .Cast<KnowledgeRule>()
            .OrderBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var addedRules = new List<KnowledgeRuleEntity>();
        KnowledgeBaseVersionEntity? addedPackage = null;
        try
        {
            var existingPackage = await dbContext.KnowledgeBaseVersions.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Version == package.Version, cancellationToken).ConfigureAwait(false);
            if (existingPackage is not null)
            {
                if (!string.Equals(existingPackage.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"A versão de base '{package.Version}' já existe com conteúdo diferente.");
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var rule in package.Rules)
            {
                var payload = JsonSerializer.Serialize(rule, InventoryJson.Options);
                var previous = await dbContext.KnowledgeRules.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.RuleId == rule.Id && item.RuleVersion == rule.Version, cancellationToken)
                    .ConfigureAwait(false);
                if (previous is not null)
                {
                    if (!string.Equals(previous.PayloadJson, payload, StringComparison.Ordinal))
                        throw new InvalidOperationException($"A regra {rule.Id} v{rule.Version} já existe com conteúdo diferente; incremente a versão da regra.");
                    continue;
                }
                addedRules.Add(new KnowledgeRuleEntity
                {
                    RuleId = rule.Id,
                    RuleVersion = rule.Version,
                    PackageVersion = package.Version,
                    PayloadJson = payload,
                    ImportedAtUnixMilliseconds = now
                });
            }

            dbContext.KnowledgeRules.AddRange(addedRules);
            addedPackage = new KnowledgeBaseVersionEntity
            {
                Version = package.Version,
                Source = package.Source,
                Sha256 = sha256,
                RuleCount = package.Rules.Count,
                ImportedAtUnixMilliseconds = now
            };
            dbContext.KnowledgeBaseVersions.Add(addedPackage);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* Preserve the original validation/persistence exception. */ }
            foreach (var rule in addedRules) dbContext.Entry(rule).State = EntityState.Detached;
            if (addedPackage is not null) dbContext.Entry(addedPackage).State = EntityState.Detached;
            throw;
        }
    }
}

public sealed class SqliteRepairAuditLog(WindowsDoctorDbContext dbContext) : IRepairAuditLog
{
    public async Task SaveAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var entity = await dbContext.RepairHistory
            .SingleOrDefaultAsync(item => item.Id == record.RepairExecutionId, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            entity = new RepairHistoryEntity { Id = record.RepairExecutionId };
            dbContext.RepairHistory.Add(entity);
        }

        entity.ProposalId = record.RepairId;
        entity.Title = record.Title;
        entity.Status = record.Status;
        entity.Risk = record.Risk;
        entity.UserConfirmed = record.UserConfirmed;
        entity.RollbackSupported = record.RollbackSupported;
        entity.StartedAtUnixMilliseconds = record.StartedAtUtc.ToUnixTimeMilliseconds();
        entity.CompletedAtUnixMilliseconds = record.CompletedAtUtc.ToUnixTimeMilliseconds();
        entity.Details = record.Details.Length <= 2000 ? record.Details : record.Details[..2000];
        entity.AuditMetadataJson = JsonSerializer.Serialize(new RepairHistoryAuditMetadata
        {
            PlanVersion = record.PlanVersion,
            Target = record.Target,
            Preconditions = record.Preconditions.ToArray(),
            Postconditions = record.Postconditions.ToArray(),
            RollbackPreconditions = record.RollbackPreconditions.ToArray(),
            RollbackPostconditions = record.RollbackPostconditions.ToArray(),
            Action = record.Action,
            RelatedRepairExecutionId = record.RelatedRepairExecutionId,
            ConsentId = record.ConsentId,
            ConsentConfirmedAtUtc = record.ConsentConfirmedAtUtc,
            PlanFingerprint = record.PlanFingerprint,
            ExecutionStarted = record.ExecutionStarted,
            PostconditionStatus = record.PostconditionStatus,
            PostconditionDetails = record.PostconditionDetails
        }, InventoryJson.Options);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RepairHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        var boundedCount = Math.Clamp(count, 1, 500);
        var entities = await dbContext.RepairHistory.AsNoTracking()
            .OrderByDescending(item => item.CompletedAtUnixMilliseconds)
            .Take(boundedCount)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(ToRecord).ToArray();
    }

    public async Task<RepairHistoryRecord?> GetByExecutionIdAsync(
        Guid repairExecutionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.RepairHistory.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == repairExecutionId, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToRecord(entity);
    }

    private static RepairHistoryRecord ToRecord(RepairHistoryEntity entity)
    {
        var metadata = string.IsNullOrWhiteSpace(entity.AuditMetadataJson)
            ? new RepairHistoryAuditMetadata()
            : JsonSerializer.Deserialize<RepairHistoryAuditMetadata>(entity.AuditMetadataJson, InventoryJson.Options)
                ?? new RepairHistoryAuditMetadata();
        return new RepairHistoryRecord(
            entity.Id,
            entity.ProposalId,
            entity.Title,
            entity.Status,
            entity.Risk,
            entity.UserConfirmed,
            entity.RollbackSupported,
            DateTimeOffset.FromUnixTimeMilliseconds(entity.StartedAtUnixMilliseconds),
            DateTimeOffset.FromUnixTimeMilliseconds(entity.CompletedAtUnixMilliseconds),
            entity.Details)
        {
            PlanVersion = metadata.PlanVersion,
            Target = metadata.Target,
            Preconditions = metadata.Preconditions,
            Postconditions = metadata.Postconditions,
            RollbackPreconditions = metadata.RollbackPreconditions,
            RollbackPostconditions = metadata.RollbackPostconditions,
            Action = metadata.Action,
            RelatedRepairExecutionId = metadata.RelatedRepairExecutionId,
            ConsentId = metadata.ConsentId,
            ConsentConfirmedAtUtc = metadata.ConsentConfirmedAtUtc,
            PlanFingerprint = metadata.PlanFingerprint,
            ExecutionStarted = metadata.ExecutionStarted,
            PostconditionStatus = metadata.PostconditionStatus,
            PostconditionDetails = metadata.PostconditionDetails
        };
    }
}

public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsDoctorDatabase(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        services.AddDbContext<WindowsDoctorDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IDiagnosticRunRepository, SqliteDiagnosticRunRepository>();
        services.AddScoped<IUserSettingsRepository, SqliteUserSettingsRepository>();
        services.AddScoped<IKnowledgeRepository, SqliteKnowledgeRepository>();
        services.AddScoped<IRepairAuditLog, SqliteRepairAuditLog>();
        return services;
    }

    public static async Task InitializeWindowsDoctorDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WindowsDoctorDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await WindowsDoctorDatabaseMigrator.MigrateAsync(context, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Schema local evolui em passos idempotentes; PRAGMA user_version identifica o último passo concluído.</summary>
public static class WindowsDoctorDatabaseMigrator
{
    public const int CurrentVersion = 2;

    public static async Task MigrateAsync(WindowsDoctorDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (version > CurrentVersion)
                throw new InvalidOperationException($"O banco SQLite está na versão {version}, superior à versão suportada {CurrentVersion}.");
            if (version == CurrentVersion) return;

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "KnowledgeRules" (
                    "RuleId" TEXT NOT NULL,
                    "RuleVersion" INTEGER NOT NULL,
                    "PackageVersion" TEXT NOT NULL,
                    "PayloadJson" TEXT NOT NULL,
                    "ImportedAtUnixMilliseconds" INTEGER NOT NULL,
                    CONSTRAINT "PK_KnowledgeRules" PRIMARY KEY ("RuleId", "RuleVersion"));
                CREATE INDEX IF NOT EXISTS "IX_KnowledgeRules_RuleId_RuleVersion" ON "KnowledgeRules" ("RuleId", "RuleVersion");
                CREATE TABLE IF NOT EXISTS "KnowledgeBaseVersions" (
                    "Version" TEXT NOT NULL CONSTRAINT "PK_KnowledgeBaseVersions" PRIMARY KEY,
                    "Source" TEXT NOT NULL,
                    "Sha256" TEXT NOT NULL,
                    "RuleCount" INTEGER NOT NULL,
                    "ImportedAtUnixMilliseconds" INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS "RepairHistory" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_RepairHistory" PRIMARY KEY,
                    "ProposalId" TEXT NOT NULL,
                    "Title" TEXT NOT NULL,
                    "Status" INTEGER NOT NULL,
                    "Risk" INTEGER NOT NULL,
                    "UserConfirmed" INTEGER NOT NULL,
                    "RollbackSupported" INTEGER NOT NULL,
                    "StartedAtUnixMilliseconds" INTEGER NOT NULL,
                    "CompletedAtUnixMilliseconds" INTEGER NOT NULL,
                    "Details" TEXT NOT NULL,
                    "AuditMetadataJson" TEXT NOT NULL DEFAULT '{{}}');
                CREATE INDEX IF NOT EXISTS "IX_RepairHistory_CompletedAtUnixMilliseconds" ON "RepairHistory" ("CompletedAtUnixMilliseconds");
                """, cancellationToken).ConfigureAwait(false);

            if (!await HasRepairHistoryMetadataColumnAsync(context, cancellationToken).ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE "RepairHistory" ADD COLUMN "AuditMetadataJson" TEXT NOT NULL DEFAULT '{{}}';
                    """, cancellationToken).ConfigureAwait(false);
            }

            await context.Database.ExecuteSqlRawAsync($"PRAGMA user_version = {CurrentVersion};", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static async Task<bool> HasRepairHistoryMetadataColumnAsync(
        WindowsDoctorDbContext context,
        CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info(\"RepairHistory\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), "AuditMetadataJson", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
