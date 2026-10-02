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

/// <summary>Contexto SQLite local. Só armazena execuções quando o usuário mantém o histórico habilitado.</summary>
public sealed class WindowsDoctorDbContext(DbContextOptions<WindowsDoctorDbContext> options) : DbContext(options)
{
    public DbSet<DiagnosticRunEntity> DiagnosticRuns => Set<DiagnosticRunEntity>();
    public DbSet<UserSettingsEntity> UserSettings => Set<UserSettingsEntity>();

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
    }
}
