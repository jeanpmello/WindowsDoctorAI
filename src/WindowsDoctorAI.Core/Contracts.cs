using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Core;

/// <summary>Porta do caso de uso para obter um snapshot do dispositivo atual.</summary>
public interface IComputerInventoryScanner
{
    Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>Abstrai APIs locais de inventário para permitir testes sem WMI ou hardware real.</summary>
public interface IComputerInventoryDataSource
{
    Task<ComputerInventory> CollectAsync(CancellationToken cancellationToken = default);
}

public interface IDiagnosticRunRepository
{
    Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken = default);
    Task<DiagnosticRun?> GetLatestAsync(CancellationToken cancellationToken = default);
}

public interface IUserSettingsRepository
{
    Task<UserSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);
}
