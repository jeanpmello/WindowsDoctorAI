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
        return services;
    }

    public static async Task InitializeWindowsDoctorDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WindowsDoctorDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }
}
