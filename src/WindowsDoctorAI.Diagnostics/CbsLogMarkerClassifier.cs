using System.Text.RegularExpressions;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Classifica tipos genéricos de marcador em texto fornecido pelo chamador, sem associação a eventos.</summary>
public sealed class CbsLogMarkerClassifier : ICbsLogMarkerClassifier
{
    public const int MaximumInputCharacters = 2 * 1024 * 1024;
    private const int MaximumLineCharacters = 4096;
    private const string TimestampPattern = @"(?<timestamp>[0-9]{4}-[0-9]{2}-[0-9]{2}[ \t]+[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?)";

    private static readonly Regex ManifestMissingLine = new(
        $@"^[ \t]*(?:{TimestampPattern}(?:[ \t]+[0-9]+)?[ \t]*,[ \t]*)?Info[ \t]+CBS[ \t]+Store corruption, manifest missing for package:[ \t]*[^\s]+[ \t]*\.?[ \t]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));
    private static readonly Regex FailedToResolveLine = new(
        $@"^[ \t]*(?:{TimestampPattern}(?:[ \t]+[0-9]+)?[ \t]*,[ \t]*)?Error[ \t]+CBS[ \t]+Failed to resolve package[ \t]+'[^'\s]+'[ \t]+\[HRESULT[ \t]*=[ \t]*0x800F0831[ \t]+-[ \t]+CBS_E_STORE_CORRUPTION\][ \t]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    public IReadOnlyList<CbsMarkerType> Classify(string? cbsLogText)
    {
        if (string.IsNullOrEmpty(cbsLogText)
            || cbsLogText.Length > MaximumInputCharacters
            || !(cbsLogText.EndsWith('\n') || cbsLogText.EndsWith('\r'))
            || cbsLogText.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            return Array.Empty<CbsMarkerType>();

        var lines = Regex.Split(cbsLogText, "\\r\\n|\\n|\\r");
        var markerTypes = new HashSet<CbsMarkerType>();
        for (var index = 0; index < lines.Length - 1; index++)
        {
            var line = lines[index];
            if (line.Length > MaximumLineCharacters) return Array.Empty<CbsMarkerType>();
            if (ManifestMissingLine.IsMatch(line))
                markerTypes.Add(CbsMarkerType.ManifestMissing);
            else if (FailedToResolveLine.IsMatch(line))
                markerTypes.Add(CbsMarkerType.FailedToResolvePackage);
        }

        return markerTypes.Order().ToArray();
    }
}
