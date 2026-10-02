using System.Text;
using System.Text.Json;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class CbsLogImportServiceTests
{
    private const string PackageIdentity = "Package_123_for_KB3192392~31bf3856ad364e35~amd64~~6.3.1.4";
    private const string PrivatePath = @"C:\Users\private.user\CBS.log";
    private const string PrivateHost = "CBS-PRIVATE-HOST-938";
    private const string PrivateUser = "private.user@example.invalid";
    private const string PrivateToken = "raw-cbs-secret-938";

    [Fact]
    public async Task CancelledPickerReturnsSafeCancelledState()
    {
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(null));

        var outcome = await Service(picker).ImportAndAnalyzeAsync();

        Assert.Equal(CbsLogImportStatus.Cancelled, outcome.Status);
        Assert.Null(outcome.MarkerTypes);
        Assert.Equal(1, picker.Calls);
    }

    [Fact]
    public async Task IoErrorDoesNotExposeExceptionPathOrMessage()
    {
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new ThrowingReadStream(PrivatePath)));
        var outcome = await Service(picker).ImportAndAnalyzeAsync();
        var serialized = JsonSerializer.Serialize(outcome);

        Assert.Equal(CbsLogImportStatus.FileReadFailed, outcome.Status);
        Assert.DoesNotContain(PrivatePath, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("private.user", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingFileDuringPickerOpenReturnsGenericReadFailure()
    {
        var picker = new FakePicker(_ => Task.FromException<Stream?>(new FileNotFoundException(PrivatePath)));

        var outcome = await Service(picker).ImportAndAnalyzeAsync();

        Assert.Equal(CbsLogImportStatus.FileReadFailed, outcome.Status);
        Assert.DoesNotContain(PrivatePath, JsonSerializer.Serialize(outcome), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileOverMaximumIsRejectedBeforeClassifierRuns()
    {
        var classifier = new CountingClassifier();
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new MemoryStream(new byte[CbsLogImportService.MaximumFileBytes + 1])));

        var outcome = await new CbsLogImportService(picker, classifier).ImportAndAnalyzeAsync();

        Assert.Equal(CbsLogImportStatus.FileTooLarge, outcome.Status);
        Assert.Equal(0, classifier.Calls);
    }

    [Fact]
    public async Task UnexpectedEncodingIsRejectedWithoutReturningRawContent()
    {
        var unsupported = new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00 };
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new MemoryStream(unsupported)));

        var outcome = await Service(picker).ImportAndAnalyzeAsync();

        Assert.Equal(CbsLogImportStatus.UnsupportedEncoding, outcome.Status);
        Assert.Null(outcome.MarkerTypes);
    }

    [Fact]
    public async Task MarkerWithoutAnyEventIsAStandaloneOfflineObservation()
    {
        var cbsText = "1999-01-01 00:00:00, Info CBS Store corruption, manifest missing for package: " + PackageIdentity + "\r\n"
            + "2026-11-01 01:30:00, Error CBS Failed to resolve package 'unrelated-package' [HRESULT = 0x800F0831 - CBS_E_STORE_CORRUPTION]\r\n"
            + $"Private {PrivatePath}; host={PrivateHost}; user={PrivateUser}; token={PrivateToken}\r\n";
        var picker = PickerFor(cbsText);

        var outcome = await Service(picker).ImportAndAnalyzeAsync();
        var serialized = JsonSerializer.Serialize(outcome);

        Assert.Equal(CbsLogImportStatus.ObservationAvailable, outcome.Status);
        Assert.Equal([CbsMarkerType.ManifestMissing, CbsMarkerType.FailedToResolvePackage], outcome.MarkerTypes);
        Assert.DoesNotContain(PackageIdentity, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated-package", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePath, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateHost, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateUser, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PrivateToken, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Store corruption, manifest missing", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("0x800F0831", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SameSecondMarkerPairProducesOnlyNonDiagnosticTypesAndNoRecommendation()
    {
        const string sameSecondCbs = "2026-10-02 20:00:00, Info CBS Store corruption, manifest missing for package: " + PackageIdentity + "\n";
        var outcome = await Service(PickerFor(sameSecondCbs)).ImportAndAnalyzeAsync();
        var diagnosticProperties = typeof(CbsLogImportOutcome).GetProperties()
            .Where(property => typeof(DiagnosticResult).IsAssignableFrom(property.PropertyType));

        Assert.Equal(CbsLogImportStatus.ObservationAvailable, outcome.Status);
        Assert.Empty(diagnosticProperties);
        Assert.DoesNotContain("Result", typeof(CbsLogImportOutcome).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("Recommendation", typeof(CbsLogImportOutcome).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("Finding", Enum.GetNames<CbsLogImportStatus>());
        Assert.DoesNotContain("0x800F0831", JsonSerializer.Serialize(outcome), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HresultWithoutRecognizedCbsMarkerProducesNoObservation()
    {
        var picker = PickerFor("WindowsUpdateClient event failed with HRESULT 0x800F0831\r\n");

        var outcome = await Service(picker).ImportAndAnalyzeAsync();

        Assert.Equal(CbsLogImportStatus.NoRecognizedMarker, outcome.Status);
        Assert.Null(outcome.MarkerTypes);
    }

    [Fact]
    public async Task OrdinaryUnrelatedSameSecondLinesDoNotCreateAnObservation()
    {
        var picker = PickerFor("2026-10-02 20:00:00, Info CBS unrelated private data\r\n"
            + "2026-10-02 20:00:00, Error Windows Update event 0x800F0831\r\n");

        var outcome = await Service(picker).ImportAndAnalyzeAsync();

        Assert.Equal(CbsLogImportStatus.NoRecognizedMarker, outcome.Status);
        Assert.Null(outcome.MarkerTypes);
    }

    private static CbsLogImportService Service(FakePicker picker) => new(picker, new CbsLogMarkerClassifier());

    private static FakePicker PickerFor(string text) => new(_ => Task.FromResult<Stream?>(
        new MemoryStream(Encoding.UTF8.GetBytes(text))));

    private sealed class FakePicker(Func<CancellationToken, Task<Stream?>> pick) : ICbsLogFilePicker
    {
        public int Calls { get; private set; }

        public Task<Stream?> PickCbsLogAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return pick(cancellationToken);
        }
    }

    private sealed class CountingClassifier : ICbsLogMarkerClassifier
    {
        public int Calls { get; private set; }
        public IReadOnlyList<CbsMarkerType> Classify(string? cbsLogText)
        {
            Calls++;
            return Array.Empty<CbsMarkerType>();
        }
    }

    private sealed class ThrowingReadStream(string errorMessage) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException(errorMessage);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException(errorMessage));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
