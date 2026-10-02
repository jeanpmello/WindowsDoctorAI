using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Executa apenas a consulta fixa Get-WBBackupSet no Windows PowerShell já instalado.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPowerShellBackupCommandRunner : IWindowsBackupCommandRunner
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);
    private const int MaximumOutputCharacters = 8 * 1024;

    private const string QueryScript = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
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
            $sets = @(Get-WBBackupSet -ErrorAction Stop -WarningAction SilentlyContinue -InformationAction SilentlyContinue)
            if ($sets.Count -eq 0) {
                [Console]::Out.WriteLine('{"status":"no_backup","count":0}')
                exit 0
            }
            $latest = $null
            $latestTime = [datetime]::MinValue
            foreach ($set in $sets) {
                $timeProperty = $set.PSObject.Properties['BackupTime']
                if ($null -eq $timeProperty -or $null -eq $timeProperty.Value) { continue }
                try { $candidateTime = [datetime]$timeProperty.Value } catch { continue }
                if ($null -eq $latest -or $candidateTime -gt $latestTime) {
                    $latest = $set
                    $latestTime = $candidateTime
                }
            }
            if ($null -eq $latest) {
                [Console]::Out.WriteLine('{"status":"invalid_response"}')
                exit 0
            }
            $safeType = 'Other'
            $typeProperty = $latest.PSObject.Properties['BackupType']
            if ($null -ne $typeProperty -and $null -ne $typeProperty.Value) {
                switch (([string]$typeProperty.Value).Trim().ToLowerInvariant()) {
                    'full' { $safeType = 'Full' }
                    'normal' { $safeType = 'Full' }
                    'incremental' { $safeType = 'Incremental' }
                    'differential' { $safeType = 'Differential' }
                }
            }
            $payload = [ordered]@{
                status = 'available'
                count = [int]$sets.Count
                latestBackupTimeUtc = $latestTime.ToUniversalTime().ToString('o', [Globalization.CultureInfo]::InvariantCulture)
                backupType = $safeType
            }
            [Console]::Out.WriteLine(($payload | ConvertTo-Json -Compress -Depth 2))
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
                [Console]::Out.WriteLine('{"status":"query_failed"}')
            }
        }
        """;

    public async Task<WindowsBackupCommandRunResult> GetBackupSetMetadataAsync(CancellationToken cancellationToken = default)
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
            // Não propagar detalhes internos ou caminhos do processo.
        }
    }

    private sealed class OutputLimitExceededException : Exception;
}
