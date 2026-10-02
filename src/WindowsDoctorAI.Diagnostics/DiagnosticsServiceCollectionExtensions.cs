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

        services.AddWindowsDiagnosticPlugins();
        return services;
    }

    /// <summary>Registra cada scanner como plugin. Plugins futuros podem expor registro equivalente em seu próprio assembly.</summary>
    public static IServiceCollection AddWindowsDiagnosticPlugins(this IServiceCollection services)
    {
        services.AddSingleton<IWindowsDiagnosticDataSource>(provider => OperatingSystem.IsWindows()
            ? ActivatorUtilities.CreateInstance<WindowsDiagnosticDataSource>(provider)
            : new UnsupportedWindowsDiagnosticDataSource());
        services.AddSingleton<IDiagnosticScanner, WindowsUpdateDiagnosticScanner>();
        services.AddSingleton<IDiagnosticScanner, ServicesDiagnosticScanner>();
        services.AddSingleton<IDiagnosticScanner, DriversDiagnosticScanner>();
        services.AddSingleton<IDiagnosticScanner, DiskDiagnosticScanner>();
        services.AddSingleton<IDiagnosticScanner, EventViewerDiagnosticScanner>();
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IWindowsBackupCommandRunner, WindowsPowerShellBackupCommandRunner>();
            services.AddSingleton<IWindowsBackupDataSource, WindowsBackupDataSource>();
        }
        else
        {
            services.AddSingleton<IWindowsBackupDataSource, UnsupportedWindowsBackupDataSource>();
        }

        services.AddSingleton<IDiagnosticScanner, WindowsBackupDiagnosticScanner>();
        return services;
    }

    private sealed class UnsupportedInventoryDataSource : IComputerInventoryDataSource
    {
        public Task<ComputerInventory> CollectAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ComputerInventory>(new PlatformNotSupportedException("O inventário do Windows só pode ser coletado em Windows."));
    }
}
