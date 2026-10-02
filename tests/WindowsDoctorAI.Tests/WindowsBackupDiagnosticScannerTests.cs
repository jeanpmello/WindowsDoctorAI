using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class WindowsBackupDiagnosticScannerTests
{
    private const string SensitiveHost = "HOST_BACKUP_PRIVATE_91";
    private const string SensitivePath = @"E:\Confidential\Finance\archive.vhdx";
    private const string SensitiveTarget = @"\\private-backup\hidden-share";

    [Fact]
    public async Task AvailableResponseYieldsOnlyCountDateAndMinimizedTypeInResultsAndHtml()
    {
        var runner = Runner("""
            {
              "status":"available",
              "count":2,
              "latestBackupTimeUtc":"2026-10-02T08:30:00Z",
              "backupType":"Full",
              "machineName":"HOST_BACKUP_PRIVATE_91",
              "backupItems":["E:\\Confidential\\Finance\\archive.vhdx"],
              "backupTarget":"\\\\private-backup\\hidden-share"
            }
            """);
        var source = new WindowsBackupDataSource(runner);

        var probe = await source.ReadBackupSetsAsync();
        var result = Assert.Single(await new WindowsBackupDiagnosticScanner(source).ScanAsync());

        Assert.Equal(WindowsBackupProbeStatus.Available, probe.Status);
        Assert.Equal(2, probe.BackupSetCount);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 8, 30, 0, TimeSpan.Zero), probe.LatestBackupTimeUtc);
        Assert.Equal(WindowsBackupType.Full, probe.BackupType);
        Assert.Equal(DiagnosticStatus.NotVerified, result.Status);
        Assert.Contains("2 conjunto(s)", result.Title);
        Assert.Contains("data mais recente", result.Evidence);
        Assert.Contains("tipo: Completo", result.Evidence);
        Assert.Contains("não comprova integridade", result.Description);
        Assert.Contains("Nenhuma restauração foi iniciada", result.Recommendation);
        AssertNoSensitiveData(result);
        var uiText = FormatUi(result);
        Assert.Contains("Metadados de backup: 2 conjunto(s)", uiText, StringComparison.Ordinal);
        Assert.Contains("data mais recente", uiText, StringComparison.Ordinal);
        Assert.Contains("tipo: Completo", uiText, StringComparison.Ordinal);
        Assert.Contains("Não verificado", uiText, StringComparison.Ordinal);
        AssertUiSafe(uiText);

        var report = new DiagnosticReport([result], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TimeSpan.Zero, null);
        var redacted = DiagnosticPrivacyRedactor.RedactReport(report)!;
        AssertNoSensitiveData(redacted.Results.Single());
        var run = new DiagnosticRun(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            TimeSpan.Zero, new ComputerInventory(), report);
        var html = new HtmlDiagnosticReportFormatter().Format(
            run,
            [],
            new RootCauseAnalysis("Causa indeterminada.", []));
        Assert.DoesNotContain(SensitiveHost, html, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitivePath, html, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveTarget, html, StringComparison.Ordinal);
        Assert.DoesNotContain("machineName", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("backupItems", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Conjuntos retornados: 2", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"status\":\"no_backup\",\"count\":0}", WindowsBackupProbeStatus.NoBackupSets, DiagnosticStatus.Finding)]
    [InlineData("{\"status\":\"module_unavailable\"}", WindowsBackupProbeStatus.ModuleUnavailable, DiagnosticStatus.Unavailable)]
    [InlineData("{\"status\":\"access_denied\"}", WindowsBackupProbeStatus.AccessDenied, DiagnosticStatus.Unavailable)]
    [InlineData("{\"status\":\"query_failed\"}", WindowsBackupProbeStatus.QueryFailed, DiagnosticStatus.Unavailable)]
    [InlineData("{broken json with E:\\Confidential\\Finance\\archive.vhdx", WindowsBackupProbeStatus.InvalidResponse, DiagnosticStatus.Unavailable)]
    public async Task HandlesDeterministicJsonStatusesWithoutEchoingSourceData(
        string json,
        WindowsBackupProbeStatus expectedProbeStatus,
        DiagnosticStatus expectedDiagnosticStatus)
    {
        var source = new WindowsBackupDataSource(Runner(json));

        var probe = await source.ReadBackupSetsAsync();
        var result = Assert.Single(await new WindowsBackupDiagnosticScanner(source).ScanAsync());

        Assert.Equal(expectedProbeStatus, probe.Status);
        Assert.Equal(expectedDiagnosticStatus, result.Status);
        AssertNoSensitiveData(result);
        var uiText = FormatUi(result);
        Assert.Contains(result.Title, uiText, StringComparison.Ordinal);
        AssertUiSafe(uiText);
        Assert.DoesNotContain(SensitivePath, result.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveHost, result.Evidence, StringComparison.Ordinal);
        if (expectedProbeStatus == WindowsBackupProbeStatus.AccessDenied)
        {
            Assert.Contains("não solicita elevação", result.Recommendation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("runas", result.Recommendation, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TimeoutAndProcessFailureAreDistinctUnavailableStatesAndNeverLeakOutput()
    {
        var timeoutSource = new WindowsBackupDataSource(new FakeWindowsBackupCommandRunner(
            new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.TimedOut, SensitivePath)));
        var failedSource = new WindowsBackupDataSource(new FakeWindowsBackupCommandRunner(
            new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.Failed, SensitivePath)));

        var timeout = Assert.Single(await new WindowsBackupDiagnosticScanner(timeoutSource).ScanAsync());
        var failed = Assert.Single(await new WindowsBackupDiagnosticScanner(failedSource).ScanAsync());

        Assert.Contains("expirou", timeout.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiagnosticStatus.Unavailable, timeout.Status);
        Assert.Equal(DiagnosticStatus.Unavailable, failed.Status);
        AssertNoSensitiveData(timeout);
        AssertNoSensitiveData(failed);
        Assert.Contains(timeout.Title, FormatUi(timeout), StringComparison.Ordinal);
        Assert.Contains(failed.Title, FormatUi(failed), StringComparison.Ordinal);
        AssertUiSafe(FormatUi(timeout));
        AssertUiSafe(FormatUi(failed));
    }

    [Fact]
    public async Task RawCommandOutputNeverAppearsInDiagnosticLogs()
    {
        var runner = Runner("""
            {
              "status":"available",
              "count":1,
              "latestBackupTimeUtc":"2026-10-02T08:30:00Z",
              "backupType":"Full",
              "machineName":"HOST_BACKUP_PRIVATE_91",
              "backupItems":["E:\\Confidential\\Finance\\archive.vhdx"],
              "backupTarget":"\\\\private-backup\\hidden-share"
            }
            """);
        var logger = new CapturingLogger<DiagnosticEngine>();
        var engine = new DiagnosticEngine(
            [new WindowsBackupDiagnosticScanner(new WindowsBackupDataSource(runner))],
            logger);

        var report = await engine.RunAsync();
        var logs = string.Join('\n', logger.Messages);

        Assert.Equal(DiagnosticStatus.NotVerified, Assert.Single(report.Results).Status);
        Assert.Null(report.HealthScore);
        Assert.Equal(0, report.VerifiedChecks);
        Assert.DoesNotContain(SensitiveHost, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitivePath, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveTarget, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFieldsUnknownTypesOversizedOutputAndThrowingRunnerBecomeInvalidOrFailed()
    {
        var malformedSource = new WindowsBackupDataSource(Runner("""{"status":"available","count":1,"latestBackupTimeUtc":"not-a-date","backupType":"hostname-secret"}"""));
        var oversizedSource = new WindowsBackupDataSource(Runner(new string('x', 9000)));
        var throwingSource = new WindowsBackupDataSource(new ThrowingWindowsBackupCommandRunner());

        Assert.Equal(WindowsBackupProbeStatus.InvalidResponse, (await malformedSource.ReadBackupSetsAsync()).Status);
        Assert.Equal(WindowsBackupProbeStatus.InvalidResponse, (await oversizedSource.ReadBackupSetsAsync()).Status);
        Assert.Equal(WindowsBackupProbeStatus.QueryFailed, (await throwingSource.ReadBackupSetsAsync()).Status);

        foreach (var source in new IWindowsBackupDataSource[] { malformedSource, oversizedSource, throwingSource })
        {
            var result = Assert.Single(await new WindowsBackupDiagnosticScanner(source).ScanAsync());
            AssertNoSensitiveData(result);
            Assert.DoesNotContain("hostname-secret", result.Title, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DiagnosticPathCanInvokeOnlyTheSingleAllowlistedReadCommand()
    {
        var runner = Runner("""{"status":"no_backup","count":0}""");
        var scanner = new WindowsBackupDiagnosticScanner(new WindowsBackupDataSource(runner));

        _ = await scanner.ScanAsync();

        Assert.Equal(1, runner.CallCount);
        var commandMethods = typeof(IWindowsBackupCommandRunner).GetMethods().Select(method => method.Name).ToArray();
        Assert.Equal([nameof(IWindowsBackupCommandRunner.GetBackupSetMetadataAsync)], commandMethods);

        var scriptField = typeof(WindowsPowerShellBackupCommandRunner).GetField("QueryScript", BindingFlags.NonPublic | BindingFlags.Static);
        var script = Assert.IsType<string>(scriptField?.GetRawConstantValue());
        Assert.Contains("Get-WBBackupSet", script, StringComparison.Ordinal);
        Assert.DoesNotContain("wbadmin", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Restore-", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start-", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Add-WB", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("New-WB", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-WB", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-WB", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stop-WB", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Resume-WB", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-ExecutionPolicy", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Import-WindowsFeature", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Install-WindowsFeature", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Add-WindowsFeature", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Enable-WindowsOptionalFeature", script, StringComparison.OrdinalIgnoreCase);

        var startInfoFactory = typeof(WindowsPowerShellBackupCommandRunner).GetMethod("CreateStartInfo", BindingFlags.NonPublic | BindingFlags.Static);
        var startInfo = Assert.IsType<ProcessStartInfo>(startInfoFactory?.Invoke(null, null));
        Assert.EndsWith("WindowsPowerShell" + Path.DirectorySeparatorChar + "v1.0" + Path.DirectorySeparatorChar + "powershell.exe", startInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.Contains("-NoProfile", startInfo.ArgumentList);
        Assert.Contains("-NonInteractive", startInfo.ArgumentList);
        Assert.DoesNotContain("-ExecutionPolicy", startInfo.ArgumentList);
        Assert.DoesNotContain("wbadmin", string.Join(' ', startInfo.ArgumentList), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationIsNotRewrittenAsQueryFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new WindowsBackupDataSource(Runner("{}"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadBackupSetsAsync(cancellation.Token));
    }

    private static void AssertNoSensitiveData(DiagnosticResult result)
    {
        var fields = string.Join('\n', result.ScannerName, result.Category, result.Title, result.Description, result.Recommendation, result.Evidence);
        Assert.DoesNotContain(SensitiveHost, fields, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitivePath, fields, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveTarget, fields, StringComparison.Ordinal);
        Assert.DoesNotContain("backupItems", fields, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("machineName", fields, StringComparison.OrdinalIgnoreCase);
    }

    private static FakeWindowsBackupCommandRunner Runner(string? output) => new(
        new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.Completed, output));

    private static string FormatUi(DiagnosticResult result)
    {
        var now = DateTimeOffset.UtcNow;
        return DiagnosticDisplayFormatter.FormatFindings(
            new DiagnosticReport([result], now, now, TimeSpan.Zero, null),
            new ComputerInventory());
    }

    private static void AssertUiSafe(string text)
    {
        Assert.DoesNotContain(SensitiveHost, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitivePath, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveTarget, text, StringComparison.Ordinal);
        Assert.DoesNotContain("backupItems", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("machineName", text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeWindowsBackupCommandRunner(WindowsBackupCommandRunResult result) : IWindowsBackupCommandRunner
    {
        public int CallCount { get; private set; }

        public Task<WindowsBackupCommandRunResult> GetBackupSetMetadataAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingWindowsBackupCommandRunner : IWindowsBackupCommandRunner
    {
        public Task<WindowsBackupCommandRunResult> GetBackupSetMetadataAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<WindowsBackupCommandRunResult>(new InvalidOperationException(SensitivePath));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
                Messages.Add(exception.ToString());
        }
    }
}
