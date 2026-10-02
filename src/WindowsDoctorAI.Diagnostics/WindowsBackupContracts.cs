namespace WindowsDoctorAI.Diagnostics;

/// <summary>Estado de execução do único comando de consulta permitido para Windows Server Backup.</summary>
public enum WindowsBackupCommandRunStatus
{
    Completed,
    TimedOut,
    Failed,
    InvalidResponse
}

/// <summary>Saída temporária do processo; StandardOutput é consumido e descartado após parsing local.</summary>
public sealed record WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus Status, string? StandardOutput = null);

/// <summary>Superfície allowlist: a implementação só pode enumerar metadados com Get-WBBackupSet.</summary>
public interface IWindowsBackupCommandRunner
{
    Task<WindowsBackupCommandRunResult> GetBackupSetMetadataAsync(CancellationToken cancellationToken = default);
}

public enum WindowsBackupProbeStatus
{
    Available,
    NoBackupSets,
    ModuleUnavailable,
    AccessDenied,
    InvalidResponse,
    TimedOut,
    QueryFailed
}

/// <summary>Tipo minimizado; nunca preserva nomes arbitrários fornecidos pelo sistema.</summary>
public enum WindowsBackupType
{
    Full,
    Incremental,
    Differential,
    Other
}

/// <summary>Metadados minimizados da consulta. Não contém host, destino ou itens de backup.</summary>
public sealed record WindowsBackupProbe(
    WindowsBackupProbeStatus Status,
    int? BackupSetCount = null,
    DateTimeOffset? LatestBackupTimeUtc = null,
    WindowsBackupType? BackupType = null);

/// <summary>Fonte substituível que devolve apenas estados e metadados minimizados.</summary>
public interface IWindowsBackupDataSource
{
    Task<WindowsBackupProbe> ReadBackupSetsAsync(CancellationToken cancellationToken = default);
}
