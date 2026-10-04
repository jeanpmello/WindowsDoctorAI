using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Core;

/// <summary>Consulta sob demanda e somente de leitura do catálogo temporário de versões WSB.</summary>
public interface IBackupSetCatalogSource
{
    Task<BackupSetCatalogResult> GetCatalogAsync(CancellationToken cancellationToken = default);
}
