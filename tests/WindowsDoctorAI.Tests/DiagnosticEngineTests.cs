using Microsoft.Extensions.Logging.Abstractions;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class DiagnosticEngineTests
{
    [Fact]
    public async Task RunsSafePluginsInParallelAndNormalizesCommonFieldsAndTiming()
    {
        var active = 0;
        var maximumActive = 0;
        async Task<IReadOnlyList<DiagnosticResult>> Scan(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximumActive, current);
            try
            {
                await Task.Delay(60, cancellationToken);
                return [RawResult("plugin identity", "wrong category", DiagnosticSeverity.Information, DiagnosticStatus.Healthy)];
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        var first = new TestScanner("Plugin A", "Sistema", true, Scan);
        var second = new TestScanner("Plugin B", "Hardware", true, Scan);
        var report = await CreateEngine(first, second).RunAsync();

        Assert.Equal(2, maximumActive);
        Assert.Equal(2, report.VerifiedChecks);
        Assert.Equal(new[] { "Plugin A", "Plugin B" }, report.Results.Select(result => result.ScannerName));
        Assert.Equal(new[] { "Sistema", "Hardware" }, report.Results.Select(result => result.Category));
        Assert.All(report.Results, result => Assert.True(result.Duration > TimeSpan.Zero));
        Assert.All(report.Results, result => Assert.InRange(result.Timestamp, report.StartedAtUtc, report.CompletedAtUtc));
        Assert.Equal(100, report.HealthScore?.Value);
    }

    [Fact]
    public async Task IsolatesPluginFailureAndContinuesOtherScanners()
    {
        var healthy = new TestScanner("Available", "Sistema", true, _ => Task.FromResult<IReadOnlyList<DiagnosticResult>>(
            [RawResult("", "", DiagnosticSeverity.Information, DiagnosticStatus.Healthy)]));
        var failing = new TestScanner("Broken", "Drivers", true, _ => throw new InvalidOperationException("internal test detail"));

        var report = await CreateEngine(healthy, failing).RunAsync();

        Assert.Equal(2, report.Results.Count);
        var failure = Assert.Single(report.Results, result => result.ScannerName == "Broken");
        Assert.Equal(DiagnosticStatus.Unavailable, failure.Status);
        Assert.Contains("InvalidOperationException", failure.Evidence);
        Assert.DoesNotContain("internal test detail", failure.Description);
        Assert.Equal(100, report.HealthScore?.Value);
    }

    [Fact]
    public async Task CalculatesScoreFromVerifiedFindingsAndCountsOnlyCriticalAndWarnings()
    {
        var plugin = new TestScanner("Scoring", "System", true, _ => Task.FromResult<IReadOnlyList<DiagnosticResult>>(
        [
            RawResult("critical", "", DiagnosticSeverity.Critical, DiagnosticStatus.Finding),
            RawResult("warning 1", "", DiagnosticSeverity.Warning, DiagnosticStatus.Finding),
            RawResult("warning 2", "", DiagnosticSeverity.Warning, DiagnosticStatus.Finding),
            RawResult("unavailable", "", DiagnosticSeverity.Critical, DiagnosticStatus.Unavailable),
            RawResult("healthy", "", DiagnosticSeverity.Information, DiagnosticStatus.Healthy)
        ]));

        var report = await CreateEngine(plugin).RunAsync();

        Assert.Equal(59, report.HealthScore?.Value);
        Assert.Equal(1, report.CriticalProblems);
        Assert.Equal(2, report.Warnings);
        Assert.Equal(4, report.VerifiedChecks);
        Assert.Equal(1, report.UnavailableChecks);
        Assert.Equal(3, Assert.Single(report.Categories).Findings);
    }

    [Fact]
    public async Task DoesNotCalculateScoreWhenEveryCheckIsUnavailableOrUnverified()
    {
        var plugin = new TestScanner("No data", "Sistema", true, _ => Task.FromResult<IReadOnlyList<DiagnosticResult>>(
        [
            RawResult("unavailable", "", DiagnosticSeverity.Information, DiagnosticStatus.Unavailable),
            RawResult("not verified", "", DiagnosticSeverity.Information, DiagnosticStatus.NotVerified)
        ]));

        var report = await CreateEngine(plugin).RunAsync();

        Assert.Null(report.HealthScore);
        Assert.Equal(0, report.VerifiedChecks);
    }

    [Fact]
    public async Task RunsPluginsThatDeclareSerialExecutionOneAtATime()
    {
        var active = 0;
        var maximumActive = 0;
        async Task<IReadOnlyList<DiagnosticResult>> Scan(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximumActive, current);
            try
            {
                await Task.Delay(25, cancellationToken);
                return [RawResult("serial", "", DiagnosticSeverity.Information, DiagnosticStatus.Healthy)];
            }
            finally { Interlocked.Decrement(ref active); }
        }

        var report = await CreateEngine(
            new TestScanner("Serial A", "System", false, Scan),
            new TestScanner("Serial B", "System", false, Scan)).RunAsync();

        Assert.Equal(1, maximumActive);
        Assert.Equal(2, report.VerifiedChecks);
    }

    [Fact]
    public async Task CancellationRequestedByCallerIsNotConvertedIntoAnUnavailableFinding()
    {
        var scanner = new TestScanner("Slow", "System", true, async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Array.Empty<DiagnosticResult>();
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateEngine(scanner).RunAsync(cancellation.Token));
    }

    [Fact]
    public async Task NoRegisteredPluginProducesExplicitUnverifiedResultAndNoScore()
    {
        var report = await CreateEngine().RunAsync();

        Assert.Null(report.HealthScore);
        var result = Assert.Single(report.Results);
        Assert.Equal(DiagnosticStatus.NotVerified, result.Status);
        Assert.Contains("Nenhum scanner", result.Title);
    }

    private static DiagnosticEngine CreateEngine(params IDiagnosticScanner[] scanners) =>
        new(scanners, NullLogger<DiagnosticEngine>.Instance);

    private static DiagnosticResult RawResult(string title, string category, DiagnosticSeverity severity, DiagnosticStatus status) => new(
        "nome fornecido pelo plugin", category, severity, status, title, "Descrição", "Recomendação", "Evidência", TimeSpan.Zero, DateTimeOffset.MinValue);

    private static void UpdateMaximum(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private sealed class TestScanner(
        string name,
        string category,
        bool supportsParallelExecution,
        Func<CancellationToken, Task<IReadOnlyList<DiagnosticResult>>> scan) : IDiagnosticScanner
    {
        public string Name => name;
        public string Category => category;
        public bool SupportsParallelExecution => supportsParallelExecution;
        public Task<IReadOnlyList<DiagnosticResult>> ScanAsync(CancellationToken cancellationToken = default) => scan(cancellationToken);
    }
}

public sealed class DiagnosticScannerPluginTests
{
    [Fact]
    public async Task WindowsUpdatePluginReportsPendingRestartAndObservedErrorCode()
    {
        var source = new FakeWindowsDiagnosticDataSource
        {
            WindowsUpdate = new WindowsUpdateProbe(
                ProbeResult<IReadOnlyList<PendingWindowsUpdate>>.Available([new PendingWindowsUpdate("Security update", "KB-TEST")]),
                ProbeResult<bool>.Available(true),
                ProbeResult<IReadOnlyList<DiagnosticEvent>>.Available(
                [
                    new DiagnosticEvent(20, "WindowsUpdate", "WindowsUpdateClient", 2, DateTimeOffset.UtcNow, "Install failed with HRESULT 0x80070005"),
                    new DiagnosticEvent(100, "WindowsUpdate", "WindowsUpdateClient", 4, DateTimeOffset.UtcNow, "Information only")
                ]))
        };

        var results = await new WindowsUpdateDiagnosticScanner(source).ScanAsync();

        Assert.Contains(results, result => result.Status == DiagnosticStatus.Finding && result.Title.Contains("pendente"));
        Assert.Contains(results, result => result.Status == DiagnosticStatus.Finding && result.Title.Contains("Reinicialização"));
        var failure = Assert.Single(results, result => result.Title.Contains("ID 20", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("0x80070005", failure.Evidence);
        Assert.DoesNotContain(results, result => result.Title.Contains("evento 100", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ServicesPluginFindsStoppedCriticalServiceStartupMisconfigurationAndDependencies()
    {
        var source = new FakeWindowsDiagnosticDataSource
        {
            Services = ProbeResult<IReadOnlyList<ServiceDiagnosticInfo>>.Available(
            [
                new ServiceDiagnosticInfo("RpcSs", "Remote Procedure Call", "Stopped", "Disabled", []),
                new ServiceDiagnosticInfo("DcomLaunch", "DCOM Server Process Launcher", "Running", "Auto", ["MissingDependency"]),
                new ServiceDiagnosticInfo("EventLog", "Windows Event Log", "Running", "Auto", [])
            ])
        };

        var results = await new ServicesDiagnosticScanner(source).ScanAsync();

        Assert.Contains(results, result => result.Severity == DiagnosticSeverity.Critical && result.Title.Contains("parado"));
        Assert.Contains(results, result => result.Severity == DiagnosticSeverity.Critical && result.Title.Contains("Inicialização"));
        Assert.Contains(results, result => result.Title.Contains("Dependências") && result.Evidence.Contains("DcomLaunch"));
    }

    [Fact]
    public async Task DriversPluginReportsMissingAndCriticalErrorCodesAndUnknownDevices()
    {
        var source = new FakeWindowsDiagnosticDataSource
        {
            Devices = ProbeResult<IReadOnlyList<DeviceDiagnosticInfo>>.Available(
            [
                new DeviceDiagnosticInfo("Unknown device", "PCI\\VEN_TEST", 28, "Error"),
                new DeviceDiagnosticInfo("GPU", "PCI\\VEN_GPU", 43, "Error"),
                new DeviceDiagnosticInfo("Disabled device", "USB\\TEST", 22, "Error")
            ])
        };

        var results = await new DriversDiagnosticScanner(source).ScanAsync();

        Assert.Contains(results, result => result.Title.Contains("driver(s) ausente", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, result => result.Severity == DiagnosticSeverity.Critical && result.Title.Contains("erro crítico"));
        Assert.Contains(results, result => result.Title.Contains("desconhecido"));
        Assert.DoesNotContain(results, result => result.Title.Contains("Disabled device"));
    }

    [Fact]
    public async Task DiskPluginDistinguishesSmartFailureSpaceWarningAndMeasuredButUnclassifiedTemperature()
    {
        var source = new FakeWindowsDiagnosticDataSource
        {
            Disks = new DiskProbe(
                ProbeResult<IReadOnlyList<DiskSmartStatus>>.Available([new DiskSmartStatus("Disk0", true)]),
                ProbeResult<IReadOnlyList<DiskHealthInfo>>.Available([new DiskHealthInfo("Disk0", "Model", "OK"), new DiskHealthInfo("Disk1", "Unknown model", "Unknown")]),
                ProbeResult<IReadOnlyList<DiskVolumeInfo>>.Available([new DiskVolumeInfo("C:", 1_000, 50)]),
                ProbeResult<IReadOnlyList<DiskTemperatureInfo>>.Available([new DiskTemperatureInfo("Disk0", 42)]))
        };

        var results = await new DiskDiagnosticScanner(source).ScanAsync();

        Assert.Contains(results, result => result.Severity == DiagnosticSeverity.Critical && result.Title.Contains("SMART"));
        Assert.Contains(results, result => result.Severity == DiagnosticSeverity.Critical && result.Title.Contains("abaixo de 10%"));
        var temperature = Assert.Single(results, result => result.Title.Contains("Temperatura SMART observada"));
        Assert.Equal(DiagnosticStatus.NotVerified, temperature.Status);
        Assert.Contains("42 °C", temperature.Evidence);
        Assert.Contains(results, result => result.Status == DiagnosticStatus.NotVerified && result.Title.Contains("Saúde de disco"));
        Assert.DoesNotContain(results, result => result.Severity == DiagnosticSeverity.Critical && result.Title.Contains("estado crítico"));
    }

    [Fact]
    public async Task EventViewerPluginIncludesOnlyCriticalErrorAndWarningEventsAcrossRequestedLogs()
    {
        var source = new FakeWindowsDiagnosticDataSource
        {
            EventLogs = new EventLogProbe(
                ProbeResult<IReadOnlyList<DiagnosticEvent>>.Available(
                [
                    Event(1, "System", 1), Event(2, "System", 2), Event(3, "System", 3), Event(4, "System", 4)
                ]),
                ProbeResult<IReadOnlyList<DiagnosticEvent>>.Available([]),
                ProbeResult<IReadOnlyList<DiagnosticEvent>>.Available([]))
        };

        var results = await new EventViewerDiagnosticScanner(source).ScanAsync();
        var findings = results.Where(result => result.Status == DiagnosticStatus.Finding).ToArray();

        Assert.Equal(new[] { "evento 1", "evento 2", "evento 3" }, findings.Select(result => result.Title.Split('·').Last().Trim().ToLowerInvariant()));
        Assert.Equal(DiagnosticSeverity.Critical, findings[0].Severity);
        Assert.DoesNotContain(findings, result => result.Title.Contains("evento 4"));
        Assert.Equal(2, results.Count(result => result.Status == DiagnosticStatus.Healthy));
    }

    [Fact]
    public async Task UnsupportedPlatformReportsUnavailableForEveryPluginAndNeverClaimsHealth()
    {
        var source = new UnsupportedWindowsDiagnosticDataSource();
        IDiagnosticScanner[] scanners =
        [
            new WindowsUpdateDiagnosticScanner(source),
            new ServicesDiagnosticScanner(source),
            new DriversDiagnosticScanner(source),
            new DiskDiagnosticScanner(source),
            new EventViewerDiagnosticScanner(source)
        ];
        var report = await new DiagnosticEngine(scanners, NullLogger<DiagnosticEngine>.Instance).RunAsync();

        Assert.Null(report.HealthScore);
        Assert.Equal(12, report.UnavailableChecks);
        Assert.Equal(0, report.VerifiedChecks);
        Assert.All(report.Results, result => Assert.Equal(DiagnosticStatus.Unavailable, result.Status));
    }

    private static DiagnosticEvent Event(int id, string log, int level) => new(id, log, "Provider", level, DateTimeOffset.UtcNow, $"Event {id}");

    private sealed class FakeWindowsDiagnosticDataSource : IWindowsDiagnosticDataSource
    {
        private static readonly ProbeResult<IReadOnlyList<PendingWindowsUpdate>> UnavailableUpdates = ProbeResult<IReadOnlyList<PendingWindowsUpdate>>.Unavailable("Not configured");
        private static readonly ProbeResult<bool> UnavailableBoolean = ProbeResult<bool>.Unavailable("Not configured");
        private static readonly ProbeResult<IReadOnlyList<DiagnosticEvent>> EmptyEvents = ProbeResult<IReadOnlyList<DiagnosticEvent>>.Available([]);

        public WindowsUpdateProbe WindowsUpdate { get; init; } = new(UnavailableUpdates, UnavailableBoolean, EmptyEvents);
        public ProbeResult<IReadOnlyList<ServiceDiagnosticInfo>> Services { get; init; } = ProbeResult<IReadOnlyList<ServiceDiagnosticInfo>>.Unavailable("Not configured");
        public ProbeResult<IReadOnlyList<DeviceDiagnosticInfo>> Devices { get; init; } = ProbeResult<IReadOnlyList<DeviceDiagnosticInfo>>.Unavailable("Not configured");
        public DiskProbe Disks { get; init; } = new(
            ProbeResult<IReadOnlyList<DiskSmartStatus>>.Unavailable("Not configured"),
            ProbeResult<IReadOnlyList<DiskHealthInfo>>.Unavailable("Not configured"),
            ProbeResult<IReadOnlyList<DiskVolumeInfo>>.Unavailable("Not configured"),
            ProbeResult<IReadOnlyList<DiskTemperatureInfo>>.Unavailable("Not configured"));
        public EventLogProbe EventLogs { get; init; } = new(EmptyEvents, EmptyEvents, EmptyEvents);

        public Task<WindowsUpdateProbe> ReadWindowsUpdateAsync(CancellationToken cancellationToken = default) => Task.FromResult(WindowsUpdate);
        public Task<ProbeResult<IReadOnlyList<ServiceDiagnosticInfo>>> ReadServicesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Services);
        public Task<ProbeResult<IReadOnlyList<DeviceDiagnosticInfo>>> ReadDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Devices);
        public Task<DiskProbe> ReadDisksAsync(CancellationToken cancellationToken = default) => Task.FromResult(Disks);
        public Task<EventLogProbe> ReadEventLogsAsync(CancellationToken cancellationToken = default) => Task.FromResult(EventLogs);
    }
}

public sealed class DiagnosticReportFormatterTests
{
    [Fact]
    public void FormatsScoreCoverageAndEvidenceWithoutInventingAHealthScore()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new DiagnosticResult("Drivers", "Drivers", DiagnosticSeverity.Information, DiagnosticStatus.Unavailable,
            "Drivers indisponível", "Windows não disponível", "Execute em Windows", "Nenhuma evidência coletada", TimeSpan.FromMilliseconds(5), now);
        var report = new DiagnosticReport([result], now, now, TimeSpan.FromMilliseconds(10), null);

        var text = new DiagnosticReportFormatter().Format(report);

        Assert.Contains("não calculado", text);
        Assert.Contains("Indisponíveis: 1", text);
        Assert.Contains("Nenhuma evidência coletada", text);
        Assert.Contains("Drivers indisponível", text);
    }
}
