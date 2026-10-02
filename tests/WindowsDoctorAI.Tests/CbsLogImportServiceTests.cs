using System.Globalization;
using System.Text;
using System.Text.Json;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Tests;

public sealed class CbsLogImportServiceTests
{
    private const string ValidPackage = "Package_123_for_KB3192392~31bf3856ad364e35~amd64~~6.3.1.4";
    private const string PrivatePath = @"C:\Users\private.user\CBS.log";
    private const string PrivateHost = "CBS-PRIVATE-HOST-938";
    private const string PrivateUser = "private.user@example.invalid";
    private const string PrivateToken = "raw-cbs-secret-938";

    [Fact]
    public async Task CancelledPickerReturnsSafeCancelledState()
    {
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(null));
        var service = Service(picker);

        var outcome = await service.ImportAndAnalyzeAsync(CurrentRun());

        Assert.Equal(CbsLogImportStatus.Cancelled, outcome.Status);
        Assert.Null(outcome.Result);
        Assert.Equal(1, picker.Calls);
    }

    [Fact]
    public async Task IoErrorDoesNotExposeExceptionPathOrMessage()
    {
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new ThrowingReadStream(PrivatePath)));
        var outcome = await Service(picker).ImportAndAnalyzeAsync(CurrentRun());
        var serialized = JsonSerializer.Serialize(outcome);

        Assert.Equal(CbsLogImportStatus.FileReadFailed, outcome.Status);
        Assert.DoesNotContain(PrivatePath, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("private.user", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingFileDuringPickerOpenReturnsGenericReadFailure()
    {
        var picker = new FakePicker(_ => Task.FromException<Stream?>(new FileNotFoundException(PrivatePath)));

        var outcome = await Service(picker).ImportAndAnalyzeAsync(CurrentRun());

        Assert.Equal(CbsLogImportStatus.FileReadFailed, outcome.Status);
        Assert.DoesNotContain(PrivatePath, JsonSerializer.Serialize(outcome), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileOverMaximumIsRejectedBeforeAnalyzerRuns()
    {
        var analyzer = new CountingAnalyzer();
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new MemoryStream(new byte[CbsLogImportService.MaximumFileBytes + 1])));
        var service = new CbsLogImportService(picker, analyzer);

        var outcome = await service.ImportAndAnalyzeAsync(CurrentRun());

        Assert.Equal(CbsLogImportStatus.FileTooLarge, outcome.Status);
        Assert.Equal(0, analyzer.Calls);
    }

    [Fact]
    public async Task UnexpectedEncodingIsRejectedWithoutReturningRawContent()
    {
        var unsupported = new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00 };
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new MemoryStream(unsupported)));

        var outcome = await Service(picker).ImportAndAnalyzeAsync(CurrentRun());

        Assert.Equal(CbsLogImportStatus.UnsupportedEncoding, outcome.Status);
        Assert.Null(outcome.Result);
    }

    [Fact]
    public async Task CurrentEventAndTemporallyMatchingCbsMarkerProduceOnlyGenericTransientResult()
    {
        var run = CurrentRun();
        var cbsBody = CbsMarker(run, ValidPackage)
            + $"Info CBS unrelated private data {PrivatePath}; host={PrivateHost}; user={PrivateUser}; token={PrivateToken}\r\n";
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(cbsBody))));
        var analyzer = new WindowsUpdateCbsLogAnalyzer();
        var service = new CbsLogImportService(picker, analyzer);
        var originalRunJson = JsonSerializer.Serialize(run);

        var outcome = await service.ImportAndAnalyzeAsync(run);
        var serializedOutcome = JsonSerializer.Serialize(outcome);

        Assert.Equal(CbsLogImportStatus.Finding, outcome.Status);
        Assert.Equal(new CbsPackageEvidence(CbsEvidenceType.ManifestMissing), outcome.Result?.CbsEvidence);
        Assert.Null(outcome.Result?.CbsEvidence?.PackageIdentity);
        Assert.Equal("WindowsUpdateClient", outcome.Result?.SourceMetadata?.Provider);
        Assert.DoesNotContain(ValidPackage, serializedOutcome, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePath, serializedOutcome, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateHost, serializedOutcome, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateUser, serializedOutcome, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PrivateToken, serializedOutcome, StringComparison.Ordinal);
        Assert.DoesNotContain("Store corruption, manifest missing", serializedOutcome, StringComparison.Ordinal);
        Assert.DoesNotContain("0x800F0831; path=", serializedOutcome, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalRunJson, JsonSerializer.Serialize(run));
        // The service has no logger, repository, or report/HTML writer; only minimized generic metadata is returned.
    }

    [Fact]
    public async Task OldOrStructurallyForgedRunDoesNotOpenPicker()
    {
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("unused\n"))));
        var service = Service(picker);
        var valid = CurrentRun();
        var twoHoursAgo = DateTimeOffset.UtcNow.AddHours(-2);
        var stale = valid with { StartedAtUtc = twoHoursAgo.AddMinutes(-1), CompletedAtUtc = twoHoursAgo };
        var forged = valid with { Id = Guid.Empty };

        Assert.Equal(CbsLogImportStatus.NoCurrentRunEvent, (await service.ImportAndAnalyzeAsync(stale)).Status);
        Assert.Equal(CbsLogImportStatus.NoCurrentRunEvent, (await service.ImportAndAnalyzeAsync(forged)).Status);
        Assert.Equal(0, picker.Calls);
    }

    [Fact]
    public async Task MarkerForAnotherPackageAtUnrelatedSecondProducesNoRecommendation()
    {
        const string anotherPackage = "Package_456_for_KB3192393~31bf3856ad364e35~amd64~~6.3.1.4";
        var run = CurrentRun();
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new MemoryStream(
            Encoding.UTF8.GetBytes(CbsMarker(run, anotherPackage, secondsOffset: 1)))));

        var outcome = await Service(picker).ImportAndAnalyzeAsync(run);

        Assert.Equal(CbsLogImportStatus.NoRecognizedEvidence, outcome.Status);
        Assert.Null(outcome.Result);
        Assert.Equal(1, picker.Calls);
    }

    [Fact]
    public async Task HresultOrFileAloneDoesNotInvokePickerOrProduceFinding()
    {
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("unused\n"))));
        var service = Service(picker);
        var resultWithoutTypedEvent = FindingResult() with
        {
            Description = "Windows Update event contains 0x800F0831.",
            WindowsUpdateEventEvidence = null
        };
        var runWithHresultOnly = RunWithResult(resultWithoutTypedEvent);

        var outcome = await service.ImportAndAnalyzeAsync(runWithHresultOnly);

        Assert.Equal(CbsLogImportStatus.NoCurrentRunEvent, outcome.Status);
        Assert.Null(outcome.Result);
        Assert.Equal(0, picker.Calls);

        var noRunOutcome = await service.ImportAndAnalyzeAsync(null);
        Assert.Equal(CbsLogImportStatus.NoCurrentRunEvent, noRunOutcome.Status);
        Assert.Equal(0, picker.Calls);
    }

    [Fact]
    public async Task WrongChannelOrProviderIsNotTreatedAsCurrentWindowsUpdateEvent()
    {
        var picker = new FakePicker(_ => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("anything\n"))));
        var service = Service(picker);
        var wrongChannel = RunWithResult(FindingResult() with
        {
            WindowsUpdateEventEvidence = new WindowsUpdateEventEvidence("System", WindowsUpdateEventEvidence.CbsStoreCorruptionHresult,
                DateTimeOffset.UtcNow)
        });
        var wrongProvider = RunWithResult(FindingResult() with
        {
            SourceMetadata = new DiagnosticSourceMetadata("OtherProvider")
        });

        Assert.Equal(CbsLogImportStatus.NoCurrentRunEvent, (await service.ImportAndAnalyzeAsync(wrongChannel)).Status);
        Assert.Equal(CbsLogImportStatus.NoCurrentRunEvent, (await service.ImportAndAnalyzeAsync(wrongProvider)).Status);
        Assert.Equal(0, picker.Calls);
    }

    private static CbsLogImportService Service(FakePicker picker) => new(picker, new WindowsUpdateCbsLogAnalyzer());

    private static DiagnosticRun CurrentRun()
    {
        var now = DateTimeOffset.UtcNow;
        return RunWithResult(FindingResult(now));
    }

    private static DiagnosticRun RunWithResult(DiagnosticResult result)
    {
        var now = DateTimeOffset.UtcNow;
        var report = new DiagnosticReport([result], now.AddSeconds(-1), now, TimeSpan.Zero, new HealthScore(92));
        return new DiagnosticRun(Guid.NewGuid(), now.AddSeconds(-2), now, TimeSpan.FromSeconds(2), new ComputerInventory(), report);
    }

    private static DiagnosticResult FindingResult(DateTimeOffset? eventTimestamp = null)
    {
        var timestamp = eventTimestamp ?? DateTimeOffset.UtcNow;
        return new DiagnosticResult(
            "Windows Update", "Sistema", DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
            "Evento de falha do Windows Update (ID 20)", "Mensagem bruta do evento omitida.",
            "Revise o evento na fonte.", "Log=Windows Update; código 0x800F0831.", TimeSpan.Zero, timestamp)
        {
            SourceMetadata = new DiagnosticSourceMetadata("WindowsUpdateClient"),
            WindowsUpdateEventEvidence = new WindowsUpdateEventEvidence(
                WindowsUpdateEventEvidence.OperationalChannel, WindowsUpdateEventEvidence.CbsStoreCorruptionHresult, timestamp)
        };
    }

    private static string CbsMarker(DiagnosticRun run, string package, int secondsOffset = 0)
    {
        var timestamp = run.Report!.Results.Single().WindowsUpdateEventEvidence!.EventTimestamp!.Value.AddSeconds(secondsOffset);
        return $"{timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}, Info CBS Store corruption, manifest missing for package: {package}\r\n";
    }

    private sealed class FakePicker(Func<CancellationToken, Task<Stream?>> pick) : ICbsLogFilePicker
    {
        public int Calls { get; private set; }

        public Task<Stream?> PickCbsLogAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return pick(cancellationToken);
        }
    }

    private sealed class CountingAnalyzer : IWindowsUpdateCbsLogAnalyzer
    {
        public int Calls { get; private set; }
        public DiagnosticResult? AnalyzeWithCurrentRunEvent(string? cbsLogText, WindowsUpdateEventEvidence? eventEvidence)
        {
            Calls++;
            return null;
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
