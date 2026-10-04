using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Consulta somente metadados do catálogo; não navega, monta, lê arquivos nem restaura volumes.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPowerShellBackupSetCatalogCommandRunner : IWindowsBackupSetCatalogCommandRunner
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);
    private const int MaximumOutputCharacters = WindowsBackupSetCatalogDataSource.MaximumJsonCharacters;
    private const int MaximumBackupSets = WindowsBackupSetCatalogDataSource.MaximumBackupSets;
    private const int MaximumVolumeCount = WindowsBackupSetCatalogDataSource.MaximumVolumeCount;

    private static readonly string QueryScript = $$"""
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $MaximumBackupSets = {{MaximumBackupSets}}
        $MaximumVolumeCount = {{MaximumVolumeCount}}
        $modulePath = Join-Path $env:windir 'System32\WindowsPowerShell\v1.0\Modules\WindowsServerBackup\WindowsServerBackup.psd1'
        if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
            [Console]::Out.WriteLine('{"status":"module_unavailable"}')
            exit 0
        }
        try {
            Import-Module -Name $modulePath -ErrorAction Stop
            $command = Get-Command -Name 'Get-WBBackupSet' -CommandType Cmdlet -ErrorAction SilentlyContinue
            if ($null -eq $command -or $command.ModuleName -ne 'WindowsServerBackup') {
                [Console]::Out.WriteLine('{"status":"module_unavailable"}')
                exit 0
            }

            # O pipeline é limitado antes de materializar; o +1 permite detectar catálogo truncado.
            $sets = @(Get-WBBackupSet -ErrorAction Stop -WarningAction SilentlyContinue -InformationAction SilentlyContinue |
                Select-Object -First ($MaximumBackupSets + 1))
            if ($sets.Count -eq 0) {
                [Console]::Out.WriteLine('{"status":"no_backup","backupSets":[]}')
                exit 0
            }
            if ($sets.Count -gt $MaximumBackupSets) {
                [Console]::Out.WriteLine('{"status":"too_many"}')
                exit 0
            }

            $safeSets = [System.Collections.Generic.List[object]]::new()
            $seenIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($set in $sets) {
                # Confirma a forma do objeto em runtime antes de ler nomes explicitamente allowlistados.
                if ($null -eq $set -or $set.GetType().Name -ne 'WBBackupSet') {
                    [Console]::Out.WriteLine('{"status":"unavailable"}')
                    exit 0
                }
                $versionProperty = $set.PSObject.Properties['VersionId']
                $timeProperty = $set.PSObject.Properties['BackupTime']
                $typeProperty = $set.PSObject.Properties['BackupType']
                if ($null -eq $versionProperty -or $null -eq $timeProperty -or $null -eq $typeProperty -or
                    $null -eq $versionProperty.Value -or $null -eq $timeProperty.Value -or $null -eq $typeProperty.Value) {
                    [Console]::Out.WriteLine('{"status":"unavailable"}')
                    exit 0
                }

                $versionId = [string]$versionProperty.Value
                if ($versionId.Length -lt 1 -or $versionId.Length -gt 128 -or
                    $versionId -notmatch '^[A-Za-z0-9._-]+$' -or -not $seenIds.Add($versionId)) {
                    [Console]::Out.WriteLine('{"status":"invalid_response"}')
                    exit 0
                }

                $rawTime = $timeProperty.Value
                if ($rawTime -is [datetimeoffset]) {
                    $backupTimeUtc = $rawTime.ToUniversalTime().UtcDateTime
                }
                elseif ($rawTime -is [datetime]) {
                    if ($rawTime -eq [datetime]::MinValue -or $rawTime -eq [datetime]::MaxValue) {
                        [Console]::Out.WriteLine('{"status":"unavailable"}')
                        exit 0
                    }
                    $backupTimeUtc = $rawTime.ToUniversalTime()
                }
                else {
                    [Console]::Out.WriteLine('{"status":"unavailable"}')
                    exit 0
                }

                $safeType = 'Other'
                switch ($typeProperty.Value.ToString().Trim().ToLowerInvariant()) {
                    'full' { $safeType = 'Full' }
                    'normal' { $safeType = 'Full' }
                    'incremental' { $safeType = 'Incremental' }
                    'differential' { $safeType = 'Differential' }
                }

                $backupTimeText = $backupTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", [Globalization.CultureInfo]::InvariantCulture)
                # Conta somente a coleção direta em WBBackupSet; nenhum membro de WBVolume é lido.
                $volumeProperty = $set.PSObject.Properties['Volume']
                if ($null -ne $volumeProperty) {
                    $volumeValue = $volumeProperty.Value
                    if ($null -eq $volumeValue) {
                        $volumeCount = 0
                    }
                    elseif ($volumeValue -is [string]) {
                        [Console]::Out.WriteLine('{"status":"unavailable"}')
                        exit 0
                    }
                    else {
                        $volumeCount = @($volumeValue).Count
                    }
                    if ($volumeCount -gt $MaximumVolumeCount) {
                        [Console]::Out.WriteLine('{"status":"invalid_response"}')
                        exit 0
                    }
                    $safeSet = [pscustomobject][ordered]@{
                        versionId = [string]$versionId
                        backupTimeUtc = [string]$backupTimeText
                        backupType = [string]$safeType
                        volumeCount = [int]$volumeCount
                    }
                }
                else {
                    $safeSet = [pscustomobject][ordered]@{
                        versionId = [string]$versionId
                        backupTimeUtc = [string]$backupTimeText
                        backupType = [string]$safeType
                    }
                }
                $safeSets.Add($safeSet)
            }

            $payload = [pscustomobject][ordered]@{ status = [string]'available'; backupSets = [object[]]$safeSets.ToArray() }
            [Console]::Out.WriteLine(($payload | ConvertTo-Json -Compress -Depth 4))
        }
        catch {
            $category = [string]$_.CategoryInfo.Category
            $errorId = [string]$_.FullyQualifiedErrorId
            $accessDenied = $category -eq 'PermissionDenied' -or
                $_.Exception -is [System.UnauthorizedAccessException] -or
                $_.Exception -is [System.Security.SecurityException] -or
                $errorId -match '(?i)AccessDenied|UnauthorizedAccess|SecurityError'
            if ($accessDenied) {
                [Console]::Out.WriteLine('{"status":"access_denied"}')
            }
            else {
                [Console]::Out.WriteLine('{"status":"unavailable"}')
            }
        }
        """;

    public async Task<WindowsBackupCommandRunResult> GetBackupSetCatalogAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.Failed);

        var startInfo = CreateStartInfo();
        if (!File.Exists(startInfo.FileName))
            return new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.Failed);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                return new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.Failed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.Failed);
        }

        using var timeoutSource = new CancellationTokenSource(QueryTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var outputTask = ReadLimitedOutputAsync(process, linkedSource.Token);
        var errorTask = DrainStandardErrorAsync(process, linkedSource.Token);

        try
        {
            await Task.WhenAll(process.WaitForExitAsync(linkedSource.Token), outputTask, errorTask).ConfigureAwait(false);
            return process.ExitCode == 0
                ? new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.Completed, await outputTask.ConfigureAwait(false))
                : new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.Failed);
        }
        catch (OutputLimitExceededException)
        {
            TryKill(process);
            return new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.InvalidResponse);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.TimedOut);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            TryKill(process);
            return new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.Failed);
        }
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemDirectoryName = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32";
        var executable = Path.Combine(windowsDirectory, systemDirectoryName, "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(QueryScript);
        return startInfo;
    }

    private static async Task<string> ReadLimitedOutputAsync(Process process, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[1024];
        while (true)
        {
            var read = await process.StandardOutput.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return output.ToString();
            if (output.Length + read > MaximumOutputCharacters)
            {
                TryKill(process);
                throw new OutputLimitExceededException();
            }
            output.Append(buffer, 0, read);
        }
    }

    private static async Task DrainStandardErrorAsync(Process process, CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        while (await process.StandardError.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
        {
            // stderr é drenado para evitar bloqueio, mas nunca é exibido, persistido ou registrado.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Detalhes do processo não são propagados.
        }
    }

    private sealed class OutputLimitExceededException : Exception;
}
