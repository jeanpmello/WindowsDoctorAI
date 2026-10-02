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

    private static readonly Regex ExactHresult = new(
        @"(?<![A-Za-z0-9_])0x800F0831(?![A-Za-z0-9_])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));
    private static readonly Regex ManifestMissingLine = new(
        @"^\s*(?:[0-9]{4}-[0-9]{2}-[0-9]{2}\s+[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?(?:\s+[0-9]+)?\s*,\s*)?Info\s+CBS\s+Store corruption, manifest missing for package:\s*(?<package>[^\s]+)\s*\.?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));
    private static readonly Regex FailedToResolveLine = new(
        """^\s*(?:[0-9]{4}-[0-9]{2}-[0-9]{2}\s+[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?(?:\s+[0-9]+)?\s*,\s*)?Error\s+CBS\s+Failed to resolve package\s+'(?<package>[^'\s]+)'\s+\[HRESULT\s*=\s*0x800F0831\s+-\s*CBS_E_STORE_CORRUPTION\]\s*$""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    /// <summary>Compatibilidade para consumidores que já fornecem o evento estruturado in-memory.</summary>
    public DiagnosticResult? Analyze(string? cbsLogText, DiagnosticEvent? windowsUpdateEvent)
    {
        if (windowsUpdateEvent is null
            || !string.Equals(windowsUpdateEvent.LogName, WindowsUpdateOperationalChannel, StringComparison.OrdinalIgnoreCase)
            || DiagnosticSourceMetadata.NormalizeProvider(windowsUpdateEvent.Provider) is null
            || !ExactHresult.IsMatch(windowsUpdateEvent.Message ?? string.Empty))
            return null;

        return AnalyzeWithCurrentRunEvent(cbsLogText,
            new WindowsUpdateEventEvidence(WindowsUpdateOperationalChannel, WindowsUpdateEventEvidence.CbsStoreCorruptionHresult));
    }

    /// <summary>Retorna null salvo evidência de evento tipada e linha CBS completa com identidade validada.</summary>
    public DiagnosticResult? AnalyzeWithCurrentRunEvent(string? cbsLogText, WindowsUpdateEventEvidence? eventEvidence)
    {
        if (eventEvidence is not { IsExactCbsStoreCorruptionEvent: true }
            || string.IsNullOrEmpty(cbsLogText)
            || cbsLogText.Length > MaximumInputCharacters
            || !(cbsLogText.EndsWith('\n') || cbsLogText.EndsWith('\r'))
            || cbsLogText.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            return null;

        var packageIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evidenceTypes = new HashSet<CbsEvidenceType>();
        var lines = Regex.Split(cbsLogText, "\\r\\n|\\n|\\r");

        // The final empty split element exists only because complete records must end in a newline.
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
            evidenceTypes.Add(evidenceType);
        }

        // Multiple package identities make the relationship ambiguous; fail closed rather than selecting one.
        if (packageIdentities.Count != 1 || evidenceTypes.Count == 0) return null;

        var selectedType = evidenceTypes.Contains(CbsEvidenceType.ManifestMissing)
            ? CbsEvidenceType.ManifestMissing
            : CbsEvidenceType.FailedToResolvePackage;
        var identity = packageIdentities.Single();
        return new DiagnosticResult(
            "Windows Update",
            "Sistema",
            DiagnosticSeverity.Warning,
            DiagnosticStatus.Finding,
            "Evidência CBS de pacote ausente ou não resolvido",
            "A execução diagnóstica atual confirmou 0x800F0831 e o CBS.log selecionado contém um marcador e uma package identity reconhecidos.",
            "Confirme manualmente a atualização e a identidade tipada do pacote com o suporte responsável. Esta evidência é informativa; não prescreve nem executa reparo.",
            $"CBS marker={selectedType}; package identity={identity}",
            TimeSpan.Zero,
            DateTimeOffset.UtcNow)
        {
            SourceMetadata = new DiagnosticSourceMetadata(DiagnosticSourceMetadata.WindowsUpdateClientProvider),
            CbsEvidence = new CbsPackageEvidence(selectedType, identity),
            WindowsUpdateEventEvidence = new WindowsUpdateEventEvidence(
                WindowsUpdateOperationalChannel, WindowsUpdateEventEvidence.CbsStoreCorruptionHresult)
        };
    }
}
