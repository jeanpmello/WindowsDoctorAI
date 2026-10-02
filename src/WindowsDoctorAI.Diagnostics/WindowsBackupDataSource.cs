using System.Text.Json;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Consulta os metadados de backup sem preservar ou expor dados sensíveis da fonte.</summary>
public sealed class WindowsBackupDataSource(IWindowsBackupCommandRunner commandRunner) : IWindowsBackupDataSource
{
    private const int MaximumJsonCharacters = 8 * 1024;

    public async Task<WindowsBackupProbe> ReadBackupSetsAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandRunner);
        cancellationToken.ThrowIfCancellationRequested();

        WindowsBackupCommandRunResult commandResult;
        try
        {
            commandResult = await commandRunner.GetBackupSetMetadataAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new WindowsBackupProbe(WindowsBackupProbeStatus.QueryFailed);
        }

        if (commandResult is null)
            return new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse);

        return commandResult.Status switch
        {
            WindowsBackupCommandRunStatus.Completed => ParseResponse(commandResult.StandardOutput),
            WindowsBackupCommandRunStatus.TimedOut => new WindowsBackupProbe(WindowsBackupProbeStatus.TimedOut),
            WindowsBackupCommandRunStatus.Failed => new WindowsBackupProbe(WindowsBackupProbeStatus.QueryFailed),
            WindowsBackupCommandRunStatus.InvalidResponse => new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse),
            _ => new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse)
        };
    }

    private static WindowsBackupProbe ParseResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumJsonCharacters)
            return new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("status", out var statusElement) ||
                statusElement.ValueKind != JsonValueKind.String)
            {
                return new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse);
            }

            return statusElement.GetString() switch
            {
                "available" => ParseAvailable(root),
                "no_backup" => ParseNoBackupSets(root),
                "module_unavailable" => new WindowsBackupProbe(WindowsBackupProbeStatus.ModuleUnavailable),
                "access_denied" => new WindowsBackupProbe(WindowsBackupProbeStatus.AccessDenied),
                "query_failed" => new WindowsBackupProbe(WindowsBackupProbeStatus.QueryFailed),
                "invalid_response" => new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse),
                _ => new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse)
            };
        }
        catch (JsonException)
        {
            return new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse);
        }
        catch (InvalidOperationException)
        {
            return new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse);
        }
        catch (FormatException)
        {
            return new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse);
        }
    }

    private static WindowsBackupProbe ParseAvailable(JsonElement root)
    {
        if (!root.TryGetProperty("count", out var countElement) ||
            countElement.ValueKind != JsonValueKind.Number ||
            !countElement.TryGetInt32(out var count) ||
            count is < 1 or > 1_000_000 ||
            !root.TryGetProperty("latestBackupTimeUtc", out var timeElement) ||
            timeElement.ValueKind != JsonValueKind.String ||
            !timeElement.TryGetDateTimeOffset(out var latestBackupTimeUtc) ||
            !root.TryGetProperty("backupType", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            return new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse);
        }

        var backupType = typeElement.GetString() switch
        {
            "Full" => WindowsBackupType.Full,
            "Incremental" => WindowsBackupType.Incremental,
            "Differential" => WindowsBackupType.Differential,
            "Other" => WindowsBackupType.Other,
            _ => (WindowsBackupType?)null
        };

        return backupType is null
            ? new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse)
            : new WindowsBackupProbe(WindowsBackupProbeStatus.Available, count, latestBackupTimeUtc.ToUniversalTime(), backupType);
    }

    private static WindowsBackupProbe ParseNoBackupSets(JsonElement root)
    {
        if (!root.TryGetProperty("count", out var countElement) ||
            countElement.ValueKind != JsonValueKind.Number ||
            !countElement.TryGetInt32(out var count) ||
            count != 0)
        {
            return new WindowsBackupProbe(WindowsBackupProbeStatus.InvalidResponse);
        }

        return new WindowsBackupProbe(WindowsBackupProbeStatus.NoBackupSets, 0);
    }
}
