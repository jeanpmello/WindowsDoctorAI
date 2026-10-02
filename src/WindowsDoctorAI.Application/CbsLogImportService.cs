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
    NoRecognizedMarker,
    ObservationAvailable
}

/// <summary>Saída transitória com status e tipos genéricos; não contém evento, pacote, caminho ou texto bruto.</summary>
internal sealed record CbsLogImportOutcome(
    CbsLogImportStatus Status,
    IReadOnlyList<CbsMarkerType>? MarkerTypes = null);

/// <summary>Coordena leitura manual limitada e classificação em memória, sem gravação ou logging do conteúdo.</summary>
internal sealed class CbsLogImportService(
    ICbsLogFilePicker filePicker,
    ICbsLogMarkerClassifier classifier)
{
    public const int MaximumFileBytes = 2 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(false, true, true);
    private static readonly UnicodeEncoding StrictUtf16BigEndian = new(true, true, true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LittleEndianBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BigEndianBom = [0xFE, 0xFF];
    private static readonly byte[] Utf32LittleEndianBom = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BigEndianBom = [0x00, 0x00, 0xFE, 0xFF];

    public async Task<CbsLogImportOutcome> ImportAndAnalyzeAsync(CancellationToken cancellationToken = default)
    {
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

            var markerTypes = classifier.Classify(decodedText);
            return markerTypes.Count == 0
                ? new CbsLogImportOutcome(CbsLogImportStatus.NoRecognizedMarker)
                : new CbsLogImportOutcome(CbsLogImportStatus.ObservationAvailable, markerTypes);
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
