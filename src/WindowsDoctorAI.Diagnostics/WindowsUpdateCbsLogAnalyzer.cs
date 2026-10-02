using System.Globalization;
using System.Text.RegularExpressions;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Analisa texto fornecido pelo chamador sem abrir arquivos, executar comandos ou preservar mensagens brutas.</summary>
public sealed class WindowsUpdateCbsLogAnalyzer : IWindowsUpdateCbsLogAnalyzer
{
    public const string WindowsUpdateOperationalChannel = WindowsUpdateEventEvidence.OperationalChannel;
    public const int MaximumInputCharacters = 2 * 1024 * 1024;
    private const int MaximumLineCharacters = 4096;
    private const string TimestampPattern = @"(?<timestamp>[0-9]{4}-[0-9]{2}-[0-9]{2}[ \t]+[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?)";

    private static readonly Regex ExactHresult = new(
        @"(?<![A-Za-z0-9_])0x800F0831(?![A-Za-z0-9_])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));
    private static readonly Regex ManifestMissingLine = new(
        $@"^[ \t]*(?:{TimestampPattern}(?:[ \t]+[0-9]+)?[ \t]*,[ \t]*)?Info[ \t]+CBS[ \t]+Store corruption, manifest missing for package:[ \t]*(?<package>[^\s]+)[ \t]*\.?[ \t]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));
    private static readonly Regex FailedToResolveLine = new(
        $"^[ \\t]*(?:{TimestampPattern}(?:[ \\t]+[0-9]+)?[ \\t]*,[ \\t]*)?Error[ \\t]+CBS[ \\t]+Failed to resolve package[ \\t]+'(?<package>[^'\\s]+)'[ \\t]+\\[HRESULT[ \\t]*=[ \\t]*0x800F0831[ \\t]+-[ \\t]+CBS_E_STORE_CORRUPTION\\][ \\t]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    /// <summary>Compatibilidade para consumidores que já fornecem o evento estruturado in-memory.</summary>
    public DiagnosticResult? Analyze(string? cbsLogText, DiagnosticEvent? windowsUpdateEvent)
    {
        if (windowsUpdateEvent is null
            || !string.Equals(windowsUpdateEvent.LogName, WindowsUpdateOperationalChannel, StringComparison.OrdinalIgnoreCase)
            || DiagnosticSourceMetadata.NormalizeProvider(windowsUpdateEvent.Provider) is null
            || !ExactHresult.IsMatch(windowsUpdateEvent.Message ?? string.Empty)
            || windowsUpdateEvent.Timestamp is null)
            return null;

        return AnalyzeWithCurrentRunEvent(cbsLogText,
            new WindowsUpdateEventEvidence(WindowsUpdateOperationalChannel,
                WindowsUpdateEventEvidence.CbsStoreCorruptionHresult, windowsUpdateEvent.Timestamp));
    }

    /// <summary>
    /// Produces only generic, same-local-second co-occurrence evidence. A timestamp match does not prove
    /// that the CBS marker belongs to the update event, so no package identity is returned.
    /// </summary>
    public DiagnosticResult? AnalyzeWithCurrentRunEvent(string? cbsLogText, WindowsUpdateEventEvidence? eventEvidence)
    {
        if (eventEvidence is not { IsExactCbsStoreCorruptionEvent: true, EventTimestamp: not null }
            || string.IsNullOrEmpty(cbsLogText)
            || cbsLogText.Length > MaximumInputCharacters
            || !(cbsLogText.EndsWith('\n') || cbsLogText.EndsWith('\r'))
            || cbsLogText.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            return null;

        var packageIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchingEvidenceTypes = new HashSet<CbsEvidenceType>();
        var lines = Regex.Split(cbsLogText, "\\r\\n|\\n|\\r");

        // CBS records use a local wall-clock timestamp (yyyy-MM-dd HH:mm:ss, optionally fractional and sequenced).
        // A matching second is only a conservative filter, not proof of a shared update/record identity.
        for (var index = 0; index < lines.Length - 1; index++)
        {
            var line = lines[index];
            if (line.Length > MaximumLineCharacters) return null;

            var match = ManifestMissingLine.Match(line);
            var evidenceType = CbsEvidenceType.ManifestMissing;
            if (!match.Success)
            {
                match = FailedToResolveLine.Match(line);
                evidenceType = CbsEvidenceType.FailedToResolvePackage;
            }
            if (!match.Success) continue;

            var packageIdentity = match.Groups["package"].Value.TrimEnd('.');
            if (!CbsPackageIdentityValidator.IsValid(packageIdentity)) return null;
            packageIdentities.Add(packageIdentity);

            if (HasSameLocalSecond(match.Groups["timestamp"], eventEvidence.EventTimestamp.Value))
                matchingEvidenceTypes.Add(evidenceType);
        }

        // A file containing multiple package identities is ambiguous, even when only one line shares the second.
        if (packageIdentities.Count != 1 || matchingEvidenceTypes.Count == 0) return null;

        var selectedType = matchingEvidenceTypes.Contains(CbsEvidenceType.ManifestMissing)
            ? CbsEvidenceType.ManifestMissing
            : CbsEvidenceType.FailedToResolvePackage;
        return new DiagnosticResult(
            "Windows Update",
            "Sistema",
            DiagnosticSeverity.Warning,
            DiagnosticStatus.Finding,
            "Sinal CBS no mesmo segundo local de evento Windows Update",
            "Um evento WindowsUpdateClient com 0x800F0831 e um marcador CBS têm o mesmo segundo local. Essa coincidência temporal não prova que sejam da mesma atualização; nenhuma identidade de pacote foi atribuída.",
            "Revise separadamente o evento e o CBS.log com o suporte responsável. Não associe o marcador a uma atualização nem a um pacote com base apenas no horário.",
            $"CBS marker={selectedType}; mesmo segundo local; identidade do pacote não atribuída",
            TimeSpan.Zero,
            DateTimeOffset.UtcNow)
        {
            SourceMetadata = new DiagnosticSourceMetadata(DiagnosticSourceMetadata.WindowsUpdateClientProvider),
            CbsEvidence = new CbsPackageEvidence(selectedType),
            WindowsUpdateEventEvidence = new WindowsUpdateEventEvidence(
                WindowsUpdateOperationalChannel, WindowsUpdateEventEvidence.CbsStoreCorruptionHresult,
                eventEvidence.EventTimestamp)
        };
    }

    private static bool HasSameLocalSecond(Group timestampGroup, DateTimeOffset eventTimestamp)
    {
        if (!timestampGroup.Success) return false;
        var value = timestampGroup.Value;
        var fractionalSeparator = value.IndexOf('.');
        var wholeSecond = fractionalSeparator < 0 ? value : value[..fractionalSeparator];
        if (!DateTime.TryParseExact(wholeSecond, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var cbsLocalTime))
            return false;

        var eventLocalTime = eventTimestamp.ToLocalTime();
        return cbsLocalTime.Year == eventLocalTime.Year
            && cbsLocalTime.Month == eventLocalTime.Month
            && cbsLocalTime.Day == eventLocalTime.Day
            && cbsLocalTime.Hour == eventLocalTime.Hour
            && cbsLocalTime.Minute == eventLocalTime.Minute
            && cbsLocalTime.Second == eventLocalTime.Second;
    }
}
