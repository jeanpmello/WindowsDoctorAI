using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

public static class DiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddComputerInventoryDiagnostics(this IServiceCollection services)
    {
        services.AddSingleton<IComputerInventoryScanner, ComputerInventoryScanner>();
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IComputerInventoryDataSource, WindowsManagementInventoryDataSource>();
        }
        else
        {
            services.AddSingleton<IComputerInventoryDataSource, UnsupportedInventoryDataSource>();
        }
        return services;
    }

    private sealed class UnsupportedInventoryDataSource : IComputerInventoryDataSource
    {
        public Task<ComputerInventory> CollectAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ComputerInventory>(new PlatformNotSupportedException("O inventário do Windows só pode ser coletado em Windows."));
    }
}
