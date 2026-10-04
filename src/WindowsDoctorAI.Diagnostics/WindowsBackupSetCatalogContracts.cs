namespace WindowsDoctorAI.Diagnostics;

/// <summary>Superfície independente e allowlistada para uma consulta transitória do catálogo WSB.</summary>
public interface IWindowsBackupSetCatalogCommandRunner
{
    Task<WindowsBackupCommandRunResult> GetBackupSetCatalogAsync(CancellationToken cancellationToken = default);
}
