using System.Text.Json;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class WindowsUpdateCbsLogAnalyzerTests
{
    private const string ValidPackage = "Package_123_for_KB3192392~31bf3856ad364e35~amd64~~6.3.1.4";
    private const string PrivateHost = "CBS-PRIVATE-HOST-938";
    private const string PrivatePath = @"C:\Users\private.user\CBS.log";
    private const string OperationalChannel = WindowsUpdateCbsLogAnalyzer.WindowsUpdateOperationalChannel;

    [Fact]
    public void ManifestMissingWithExactWindowsUpdateEventProducesOnlyTypedMinimizedEvidence()
    {
        var cbsText = $"2026-10-02 19:40:10, Info CBS Store corruption, manifest missing for package: {ValidPackage}\n"
            + $"Info CBS unrelated private data {PrivatePath}; host={PrivateHost}; password=raw-cbs-secret\n";
        var updateEvent = Event($"Update failed with HRESULT 0x800F0831; path={PrivatePath}; host={PrivateHost}; token=raw-event-secret");

        var result = Assert.IsType<DiagnosticResult>(new WindowsUpdateCbsLogAnalyzer().Analyze(cbsText, updateEvent));

        Assert.Equal(DiagnosticStatus.Finding, result.Status);
        Assert.Equal("WindowsUpdateClient", result.SourceMetadata?.Provider);
        Assert.Equal(new CbsPackageEvidence(CbsEvidenceType.ManifestMissing, ValidPackage), result.CbsEvidence);
        Assert.Equal($"CBS marker=ManifestMissing; package identity={ValidPackage}", result.Evidence);
        Assert.Contains("0x800F0831", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CBS-PRIVATE-HOST-938", Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePath, Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("raw-cbs-secret", Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("raw-event-secret", Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("Store corruption, manifest missing", Serialize(result), StringComparison.Ordinal);

        var report = new DiagnosticReport([result], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TimeSpan.Zero, null);
        var redacted = Assert.Single(DiagnosticPrivacyRedactor.RedactReport(report)!.Results);
        Assert.Equal(new CbsPackageEvidence(CbsEvidenceType.ManifestMissing, ValidPackage), redacted.CbsEvidence);
        Assert.DoesNotContain(PrivatePath, Serialize(redacted), StringComparison.Ordinal);
    }

    [Fact]
    public void FailedToResolvePackageWithExactCbsHresultAndWindowsUpdateEventIsRecognized()
    {
        const string microsoftPackage = "Microsoft-Windows-Client-LanguagePack-Package~31bf3856ad364e35~amd64~en-US~10.0.22621.1";
        var cbsText = $"Error CBS Failed to resolve package '{microsoftPackage}' [HRESULT = 0x800f0831 - CBS_E_STORE_CORRUPTION]\n";

        var result = new WindowsUpdateCbsLogAnalyzer().Analyze(cbsText, Event("Installation failed with 0x800f0831."));

        Assert.NotNull(result);
        Assert.Equal(new CbsPackageEvidence(CbsEvidenceType.FailedToResolvePackage, microsoftPackage), result.CbsEvidence);
        Assert.Equal($"CBS marker=FailedToResolvePackage; package identity={microsoftPackage}", result.Evidence);
    }

    [Fact]
    public void HresultAloneOrCbsMarkerWithoutMatchingWindowsUpdateEventDoesNotProduceFinding()
    {
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var updateEvent = Event("Update failed with HRESULT 0x800F0831.");

        Assert.Null(analyzer.Analyze("Info CBS unrelated component-store message\n", updateEvent));
        Assert.Null(analyzer.Analyze($"Info CBS Store corruption, manifest missing for package: {ValidPackage}\n", null));
    }

    [Fact]
    public void WrongProviderWrongChannelOrNonExactHresultDoesNotProduceFinding()
    {
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var cbsText = $"Info CBS Store corruption, manifest missing for package: {ValidPackage}\n";

        Assert.Null(analyzer.Analyze(cbsText, Event("Update failed with 0x800F0831.", provider: "OtherProvider")));
        Assert.Null(analyzer.Analyze(cbsText, Event("Update failed with 0x800F0831.", channel: "System")));
        Assert.Null(analyzer.Analyze(cbsText, Event("Update failed with 0x800F08310.")));
        Assert.Null(analyzer.Analyze(cbsText, Event("Update failed with 0x800F0831_suffix.")));
    }

    [Fact]
    public void InvalidPackageIdentityAndConflictingPackagesAreRejected()
    {
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var updateEvent = Event("Update failed with 0x800F0831.");
        var invalidIdentity = $"Info CBS Store corruption, manifest missing for package: {PrivatePath}\n";
        var unsupportedIdentity = "Info CBS Store corruption, manifest missing for package: Package_Custom~31bf3856ad364e35~amd64~~1.2.3.4\n";
        var anotherPackage = "Package_456_for_KB3192392~31bf3856ad364e35~amd64~~6.3.1.4";
        var conflicting = $"Info CBS Store corruption, manifest missing for package: {ValidPackage}\nInfo CBS Store corruption, manifest missing for package: {anotherPackage}\n";

        Assert.False(CbsPackageIdentityValidator.IsValid(PrivatePath));
        Assert.False(CbsPackageIdentityValidator.IsValid("Package_Custom~31bf3856ad364e35~amd64~~1.2.3.4"));
        Assert.Null(analyzer.Analyze(invalidIdentity, updateEvent));
        Assert.Null(analyzer.Analyze(unsupportedIdentity, updateEvent));
        Assert.Null(analyzer.Analyze(conflicting, updateEvent));
    }

    [Fact]
    public void TruncatedMalformedOversizedOrControlCharacterInputIsRejected()
    {
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var updateEvent = Event("Update failed with 0x800F0831.");
        var completeMarker = $"Info CBS Store corruption, manifest missing for package: {ValidPackage}";

        Assert.Null(analyzer.Analyze(completeMarker, updateEvent));
        Assert.Null(analyzer.Analyze(completeMarker + " partial\n", updateEvent));
        Assert.Null(analyzer.Analyze($"Error CBS Failed to resolve package '{ValidPackage}' [HRESULT = 0x800F0831 - CBS_E_STORE_CORRUPTION\n", updateEvent));
        Assert.Null(analyzer.Analyze(completeMarker + "\0\n", updateEvent));
        Assert.Null(analyzer.Analyze(new string('x', 2 * 1024 * 1024 + 1), updateEvent));
    }

    private static DiagnosticEvent Event(string message, string provider = "Microsoft-Windows-WindowsUpdateClient", string channel = OperationalChannel) =>
        new(20, channel, provider, 2, DateTimeOffset.UtcNow, message);

    private static string Serialize(DiagnosticResult result) => JsonSerializer.Serialize(result);
}
