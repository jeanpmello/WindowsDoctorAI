using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Implementação do scanner de inventário; delega APIs específicas do sistema a uma fonte substituível.</summary>
public sealed class ComputerInventoryScanner(IComputerInventoryDataSource dataSource) : IComputerInventoryScanner
{
    public Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        cancellationToken.ThrowIfCancellationRequested();
        return dataSource.CollectAsync(cancellationToken);
    }
}
