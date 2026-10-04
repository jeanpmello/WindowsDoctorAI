using System.Diagnostics;
using System.Reflection;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class WindowsBackupSetCatalogTests
{
    private const string VersionOne = "version-2026-10-01T08_30_00Z";
    private const string VersionTwo = "version-2026-10-02T08_30_00Z";

    [Fact]
    public async Task EmptyCatalogIsDistinctFromUnavailableAndHasNoEntries()
    {
        var result = await Source("""{"status":"no_backup","backupSets":[]}""").GetCatalogAsync();

        Assert.Equal(BackupSetCatalogStatus.NoBackupSets, result.Status);
        Assert.Empty(result.BackupSets);
    }

    [Fact]
    public async Task MultipleSetsAreTypedNormalizedUtcSortedAndMinimized()
    {
        var json = $$"""
            {
              "status":"available",
              "backupSets":[
                {"versionId":"{{VersionOne}}","backupTimeUtc":"2026-10-01T08:30:00Z","backupType":"Full","volumeCount":2},
                {"versionId":"{{VersionTwo}}","backupTimeUtc":"2026-10-02T08:30:00Z","backupType":"Incremental"}
              ]
            }
            """;
        var result = await Source(json).GetCatalogAsync();

        Assert.Equal(BackupSetCatalogStatus.Available, result.Status);
        Assert.Equal(2, result.BackupSets.Count);
        Assert.Equal(VersionTwo, result.BackupSets[0].VersionId);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 8, 30, 0, TimeSpan.Zero), result.BackupSets[0].BackupTimeUtc);
        Assert.Equal(BackupSetType.Incremental, result.BackupSets[0].BackupType);
        Assert.Null(result.BackupSets[0].VolumeCount);
        Assert.Equal(VersionOne, result.BackupSets[1].VersionId);
        Assert.Equal(2, result.BackupSets[1].VolumeCount);
    }

    [Theory]
    [InlineData("{\"status\":\"available\",\"backupSets\":[{\"versionId\":\"same\",\"backupTimeUtc\":\"2026-10-01T08:30:00Z\",\"backupType\":\"Full\"},{\"versionId\":\"same\",\"backupTimeUtc\":\"2026-10-02T08:30:00Z\",\"backupType\":\"Full\"}]}")]
    [InlineData("{\"status\":\"available\",\"backupSets\":[{\"backupTimeUtc\":\"2026-10-01T08:30:00Z\",\"backupType\":\"Full\"}]}")]
    [InlineData("{\"status\":\"available\",\"backupSets\":[{\"versionId\":\"v1\",\"backupTimeUtc\":\"not-a-timestamp\",\"backupType\":\"Full\"}]}")]
    [InlineData("{\"status\":\"available\",\"backupSets\":[{\"versionId\":\"v1\",\"backupTimeUtc\":\"2026-10-01T08:30:00+02:00\",\"backupType\":\"Full\"}]}")]
    [InlineData("{\"status\":\"available\",\"backupSets\":[{\"versionId\":\"C:\\\\host\\\\user\",\"backupTimeUtc\":\"2026-10-01T08:30:00Z\",\"backupType\":\"Full\"}]}")]
    [InlineData("{\"status\":\"available\",\"backupSets\":[{\"versionId\":\"v1\",\"backupTimeUtc\":\"2026-10-01T08:30:00Z\",\"backupType\":\"hostname-private\"}]}")]
    [InlineData("{\"status\":\"available\",\"backupSets\":[{\"versionId\":\"v1\",\"backupTimeUtc\":\"2026-10-01T08:30:00Z\",\"backupType\":\"Full\",\"machineName\":\"HOST_PRIVATE\"}]}")]
    [InlineData("{\"status\":\"available\",\"backupSets\":[{\"versionId\":\"v1\",\"backupTimeUtc\":\"2026-10-01T08:30:00Z\",\"backupType\":\"Full\",\"volumeCount\":129}]}")]
    public async Task DuplicateMissingUnsafeUnknownOrOutOfRangeFieldsAreRejected(string json)
    {
        var result = await Source(json).GetCatalogAsync();

        Assert.Equal(BackupSetCatalogStatus.InvalidResponse, result.Status);
        Assert.Empty(result.BackupSets);
    }

    [Fact]
    public async Task MoreThanMaximumSetsAndExplicitTruncationAreReportedWithoutPartialList()
    {
        var sets = string.Join(',', Enumerable.Range(0, BackupSetCatalogLimits.MaximumBackupSets + 1)
            .Select(index => $$"""{"versionId":"v{{index}}","backupTimeUtc":"2026-10-01T08:30:00Z","backupType":"Other"}"""));
        var oversizedCount = await Source($$"""{"status":"available","backupSets":[{{sets}}]}""").GetCatalogAsync();
        var explicitTruncation = await Source("""{"status":"too_many"}""").GetCatalogAsync();

        Assert.Equal(BackupSetCatalogStatus.TooManyBackupSets, oversizedCount.Status);
        Assert.Empty(oversizedCount.BackupSets);
        Assert.Equal(BackupSetCatalogStatus.TooManyBackupSets, explicitTruncation.Status);
        Assert.Empty(explicitTruncation.BackupSets);
    }

    [Theory]
    [InlineData("{\"status\":\"module_unavailable\"}", BackupSetCatalogStatus.ModuleUnavailable)]
    [InlineData("{\"status\":\"access_denied\"}", BackupSetCatalogStatus.AccessDenied)]
    [InlineData("{\"status\":\"unavailable\"}", BackupSetCatalogStatus.Unavailable)]
    [InlineData("{\"status\":\"too_many\"}", BackupSetCatalogStatus.TooManyBackupSets)]
    public async Task GenericStatusesMapWithoutRawDetails(string json, BackupSetCatalogStatus expected)
    {
        var result = await Source(json).GetCatalogAsync();

        Assert.Equal(expected, result.Status);
        Assert.Empty(result.BackupSets);
    }

    [Fact]
    public async Task TimeoutFailureThrowAndOversizedOutputAreBounded()
    {
        var timeout = await Source(new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.TimedOut)).GetCatalogAsync();
        var failure = await Source(new WindowsBackupCommandRunResult(WindowsBackupCommandRunStatus.Failed, "PRIVATE_PATH" )).GetCatalogAsync();
        var oversized = await Source(Completed(new string('x', BackupSetCatalogLimits.MaximumJsonCharacters + 1))).GetCatalogAsync();
        var throwing = await new WindowsBackupSetCatalogDataSource(new ThrowingRunner()).GetCatalogAsync();

        Assert.Equal(BackupSetCatalogStatus.TimedOut, timeout.Status);
        Assert.Equal(BackupSetCatalogStatus.Unavailable, failure.Status);
        Assert.Equal(BackupSetCatalogStatus.InvalidResponse, oversized.Status);
        Assert.Equal(BackupSetCatalogStatus.Unavailable, throwing.Status);
        Assert.Empty(timeout.BackupSets);
        Assert.Empty(failure.BackupSets);
        Assert.Empty(oversized.BackupSets);
        Assert.Empty(throwing.BackupSets);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToUnavailable()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new FakeRunner(Completed("{}"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WindowsBackupSetCatalogDataSource(runner).GetCatalogAsync(cancellation.Token));
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public void CatalogRunnerUsesOnlyTheReadOnlyAllowlistedCommandAndNoBrowseOrRestoreSurface()
    {
        var runnerInterfaceMethods = typeof(IWindowsBackupSetCatalogCommandRunner).GetMethods().Select(method => method.Name).ToArray();
        Assert.Equal([nameof(IWindowsBackupSetCatalogCommandRunner.GetBackupSetCatalogAsync)], runnerInterfaceMethods);

        var scriptField = typeof(WindowsPowerShellBackupSetCatalogCommandRunner).GetField("QueryScript", BindingFlags.NonPublic | BindingFlags.Static);
        var script = Assert.IsType<string>(scriptField?.GetValue(null));
        Assert.Contains("Get-WBBackupSet", script, StringComparison.Ordinal);
        Assert.Contains("Select-Object -First", script, StringComparison.Ordinal);
        Assert.Contains("PSObject.Properties['VersionId']", script, StringComparison.Ordinal);
        Assert.Contains("PSObject.Properties['BackupTime']", script, StringComparison.Ordinal);
        Assert.Contains("PSObject.Properties['BackupType']", script, StringComparison.Ordinal);
        Assert.Contains("[pscustomobject][ordered]@{", script, StringComparison.Ordinal);
        Assert.Contains($"$MaximumBackupSets = {BackupSetCatalogLimits.MaximumBackupSets}", script, StringComparison.Ordinal);
        Assert.Contains($"$MaximumVolumeCount = {BackupSetCatalogLimits.MaximumVolumeCount}", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-WBBackupVolumeBrowsePath", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BrowsePath", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start-WBFileRecovery", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Restore-", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start-", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wbadmin", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-ChildItem", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadAllBytes", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mount-VHD", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Add-WB", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("New-WB", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-WB", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-WB", script, StringComparison.OrdinalIgnoreCase);

        var startInfoFactory = typeof(WindowsPowerShellBackupSetCatalogCommandRunner).GetMethod("CreateStartInfo", BindingFlags.NonPublic | BindingFlags.Static);
        var startInfo = Assert.IsType<ProcessStartInfo>(startInfoFactory?.Invoke(null, null));
        Assert.False(startInfo.UseShellExecute);
        Assert.Contains("-NoProfile", startInfo.ArgumentList);
        Assert.Contains("-NonInteractive", startInfo.ArgumentList);
        Assert.DoesNotContain("-ExecutionPolicy", startInfo.ArgumentList);
    }

    private static WindowsBackupSetCatalogDataSource Source(string json) => Source(Completed(json));
    private static WindowsBackupSetCatalogDataSource Source(WindowsBackupCommandRunResult result) => new(new FakeRunner(result));
    private static WindowsBackupCommandRunResult Completed(string json) => new(WindowsBackupCommandRunStatus.Completed, json);

    private sealed class FakeRunner(WindowsBackupCommandRunResult result) : IWindowsBackupSetCatalogCommandRunner
    {
        public int CallCount { get; private set; }
        public Task<WindowsBackupCommandRunResult> GetBackupSetCatalogAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingRunner : IWindowsBackupSetCatalogCommandRunner
    {
        public Task<WindowsBackupCommandRunResult> GetBackupSetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<WindowsBackupCommandRunResult>(new InvalidOperationException("private path must not escape"));
    }
}
