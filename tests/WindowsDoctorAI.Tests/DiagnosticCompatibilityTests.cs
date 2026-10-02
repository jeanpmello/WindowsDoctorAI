using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class DiagnosticCompatibilityTests
{
    [Fact]
    public void LegacyMilestoneOneRunLoadsInventoryWithoutTreatingOldDemoScoreAsDiagnosticReport()
    {
        var now = DateTimeOffset.UtcNow;
        var legacyPayload = new
        {
            Id = Guid.NewGuid(),
            StartedAtUtc = now.AddSeconds(-1),
            CompletedAtUtc = now,
            Duration = TimeSpan.FromSeconds(1),
            Inventory = new ComputerInventory { ComputerName = "LEGACY-PC" },
            HealthScore = new { Value = 95 }
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(legacyPayload, options);

        var restored = JsonSerializer.Deserialize<DiagnosticRun>(json, options);

        Assert.NotNull(restored);
        Assert.Equal("LEGACY-PC", restored.Inventory.ComputerName);
        Assert.Null(restored.Report);
    }

    [Fact]
    public void DiagnosticRegistrationExposesEveryScannerPluginThroughTheCommonContract()
    {
        var services = new ServiceCollection();
        services.AddWindowsDiagnosticPlugins();
        using var provider = services.BuildServiceProvider();

        var scanners = provider.GetServices<IDiagnosticScanner>().ToArray();

        Assert.Equal(
            ["Windows Update", "Services", "Drivers", "Disk", "Event Viewer"],
            scanners.Select(scanner => scanner.Name));
        Assert.All(scanners, scanner => Assert.True(scanner.SupportsParallelExecution));
    }
}
