using System.Globalization;
using System.Text.Json;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Valida e minimiza a saída temporária do runner; nenhum campo fora do schema allowlist é retido.</summary>
public sealed class WindowsBackupSetCatalogDataSource(IWindowsBackupSetCatalogCommandRunner commandRunner) : IBackupSetCatalogSource
{
    public const int MaximumJsonCharacters = BackupSetCatalogLimits.MaximumJsonCharacters;
    public const int MaximumBackupSets = BackupSetCatalogLimits.MaximumBackupSets;
    public const int MaximumVolumeCount = BackupSetCatalogLimits.MaximumVolumeCount;
    public const int MaximumVersionIdCharacters = BackupSetCatalogLimits.MaximumVersionIdCharacters;

    public async Task<BackupSetCatalogResult> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandRunner);
        cancellationToken.ThrowIfCancellationRequested();

        WindowsBackupCommandRunResult commandResult;
        try
        {
            commandResult = await commandRunner.GetBackupSetCatalogAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Result(BackupSetCatalogStatus.Unavailable);
        }

        if (commandResult is null)
            return Result(BackupSetCatalogStatus.InvalidResponse);

        return commandResult.Status switch
        {
            WindowsBackupCommandRunStatus.Completed => ParseResponse(commandResult.StandardOutput),
            WindowsBackupCommandRunStatus.TimedOut => Result(BackupSetCatalogStatus.TimedOut),
            WindowsBackupCommandRunStatus.Failed => Result(BackupSetCatalogStatus.Unavailable),
            WindowsBackupCommandRunStatus.InvalidResponse => Result(BackupSetCatalogStatus.InvalidResponse),
            _ => Result(BackupSetCatalogStatus.InvalidResponse)
        };
    }

    private static BackupSetCatalogResult ParseResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumJsonCharacters)
            return Result(BackupSetCatalogStatus.InvalidResponse);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 6 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasOnlyProperties(root, "status", "backupSets") ||
                !root.TryGetProperty("status", out var statusElement) ||
                statusElement.ValueKind != JsonValueKind.String)
            {
                return Result(BackupSetCatalogStatus.InvalidResponse);
            }

            var status = statusElement.GetString();
            if (status == "module_unavailable")
                return HasOnlyProperties(root, "status") ? Result(BackupSetCatalogStatus.ModuleUnavailable) : Result(BackupSetCatalogStatus.InvalidResponse);
            if (status == "access_denied")
                return HasOnlyProperties(root, "status") ? Result(BackupSetCatalogStatus.AccessDenied) : Result(BackupSetCatalogStatus.InvalidResponse);
            if (status == "unavailable" || status == "query_failed")
                return HasOnlyProperties(root, "status") ? Result(BackupSetCatalogStatus.Unavailable) : Result(BackupSetCatalogStatus.InvalidResponse);
            if (status == "too_many")
                return HasOnlyProperties(root, "status") ? Result(BackupSetCatalogStatus.TooManyBackupSets) : Result(BackupSetCatalogStatus.InvalidResponse);
            if (status is not ("available" or "no_backup") ||
                !root.TryGetProperty("backupSets", out var setsElement) ||
                setsElement.ValueKind != JsonValueKind.Array)
            {
                return Result(BackupSetCatalogStatus.InvalidResponse);
            }

            if (setsElement.GetArrayLength() > MaximumBackupSets)
                return Result(BackupSetCatalogStatus.TooManyBackupSets);
            if (status == "no_backup")
                return setsElement.GetArrayLength() == 0
                    ? Result(BackupSetCatalogStatus.NoBackupSets)
                    : Result(BackupSetCatalogStatus.InvalidResponse);
            if (setsElement.GetArrayLength() == 0)
                return Result(BackupSetCatalogStatus.InvalidResponse);

            var entries = new List<BackupSetCatalogEntry>(setsElement.GetArrayLength());
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in setsElement.EnumerateArray())
            {
                if (!TryParseEntry(element, out var entry) || !seenIds.Add(entry.VersionId))
                    return Result(BackupSetCatalogStatus.InvalidResponse);
                entries.Add(entry);
            }

            return new BackupSetCatalogResult(
                BackupSetCatalogStatus.Available,
                entries.OrderByDescending(entry => entry.BackupTimeUtc).ToArray());
        }
        catch (JsonException)
        {
            return Result(BackupSetCatalogStatus.InvalidResponse);
        }
        catch (InvalidOperationException)
        {
            return Result(BackupSetCatalogStatus.InvalidResponse);
        }
        catch (FormatException)
        {
            return Result(BackupSetCatalogStatus.InvalidResponse);
        }
    }

    private static bool TryParseEntry(JsonElement element, out BackupSetCatalogEntry entry)
    {
        entry = null!;
        if (element.ValueKind != JsonValueKind.Object ||
            !HasOnlyProperties(element, "versionId", "backupTimeUtc", "backupType", "volumeCount") ||
            !element.TryGetProperty("versionId", out var idElement) || idElement.ValueKind != JsonValueKind.String ||
            !element.TryGetProperty("backupTimeUtc", out var timeElement) || timeElement.ValueKind != JsonValueKind.String ||
            !element.TryGetProperty("backupType", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var versionId = idElement.GetString();
        var timestampText = timeElement.GetString();
        var typeText = typeElement.GetString();
        if (!IsSafeVersionId(versionId) || timestampText is null || timestampText.Length > 40 || !timestampText.EndsWith('Z') ||
            !DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp) ||
            timestamp.Offset != TimeSpan.Zero)
        {
            return false;
        }

        var backupType = typeText switch
        {
            "Full" => BackupSetType.Full,
            "Incremental" => BackupSetType.Incremental,
            "Differential" => BackupSetType.Differential,
            "Other" => BackupSetType.Other,
            _ => (BackupSetType?)null
        };
        if (backupType is null)
            return false;

        int? volumeCount = null;
        if (element.TryGetProperty("volumeCount", out var volumeElement))
        {
            if (volumeElement.ValueKind != JsonValueKind.Number ||
                !volumeElement.TryGetInt32(out var parsedVolumeCount) ||
                parsedVolumeCount is < 0 or > MaximumVolumeCount)
            {
                return false;
            }
            volumeCount = parsedVolumeCount;
        }

        entry = new BackupSetCatalogEntry(versionId!, timestamp.ToUniversalTime(), backupType.Value, volumeCount);
        return true;
    }

    private static bool IsSafeVersionId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumVersionIdCharacters &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool HasOnlyProperties(JsonElement element, params string[] allowedProperties)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                return false;
        }
        return true;
    }

    private static BackupSetCatalogResult Result(BackupSetCatalogStatus status) =>
        new(status, Array.Empty<BackupSetCatalogEntry>());
}

/// <summary>Indisponibilidade explícita fora do Windows; não tenta simular a fonte real.</summary>
public sealed class UnsupportedBackupSetCatalogSource : IBackupSetCatalogSource
{
    public Task<BackupSetCatalogResult> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BackupSetCatalogResult(
            BackupSetCatalogStatus.Unavailable,
            Array.Empty<BackupSetCatalogEntry>()));
    }
}
