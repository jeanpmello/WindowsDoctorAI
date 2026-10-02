using System.Security.Cryptography;
using System.Text;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

internal enum CbsLogImportStatus
{
    Cancelled,
    FileReadFailed,
    FileTooLarge,
    UnsupportedEncoding,
    NoCurrentRunEvent,
    NoRecognizedEvidence,
    Finding
}

/// <summary>Saída transitória: contém apenas estado ou resultado tipado, nunca arquivo, caminho ou texto bruto.</summary>
internal sealed record CbsLogImportOutcome(CbsLogImportStatus Status, DiagnosticResult? Result = null);

/// <summary>Coordena seleção e análise limitada de CBS.log sem gravar conteúdo ou registrar exceções.</summary>
internal sealed class CbsLogImportService(
    ICbsLogFilePicker filePicker,
    IWindowsUpdateCbsLogAnalyzer analyzer)
{
    public const int MaximumFileBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan MaximumRunAge = TimeSpan.FromMinutes(15);

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(false, true, true);
    private static readonly UnicodeEncoding StrictUtf16BigEndian = new(true, true, true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LittleEndianBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BigEndianBom = [0xFE, 0xFF];
    private static readonly byte[] Utf32LittleEndianBom = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BigEndianBom = [0x00, 0x00, 0xFE, 0xFF];

    public static bool HasCurrentRunEvent(DiagnosticRun? run) => FindCurrentRunEvent(run) is not null;

    public async Task<CbsLogImportOutcome> ImportAndAnalyzeAsync(
        DiagnosticRun? currentRun,
        CancellationToken cancellationToken = default)
    {
        var eventEvidence = FindCurrentRunEvent(currentRun);
        if (eventEvidence is null) return new CbsLogImportOutcome(CbsLogImportStatus.NoCurrentRunEvent);

        var buffer = new byte[MaximumFileBytes + 1];
        Stream? stream = null;
        try
        {
            stream = await filePicker.PickCbsLogAsync(cancellationToken).ConfigureAwait(false);
            if (stream is null) return new CbsLogImportOutcome(CbsLogImportStatus.Cancelled);

            var totalBytes = 0;
            while (totalBytes < buffer.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalBytes, buffer.Length - totalBytes), cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0) break;
                totalBytes += bytesRead;
            }

            if (totalBytes > MaximumFileBytes)
                return new CbsLogImportOutcome(CbsLogImportStatus.FileTooLarge);

            if (!TryDecode(buffer.AsSpan(0, totalBytes), out var decodedText))
                return new CbsLogImportOutcome(CbsLogImportStatus.UnsupportedEncoding);

            var result = analyzer.AnalyzeWithCurrentRunEvent(decodedText, eventEvidence);
            return result is null
                ? new CbsLogImportOutcome(CbsLogImportStatus.NoRecognizedEvidence)
                : new CbsLogImportOutcome(CbsLogImportStatus.Finding, result);
        }
        catch (OperationCanceledException)
        {
            return new CbsLogImportOutcome(CbsLogImportStatus.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Deliberately omit exception details: platform exceptions can contain private paths or file names.
            return new CbsLogImportOutcome(CbsLogImportStatus.FileReadFailed);
        }
        finally
        {
            if (stream is not null)
            {
                try { await stream.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { }
            }
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static WindowsUpdateEventEvidence? FindCurrentRunEvent(DiagnosticRun? run)
    {
        if (!IsFreshWellFormedRun(run)) return null;

        var result = run?.Report?.Results.FirstOrDefault(candidate =>
            candidate.Status == DiagnosticStatus.Finding
            && string.Equals(candidate.ScannerName, "Windows Update", StringComparison.OrdinalIgnoreCase)
            && DiagnosticSourceMetadata.NormalizeProvider(candidate.SourceMetadata?.Provider)
                == DiagnosticSourceMetadata.WindowsUpdateClientProvider
            && candidate.WindowsUpdateEventEvidence is { IsExactCbsStoreCorruptionEvent: true });

        return result?.WindowsUpdateEventEvidence is { IsExactCbsStoreCorruptionEvent: true, EventTimestamp: not null } evidence
            ? new WindowsUpdateEventEvidence(WindowsUpdateEventEvidence.OperationalChannel,
                WindowsUpdateEventEvidence.CbsStoreCorruptionHresult, evidence.EventTimestamp)
            : null;
    }

    private static bool IsFreshWellFormedRun(DiagnosticRun? run)
    {
        if (run is null || run.Id == Guid.Empty || run.Report is not { } report) return false;
        var now = DateTimeOffset.UtcNow;
        return run.StartedAtUtc <= run.CompletedAtUtc
            && run.CompletedAtUtc <= now.AddMinutes(1)
            && now - run.CompletedAtUtc <= MaximumRunAge
            && report.StartedAtUtc <= report.CompletedAtUtc
            && report.StartedAtUtc >= run.StartedAtUtc.AddMinutes(-1)
            && report.CompletedAtUtc <= run.CompletedAtUtc.AddMinutes(1);
    }

    private static bool TryDecode(ReadOnlySpan<byte> bytes, out string text)
    {
        text = string.Empty;
        if (bytes.StartsWith(Utf32LittleEndianBom) || bytes.StartsWith(Utf32BigEndianBom)) return false;

        try
        {
            if (bytes.StartsWith(Utf8Bom))
            {
                text = StrictUtf8.GetString(bytes[Utf8Bom.Length..]);
            }
            else if (bytes.StartsWith(Utf16LittleEndianBom))
            {
                text = StrictUtf16LittleEndian.GetString(bytes[Utf16LittleEndianBom.Length..]);
            }
            else if (bytes.StartsWith(Utf16BigEndianBom))
            {
                text = StrictUtf16BigEndian.GetString(bytes[Utf16BigEndianBom.Length..]);
            }
            else
            {
                // BOM-less input is accepted only as strict UTF-8; replacement fallbacks are never used.
                text = StrictUtf8.GetString(bytes);
            }
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return !text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
    }
}
