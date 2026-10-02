using System.Text.Json;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class CbsLogMarkerClassifierTests
{
    private const string FirstPackage = "Package_123_for_KB3192392~31bf3856ad364e35~amd64~~6.3.1.4";
    private const string SecondPackage = "Microsoft-Windows-Client-LanguagePack-Package~31bf3856ad364e35~amd64~en-US~10.0.22621.1";
    private const string PrivatePath = @"C:\Users\private.user\CBS.log";
    private const string PrivateHost = "CBS-PRIVATE-HOST-938";

    [Fact]
    public void MultipleRecognizedMarkersReturnOnlyGenericTypesAndNeverPackageNames()
    {
        var cbsText = "2026-10-02 19:40:10, Info CBS Store corruption, manifest missing for package: " + FirstPackage + "\r\n"
            + "2026-10-02 19:40:10.1234567 42, Error CBS Failed to resolve package '" + SecondPackage
            + "' [HRESULT = 0x800F0831 - CBS_E_STORE_CORRUPTION]\r\n"
            + $"Info CBS unrelated private data {PrivatePath}; host={PrivateHost}; password=raw-cbs-secret\r\n";

        var markerTypes = new CbsLogMarkerClassifier().Classify(cbsText);
        var serialized = JsonSerializer.Serialize(markerTypes);

        Assert.Equal([CbsMarkerType.ManifestMissing, CbsMarkerType.FailedToResolvePackage], markerTypes);
        Assert.DoesNotContain(FirstPackage, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(SecondPackage, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePath, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateHost, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-cbs-secret", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkerWithoutTimestampIsAnOfflineObservationWithoutAnyEventOrFinding()
    {
        const string text = "Info CBS Store corruption, manifest missing for package: arbitrary-package-text\n";

        var markerTypes = new CbsLogMarkerClassifier().Classify(text);

        Assert.Equal([CbsMarkerType.ManifestMissing], markerTypes);
        Assert.DoesNotContain(typeof(CbsLogImportOutcome).GetProperties(), property => property.Name == "Result");
    }

    [Fact]
    public void OldDstLikeAndSameSecondMarkersAreClassifiedWithoutTemporalFiltering()
    {
        const string oldMarker = "1999-01-01 00:00:00, Info CBS Store corruption, manifest missing for package: old-package\n";
        const string firstAmbiguousLocalTime = "2026-11-01 01:30:00, Info CBS Store corruption, manifest missing for package: first-package\n";
        const string repeatedLocalTime = "2026-11-01 01:30:00, Info CBS Store corruption, manifest missing for package: repeated-hour-package\n";
        const string unrelatedSameSecond = "2026-11-01 01:30:00, Info CBS Store corruption, manifest missing for package: unrelated-package\n";

        var markerTypes = new CbsLogMarkerClassifier().Classify(
            oldMarker + firstAmbiguousLocalTime + repeatedLocalTime + unrelatedSameSecond);

        Assert.Equal([CbsMarkerType.ManifestMissing], markerTypes);
        Assert.DoesNotContain("old-package", JsonSerializer.Serialize(markerTypes), StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated-package", JsonSerializer.Serialize(markerTypes), StringComparison.Ordinal);
    }

    [Fact]
    public void HresultOrUnrelatedLineWithoutRecognizedCbsMarkerReturnsNoObservation()
    {
        var classifier = new CbsLogMarkerClassifier();

        Assert.Empty(classifier.Classify("Windows Update event contains 0x800F0831\n"));
        Assert.Empty(classifier.Classify("Info CBS unrelated component-store message\n"));
        Assert.Empty(classifier.Classify("Error CBS Failed to resolve package 'pkg' [HRESULT = 0x800F0831 - OTHER]\n"));
    }

    [Fact]
    public void TruncatedMalformedOversizedOrControlCharacterInputReturnsNoMarker()
    {
        var classifier = new CbsLogMarkerClassifier();
        var completeMarker = "2026-10-02 19:40:10, Info CBS Store corruption, manifest missing for package: pkg";

        Assert.Empty(classifier.Classify(completeMarker));
        Assert.Empty(classifier.Classify(completeMarker + " partial\n"));
        Assert.Empty(classifier.Classify(completeMarker + "\0\n"));
        Assert.Empty(classifier.Classify(new string('x', CbsLogMarkerClassifier.MaximumInputCharacters + 1)));
        Assert.Empty(classifier.Classify("2026-10-02 19:40:10, Info CBS Store corruption, manifest missing for package: "
            + new string('x', 4097) + "\n"));
    }
}
