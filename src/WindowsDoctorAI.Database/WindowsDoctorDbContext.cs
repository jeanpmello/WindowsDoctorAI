using Microsoft.EntityFrameworkCore;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Database;

public sealed class DiagnosticRunEntity
{
    public Guid Id { get; set; }
    public long CompletedAtUnixMilliseconds { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class UserSettingsEntity
{
    public int Id { get; set; }
    public bool SaveDiagnosticHistory { get; set; }
}

public sealed class KnowledgeRuleEntity
{
    public string RuleId { get; set; } = string.Empty;
    public int RuleVersion { get; set; }
    public string PackageVersion { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public long ImportedAtUnixMilliseconds { get; set; }
}

public sealed class KnowledgeBaseVersionEntity
{
    public string Version { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public int RuleCount { get; set; }
    public long ImportedAtUnixMilliseconds { get; set; }
}

public sealed class RepairHistoryEntity
{
    public Guid Id { get; set; }
    public string ProposalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public RepairExecutionStatus Status { get; set; }
    public RepairRiskLevel Risk { get; set; }
    public bool UserConfirmed { get; set; }
    public bool RollbackSupported { get; set; }
    public long StartedAtUnixMilliseconds { get; set; }
    public long CompletedAtUnixMilliseconds { get; set; }
    public string Details { get; set; } = string.Empty;
}

/// <summary>Contexto SQLite local. O inicializador aplica migrações incrementais identificadas por PRAGMA user_version.</summary>
public sealed class WindowsDoctorDbContext(DbContextOptions<WindowsDoctorDbContext> options) : DbContext(options)
{
    public DbSet<DiagnosticRunEntity> DiagnosticRuns => Set<DiagnosticRunEntity>();
    public DbSet<UserSettingsEntity> UserSettings => Set<UserSettingsEntity>();
    public DbSet<KnowledgeRuleEntity> KnowledgeRules => Set<KnowledgeRuleEntity>();
    public DbSet<KnowledgeBaseVersionEntity> KnowledgeBaseVersions => Set<KnowledgeBaseVersionEntity>();
    public DbSet<RepairHistoryEntity> RepairHistory => Set<RepairHistoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DiagnosticRunEntity>(entity =>
        {
            entity.ToTable("DiagnosticRuns");
            entity.HasKey(run => run.Id);
            entity.HasIndex(run => run.CompletedAtUnixMilliseconds);
            entity.Property(run => run.PayloadJson).IsRequired();
        });
        modelBuilder.Entity<UserSettingsEntity>(entity =>
        {
            entity.ToTable("UserSettings");
            entity.HasKey(settings => settings.Id);
            entity.Property(settings => settings.Id).ValueGeneratedNever();
            entity.HasData(new UserSettingsEntity { Id = 1, SaveDiagnosticHistory = true });
        });
        modelBuilder.Entity<KnowledgeRuleEntity>(entity =>
        {
            entity.ToTable("KnowledgeRules");
            entity.HasKey(rule => new { rule.RuleId, rule.RuleVersion });
            entity.HasIndex(rule => new { rule.RuleId, rule.RuleVersion });
            entity.Property(rule => rule.PackageVersion).IsRequired();
            entity.Property(rule => rule.PayloadJson).IsRequired();
        });
        modelBuilder.Entity<KnowledgeBaseVersionEntity>(entity =>
        {
            entity.ToTable("KnowledgeBaseVersions");
            entity.HasKey(version => version.Version);
            entity.Property(version => version.Source).IsRequired();
            entity.Property(version => version.Sha256).IsRequired();
        });
        modelBuilder.Entity<RepairHistoryEntity>(entity =>
        {
            entity.ToTable("RepairHistory");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.CompletedAtUnixMilliseconds);
            entity.Property(item => item.Details).IsRequired();
        });
    }
}
