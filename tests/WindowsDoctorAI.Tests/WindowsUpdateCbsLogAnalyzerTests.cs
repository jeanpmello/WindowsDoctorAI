using System.Globalization;
using System.Text.Json;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class WindowsUpdateCbsLogAnalyzerTests
{
    private const string ValidPackage = "Package_123_for_KB3192392~31bf3856ad364e35~amd64~~6.3.1.4";
    private const string AnotherPackage = "Package_456_for_KB3192393~31bf3856ad364e35~amd64~~6.3.1.4";
    private const string PrivateHost = "CBS-PRIVATE-HOST-938";
    private const string PrivatePath = @"C:\Users\private.user\CBS.log";
    private const string OperationalChannel = WindowsUpdateCbsLogAnalyzer.WindowsUpdateOperationalChannel;
    private static readonly DateTimeOffset EventTimestamp = new(2026, 10, 2, 19, 40, 10, TimeSpan.Zero);

    [Fact]
    public void SameLocalSecondProducesGenericSignalWithoutPackageIdentityOrRawText()
    {
        var cbsText = CbsLine(EventTimestamp,
                $"Info CBS Store corruption, manifest missing for package: {ValidPackage}")
            + $"Info CBS unrelated private data {PrivatePath}; host={PrivateHost}; password=raw-cbs-secret\n";
        var updateEvent = Event($"Update failed with HRESULT 0x800F0831; path={PrivatePath}; host={PrivateHost}; token=raw-event-secret");

        var result = Assert.IsType<DiagnosticResult>(new WindowsUpdateCbsLogAnalyzer().Analyze(cbsText, updateEvent));

        Assert.Equal(DiagnosticStatus.Finding, result.Status);
        Assert.Equal("WindowsUpdateClient", result.SourceMetadata?.Provider);
        Assert.Equal(new CbsPackageEvidence(CbsEvidenceType.ManifestMissing), result.CbsEvidence);
        Assert.Null(result.CbsEvidence?.PackageIdentity);
        Assert.Contains("mesmo segundo local", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não prova", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("identidade do pacote não atribuída", result.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ValidPackage, Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("CBS-PRIVATE-HOST-938", Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePath, Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("raw-cbs-secret", Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("raw-event-secret", Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("Store corruption, manifest missing", Serialize(result), StringComparison.Ordinal);

        var report = new DiagnosticReport([result], EventTimestamp, EventTimestamp, TimeSpan.Zero, null);
        var redacted = Assert.Single(DiagnosticPrivacyRedactor.RedactReport(report)!.Results);
        Assert.Equal(new CbsPackageEvidence(CbsEvidenceType.ManifestMissing), redacted.CbsEvidence);
        Assert.DoesNotContain(PrivatePath, Serialize(redacted), StringComparison.Ordinal);
    }

    [Fact]
    public void FailedToResolveMarkerAtSameSecondProducesOnlyGenericTypedSignal()
    {
        const string microsoftPackage = "Microsoft-Windows-Client-LanguagePack-Package~31bf3856ad364e35~amd64~en-US~10.0.22621.1";
        var cbsText = CbsLine(EventTimestamp,
            $"Error CBS Failed to resolve package '{microsoftPackage}' [HRESULT = 0x800f0831 - CBS_E_STORE_CORRUPTION]");

        var result = new WindowsUpdateCbsLogAnalyzer().Analyze(cbsText,
            Event("Installation failed with 0x800f0831."));

        Assert.NotNull(result);
        Assert.Equal(new CbsPackageEvidence(CbsEvidenceType.FailedToResolvePackage), result.CbsEvidence);
        Assert.Null(result.CbsEvidence?.PackageIdentity);
        Assert.DoesNotContain(microsoftPackage, Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void FractionalCbsTimestampAndSequenceNumberAreAcceptedAsSameSecondMetadata()
    {
        var cbsText = $"{TimestampText(EventTimestamp)}.1234567 42, Info CBS Store corruption, manifest missing for package: {ValidPackage}\n";

        var result = new WindowsUpdateCbsLogAnalyzer().Analyze(cbsText,
            Event("Update failed with HRESULT 0x800F0831."));

        Assert.NotNull(result);
        Assert.Equal(new CbsPackageEvidence(CbsEvidenceType.ManifestMissing), result.CbsEvidence);
        Assert.DoesNotContain(ValidPackage, Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingEventTimestampHresultOrRecognizedMarkerDoesNotProduceFinding()
    {
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var validEvent = Event("Update failed with HRESULT 0x800F0831.");

        Assert.Null(analyzer.Analyze(CbsLine(EventTimestamp, $"Info CBS Store corruption, manifest missing for package: {ValidPackage}"),
            validEvent with { Timestamp = null }));
        Assert.Null(analyzer.AnalyzeWithCurrentRunEvent(
            CbsLine(EventTimestamp, $"Info CBS Store corruption, manifest missing for package: {ValidPackage}"),
            new WindowsUpdateEventEvidence(OperationalChannel, WindowsUpdateEventEvidence.CbsStoreCorruptionHresult)));
        Assert.Null(analyzer.Analyze("Info CBS unrelated component-store message\n", validEvent));
        Assert.Null(analyzer.Analyze(CbsLine(EventTimestamp, $"Info CBS Store corruption, manifest missing for package: {ValidPackage}"), null));
    }

    [Fact]
    public void WrongProviderChannelHresultOrUnrelatedTimestampDoesNotProduceFinding()
    {
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var marker = CbsLine(EventTimestamp, $"Info CBS Store corruption, manifest missing for package: {ValidPackage}");
        var updateEvent = Event("Update failed with 0x800F0831.");

        Assert.Null(analyzer.Analyze(marker, Event("Update failed with 0x800F0831.", provider: "OtherProvider")));
        Assert.Null(analyzer.Analyze(marker, Event("Update failed with 0x800F0831.", channel: "System")));
        Assert.Null(analyzer.Analyze(marker, Event("Update failed with 0x800F08310.")));
        Assert.Null(analyzer.Analyze(marker, Event("Update failed with 0x800F0831_suffix.")));
        Assert.Null(analyzer.Analyze(CbsLine(EventTimestamp.AddSeconds(1),
            $"Info CBS Store corruption, manifest missing for package: {AnotherPackage}"), updateEvent));
    }

    [Fact]
    public void AnotherPackageAtUnrelatedTimeOrMultiplePackageIdentitiesDoNotProduceFinding()
    {
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var updateEvent = Event("Update failed with 0x800F0831.");
        var otherPackageAtUnrelatedTime = CbsLine(EventTimestamp.AddMinutes(4),
            $"Info CBS Store corruption, manifest missing for package: {AnotherPackage}");
        var ambiguous = CbsLine(EventTimestamp,
                $"Info CBS Store corruption, manifest missing for package: {ValidPackage}")
            + CbsLine(EventTimestamp.AddMinutes(4),
                $"Info CBS Store corruption, manifest missing for package: {AnotherPackage}");

        Assert.Null(analyzer.Analyze(otherPackageAtUnrelatedTime, updateEvent));
        Assert.Null(analyzer.Analyze(ambiguous, updateEvent));
    }

    [Fact]
    public void InvalidPackageIdentityIsRejectedAndNoIdentityIsReturned()
    {
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var updateEvent = Event("Update failed with 0x800F0831.");
        var invalidIdentity = CbsLine(EventTimestamp,
            $"Info CBS Store corruption, manifest missing for package: {PrivatePath}");
        var unsupportedIdentity = CbsLine(EventTimestamp,
            "Info CBS Store corruption, manifest missing for package: Package_Custom~31bf3856ad364e35~amd64~~1.2.3.4");

        Assert.False(CbsPackageIdentityValidator.IsValid(PrivatePath));
        Assert.False(CbsPackageIdentityValidator.IsValid("Package_Custom~31bf3856ad364e35~amd64~~1.2.3.4"));
        Assert.Null(analyzer.Analyze(invalidIdentity, updateEvent));
        Assert.Null(analyzer.Analyze(unsupportedIdentity, updateEvent));
    }

    [Fact]
    public void UnicodeLineSeparatorCannotStandInForHorizontalBoundaryWhitespace()
    {
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var updateEvent = Event("Update failed with 0x800F0831.");
        var payload = $"{TimestampText(EventTimestamp)}, Info CBS Store corruption, manifest missing for package:\u2028{ValidPackage}\n"
            + $"{TimestampText(EventTimestamp)}, Info\u2028CBS Store corruption, manifest missing for package: {ValidPackage}\n";

        Assert.Null(analyzer.Analyze(payload, updateEvent));
    }

    [Fact]
    public void TruncatedMalformedOversizedOrControlCharacterInputIsRejected()
    {
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var updateEvent = Event("Update failed with 0x800F0831.");
        var completeMarker = $"{TimestampText(EventTimestamp)}, Info CBS Store corruption, manifest missing for package: {ValidPackage}";

        Assert.Null(analyzer.Analyze(completeMarker, updateEvent));
        Assert.Null(analyzer.Analyze(completeMarker + " partial\n", updateEvent));
        Assert.Null(analyzer.Analyze(CbsLine(EventTimestamp,
            $"Error CBS Failed to resolve package '{ValidPackage}' [HRESULT = 0x800F0831 - CBS_E_STORE_CORRUPTION"), updateEvent));
        Assert.Null(analyzer.Analyze(completeMarker + "\0\n", updateEvent));
        Assert.Null(analyzer.Analyze(new string('x', 2 * 1024 * 1024 + 1), updateEvent));
    }

    private static DiagnosticEvent Event(string message, string provider = "Microsoft-Windows-WindowsUpdateClient",
        string channel = OperationalChannel, DateTimeOffset? timestamp = null) =>
        new(20, channel, provider, 2, timestamp ?? EventTimestamp, message);

    private static string CbsLine(DateTimeOffset timestamp, string content) => $"{TimestampText(timestamp)}, {content}\n";

    private static string TimestampText(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Serialize(DiagnosticResult result) => JsonSerializer.Serialize(result);
}
