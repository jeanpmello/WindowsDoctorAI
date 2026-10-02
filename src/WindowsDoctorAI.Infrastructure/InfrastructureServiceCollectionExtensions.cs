using Microsoft.Extensions.DependencyInjection;
using WindowsDoctorAI.Database;

namespace WindowsDoctorAI.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registra os adaptadores locais e o banco SQLite no contêiner da aplicação.</summary>
    public static IServiceCollection AddWindowsDoctorInfrastructure(this IServiceCollection services, string databasePath) =>
        services.AddWindowsDoctorDatabase(databasePath);
}
