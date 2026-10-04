using Microsoft.Extensions.Logging.Abstractions;
using WindowsDoctorAI.App;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Reporting;

namespace WindowsDoctorAI.Tests;

public sealed class HomeInventoryPrivacyTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewAndHistoricalRoutesRedactInventoryUiAndPreserveOriginalSupportData(bool loadFromHistory)
    {
        const string host = "PRIVATE-HOST-UI-726";
        const string deviceSerial = "PRIVATE-DEVICE-SERIAL-726";
        const string volumeName = "PRIVATE-VOLUME-NAME-726";
        const string volumeLabel = "PRIVATE-VOLUME-LABEL-726";
        const string biosSerial = "PRIVATE-BIOS-SERIAL-726";
        const string user = "PRIVATE-USER-726";
        const string domain = "PRIVATE-DOMAIN-726";
        const string ipv4 = "198.51.100.27";
        const string ipv6 = "2001:db8::726";
        const string adapterName = "PRIVATE-ADAPTER-NAME-726";
        const string adapterDescription = "PRIVATE-ADAPTER-DESCRIPTION-726";
        const string rawSupportMessage = "Conta PRIVATE-USER-726 falhou; HRESULT 0x80073712.";
        var privateIdentifiers = new[]
        {
            host, deviceSerial, volumeName, volumeLabel, biosSerial, user, domain,
            ipv4, ipv6, adapterName, adapterDescription
        };
        var inventory = new ComputerInventory
        {
            ComputerName = host,
            SerialNumber = deviceSerial,
            Disks = [new DiskVolume(volumeName, volumeLabel, "NTFS", 100_000_000_000, 50_000_000_000)],
            Bios = new BiosDetails("Private Vendor", "1.0", biosSerial),
            UserName = user,
            Domain = domain,
            IPv4Addresses = [ipv4],
            IPv6Addresses = [ipv6],
            NetworkAdapters = [new NetworkAdapterDetails(adapterName, adapterDescription, "Up", [ipv4], [ipv6])]
        };
        var result = new DiagnosticResult(
            "Event Viewer", "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
            "Warning · Provider · evento 7001", rawSupportMessage,
            "Revise o evento na fonte do Windows.",
            $"Log=System; ID=7001; nível=Warning; evidência {rawSupportMessage}",
            TimeSpan.FromSeconds(1), FixedNow);
        var report = new DiagnosticReport([result], FixedNow, FixedNow, TimeSpan.FromSeconds(1), new HealthScore(80));
        var history = new TestDiagnosticRunRepository();
        var settings = new TestUserSettingsRepository(new UserSettings { SaveDiagnosticHistory = true });
        var knowledge = new TestKnowledgeRepository();
        var useCase = new RunComputerInventoryDiagnosticUseCase(
            new TestInventoryScanner(inventory), new TestDiagnosticEngine(report), history, settings,
            NullLogger<RunComputerInventoryDiagnosticUseCase>.Instance);
        var assessment = new DiagnosticAssessmentService(
            knowledge, new RecommendationEngine(), new RootCauseAnalyzer(), new HtmlDiagnosticReportFormatter());
        var viewModel = new HomeViewModel(
            useCase, history, knowledge, new KnowledgeJsonImporter(knowledge), assessment,
            new CbsLogImportService(new NoCbsLogPicker(), new CbsLogMarkerClassifier()),
            new UnsupportedBackupSetCatalogSource(),
            NullLogger<HomeViewModel>.Instance);

        DiagnosticRun originalRun;
        if (loadFromHistory)
        {
            originalRun = new DiagnosticRun(Guid.NewGuid(), FixedNow, FixedNow, TimeSpan.FromSeconds(1), inventory, report);
            history.LatestRun = originalRun;
            await viewModel.LoadLatestAsync();
        }
        else
        {
            await viewModel.StartDiagnosticCommand.ExecuteAsync(null);
            originalRun = Assert.IsType<DiagnosticRun>(history.SavedRun);
        }

        var displayedInventory = string.Join(Environment.NewLine,
            viewModel.ComputerName, viewModel.ManufacturerModel, viewModel.SerialNumber, viewModel.Disks,
            viewModel.Bios, viewModel.UserAndDomain, viewModel.IPv4, viewModel.IPv6,
            viewModel.NetworkAdapters, viewModel.FindingsSummary);
        foreach (var identifier in privateIdentifiers)
            Assert.DoesNotContain(identifier, displayedInventory, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("[redigido]", viewModel.ComputerName);
        Assert.Equal("[redigido]", viewModel.SerialNumber);
        Assert.Contains("[redigido]", viewModel.Disks, StringComparison.Ordinal);
        Assert.Contains("[redigido]", viewModel.Bios, StringComparison.Ordinal);
        Assert.Contains("[redigido]", viewModel.UserAndDomain, StringComparison.Ordinal);
        Assert.Equal("Nenhum endereço encontrado", viewModel.IPv4);
        Assert.Equal("Nenhum endereço encontrado", viewModel.IPv6);
        Assert.Contains("[redigido]", viewModel.NetworkAdapters, StringComparison.Ordinal);
        Assert.Contains("0x80073712", viewModel.FindingsSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ID=7001", viewModel.FindingsSummary, StringComparison.Ordinal);
        Assert.Contains("Log=System", viewModel.FindingsSummary, StringComparison.Ordinal);

        Assert.Same(inventory, originalRun.Inventory);
        Assert.Same(report, originalRun.Report);
        Assert.Equal(host, originalRun.Inventory.ComputerName);
        Assert.Equal(deviceSerial, originalRun.Inventory.SerialNumber);
        Assert.Equal(biosSerial, originalRun.Inventory.Bios.SerialNumber);
        Assert.Equal(ipv4, Assert.Single(originalRun.Inventory.IPv4Addresses));
        Assert.Equal(ipv6, Assert.Single(originalRun.Inventory.IPv6Addresses));
        Assert.Equal(rawSupportMessage, Assert.Single(originalRun.Report!.Results).Description);
    }

    private sealed class TestInventoryScanner(ComputerInventory inventory) : IComputerInventoryScanner
    {
        public Task<ComputerInventory> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult(inventory);
    }

    private sealed class TestDiagnosticEngine(DiagnosticReport report) : IDiagnosticEngine
    {
        public Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(report);
    }

    private sealed class TestDiagnosticRunRepository : IDiagnosticRunRepository
    {
        public DiagnosticRun? LatestRun { get; set; }
        public DiagnosticRun? SavedRun { get; private set; }

        public Task SaveAsync(DiagnosticRun run, CancellationToken cancellationToken = default)
        {
            SavedRun = run;
            return Task.CompletedTask;
        }

        public Task<DiagnosticRun?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromResult(LatestRun);
        public Task<int> DeleteCompletedBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class TestUserSettingsRepository(UserSettings settings) : IUserSettingsRepository
    {
        public Task<UserSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
        public Task SaveAsync(UserSettings updated, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestKnowledgeRepository : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeRule>> GetLatestRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeRule>>(Array.Empty<KnowledgeRule>());

        public Task SaveImportAsync(KnowledgePackage package, string sha256, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoCbsLogPicker : ICbsLogFilePicker
    {
        public Task<Stream?> PickCbsLogAsync(CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
    }
}
