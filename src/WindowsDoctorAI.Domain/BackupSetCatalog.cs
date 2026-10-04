namespace WindowsDoctorAI.Domain;

/// <summary>Limites conservadores compartilhados para manter a consulta e a lista bounded.</summary>
public static class BackupSetCatalogLimits
{
    public const int MaximumBackupSets = 64;
    public const int MaximumVolumeCount = 128;
    public const int MaximumVersionIdCharacters = 128;
    public const int MaximumJsonCharacters = 64 * 1024;
}

/// <summary>Tipo de backup normalizado; nunca preserva o texto bruto fornecido pelo sistema.</summary>
public enum BackupSetType
{
    Full,
    Incremental,
    Differential,
    Other
}

/// <summary>Resultado da consulta temporária ao catálogo local de Windows Server Backup.</summary>
public enum BackupSetCatalogStatus
{
    Available,
    NoBackupSets,
    Unavailable,
    ModuleUnavailable,
    AccessDenied,
    InvalidResponse,
    TooManyBackupSets,
    TimedOut
}

/// <summary>Metadados mínimos de uma versão selecionável; não contém nomes, caminhos, host ou itens.</summary>
public sealed record BackupSetCatalogEntry(
    string VersionId,
    DateTimeOffset BackupTimeUtc,
    BackupSetType BackupType,
    int? VolumeCount = null);

/// <summary>Snapshot em memória de curta duração; não deve ser persistido nem exportado.</summary>
public sealed record BackupSetCatalogResult(
    BackupSetCatalogStatus Status,
    IReadOnlyList<BackupSetCatalogEntry> BackupSets);
