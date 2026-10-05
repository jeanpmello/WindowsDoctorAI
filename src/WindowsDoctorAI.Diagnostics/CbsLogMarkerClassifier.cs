using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Classifica tipos genéricos de marcador em texto fornecido pelo chamador, sem associação a eventos.</summary>
public sealed class CbsLogMarkerClassifier : ICbsLogMarkerClassifier
{
    public const int MaximumInputCharacters = 2 * 1024 * 1024;
    private const int MaximumLineCharacters = 4096;
    private const string ManifestMissingMessage = "Store corruption, manifest missing for package:";
    private const string FailedToResolveMessage = "Failed to resolve package";

    public IReadOnlyList<CbsMarkerType> Classify(string? cbsLogText)
    {
        if (string.IsNullOrEmpty(cbsLogText)
            || cbsLogText.Length > MaximumInputCharacters
            || !(cbsLogText.EndsWith('\n') || cbsLogText.EndsWith('\r')))
            return Array.Empty<CbsMarkerType>();

        foreach (var character in cbsLogText)
        {
            if (char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
                return Array.Empty<CbsMarkerType>();
        }

        var markerTypes = new HashSet<CbsMarkerType>();
        var lineStart = 0;
        for (var index = 0; index < cbsLogText.Length; index++)
        {
            if (cbsLogText[index] is not ('\r' or '\n'))
                continue;

            var line = cbsLogText.AsSpan(lineStart, index - lineStart);
            if (line.Length > MaximumLineCharacters)
                return Array.Empty<CbsMarkerType>();

            if (TryClassifyLine(line, out var markerType))
                markerTypes.Add(markerType);

            if (cbsLogText[index] == '\r' && index + 1 < cbsLogText.Length && cbsLogText[index + 1] == '\n')
                index++;
            lineStart = index + 1;
        }

        return markerTypes.Order().ToArray();
    }

    private static bool TryClassifyLine(ReadOnlySpan<char> line, out CbsMarkerType markerType)
    {
        markerType = default;
        ConsumeHorizontalWhitespace(ref line);
        var lineWithoutLeadingWhitespace = line;

        TryConsumeTimestampPrefix(ref line);
        if (IsManifestMissingLine(line))
        {
            markerType = CbsMarkerType.ManifestMissing;
            return true;
        }

        line = lineWithoutLeadingWhitespace;
        TryConsumeTimestampPrefix(ref line);
        if (IsFailedToResolveLine(line))
        {
            markerType = CbsMarkerType.FailedToResolvePackage;
            return true;
        }

        return false;
    }

    private static bool IsManifestMissingLine(ReadOnlySpan<char> line)
    {
        if (!TryConsumeHeader(ref line, "Info")
            || !TryConsumeLiteral(ref line, ManifestMissingMessage))
            return false;

        ConsumeHorizontalWhitespace(ref line);
        var packageLength = 0;
        while (packageLength < line.Length && !char.IsWhiteSpace(line[packageLength]))
            packageLength++;
        if (packageLength == 0)
            return false;

        line = line[packageLength..];
        ConsumeHorizontalWhitespace(ref line);
        if (!line.IsEmpty && line[0] == '.')
            line = line[1..];
        ConsumeHorizontalWhitespace(ref line);
        return line.IsEmpty;
    }

    private static bool IsFailedToResolveLine(ReadOnlySpan<char> line)
    {
        if (!TryConsumeHeader(ref line, "Error")
            || !TryConsumeLiteral(ref line, FailedToResolveMessage)
            || !ConsumeRequiredHorizontalWhitespace(ref line)
            || line.IsEmpty
            || line[0] != '\'')
            return false;

        line = line[1..];
        var packageLength = 0;
        while (packageLength < line.Length
            && line[packageLength] != '\''
            && !char.IsWhiteSpace(line[packageLength]))
            packageLength++;
        if (packageLength == 0 || packageLength == line.Length || line[packageLength] != '\'')
            return false;

        line = line[(packageLength + 1)..];
        if (!ConsumeRequiredHorizontalWhitespace(ref line)
            || !TryConsumeLiteral(ref line, "[HRESULT"))
            return false;

        ConsumeHorizontalWhitespace(ref line);
        if (!TryConsumeCharacter(ref line, '='))
            return false;
        ConsumeHorizontalWhitespace(ref line);
        if (!TryConsumeLiteral(ref line, "0x800F0831")
            || !ConsumeRequiredHorizontalWhitespace(ref line)
            || !TryConsumeCharacter(ref line, '-')
            || !ConsumeRequiredHorizontalWhitespace(ref line)
            || !TryConsumeLiteral(ref line, "CBS_E_STORE_CORRUPTION")
            || !TryConsumeCharacter(ref line, ']'))
            return false;

        ConsumeHorizontalWhitespace(ref line);
        return line.IsEmpty;
    }

    private static bool TryConsumeHeader(ref ReadOnlySpan<char> line, string level) =>
        TryConsumeLiteral(ref line, level)
        && ConsumeRequiredHorizontalWhitespace(ref line)
        && TryConsumeLiteral(ref line, "CBS")
        && ConsumeRequiredHorizontalWhitespace(ref line);

    private static bool TryConsumeTimestampPrefix(ref ReadOnlySpan<char> text)
    {
        var remaining = text;
        if (!TryConsumeDigits(ref remaining, 4)
            || !TryConsumeCharacter(ref remaining, '-')
            || !TryConsumeDigits(ref remaining, 2)
            || !TryConsumeCharacter(ref remaining, '-')
            || !TryConsumeDigits(ref remaining, 2)
            || !ConsumeRequiredHorizontalWhitespace(ref remaining)
            || !TryConsumeDigits(ref remaining, 2)
            || !TryConsumeCharacter(ref remaining, ':')
            || !TryConsumeDigits(ref remaining, 2)
            || !TryConsumeCharacter(ref remaining, ':')
            || !TryConsumeDigits(ref remaining, 2))
            return false;

        if (!remaining.IsEmpty && remaining[0] == '.')
        {
            remaining = remaining[1..];
            if (!TryConsumeOneOrMoreDigits(ref remaining))
                return false;
        }

        var optionalSequenceStart = remaining;
        if (ConsumeHorizontalWhitespace(ref remaining) > 0
            && !remaining.IsEmpty
            && IsAsciiDigit(remaining[0]))
        {
            TryConsumeOneOrMoreDigits(ref remaining);
        }
        else
        {
            remaining = optionalSequenceStart;
        }

        ConsumeHorizontalWhitespace(ref remaining);
        if (!TryConsumeCharacter(ref remaining, ','))
            return false;
        ConsumeHorizontalWhitespace(ref remaining);
        text = remaining;
        return true;
    }

    private static bool TryConsumeDigits(ref ReadOnlySpan<char> text, int count)
    {
        if (text.Length < count)
            return false;
        for (var index = 0; index < count; index++)
        {
            if (!IsAsciiDigit(text[index]))
                return false;
        }

        text = text[count..];
        return true;
    }

    private static bool TryConsumeOneOrMoreDigits(ref ReadOnlySpan<char> text)
    {
        var count = 0;
        while (count < text.Length && IsAsciiDigit(text[count]))
            count++;
        if (count == 0)
            return false;

        text = text[count..];
        return true;
    }

    private static int ConsumeHorizontalWhitespace(ref ReadOnlySpan<char> text)
    {
        var count = 0;
        while (count < text.Length && text[count] is ' ' or '\t')
            count++;
        text = text[count..];
        return count;
    }

    private static bool ConsumeRequiredHorizontalWhitespace(ref ReadOnlySpan<char> text) =>
        ConsumeHorizontalWhitespace(ref text) > 0;

    private static bool TryConsumeCharacter(ref ReadOnlySpan<char> text, char expected)
    {
        if (text.IsEmpty || text[0] != expected)
            return false;
        text = text[1..];
        return true;
    }

    private static bool TryConsumeLiteral(ref ReadOnlySpan<char> text, string expected)
    {
        if (!text.StartsWith(expected.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;
        text = text[expected.Length..];
        return true;
    }

    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';
}
