using System.Net;
using System.Text.RegularExpressions;

namespace WindowsDoctorAI.Domain;

/// <summary>Minimiza identificadores e textos livres em cópias persistidas, exibidas ou exportadas.</summary>
public static class DiagnosticPrivacyRedactor
{
    private static readonly Regex ErrorCodePattern = new(@"\b0x[0-9a-fA-F]{8}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EventIdPattern = new(@"\b(?:ID\s*=\s*|evento\s+)(\d{1,10})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EventLogPattern = new(@"\bLog\s*=\s*(System|Application|Windows Update)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EventLevelPattern = new(@"\bnível\s*=\s*(Critical|Error|Warning|Information|Aviso)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveAssignmentPattern = new(
        """\b(?<name>password|passwd|pwd|token|secret|credential(?:s)?|api[_ -]?key|access[_ -]?key)(?<separator>\s*[:=]\s*)(?:"[^"]*"|'[^']*'|[^\s,;]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EmailPattern = new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsPathPattern = new(
        """(?i)(?<![A-Z0-9])(?:[A-Z]:\\|\\\\[^\\\s]+\\[^\\\s]+\\)[^,;\r\n<>|"']*""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnixPathPattern = new(
        """(?i)(?<![\w])/(?:home|users|tmp|var|mnt|etc|opt|root)/[^,;\r\n<>|"']*""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Ipv4Pattern = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Ipv6Pattern = new(@"(?i)(?<![\p{L}\p{N}])(?:[0-9a-f]{0,4}:){2,7}[0-9a-f]{0,4}(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GuidPattern = new(@"\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Retorna uma cópia minimizada; não altera a execução original mantida em memória pelo chamador.</summary>
    public static DiagnosticRun Redact(DiagnosticRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var inventory = run.Inventory;
        return run with { Inventory = RedactInventory(inventory), Report = RedactReport(run.Report, inventory) };
    }

    /// <summary>Retorna uma cópia minimizada do inventário para apresentação, sem alterar o snapshot original.</summary>
    public static ComputerInventory RedactInventory(ComputerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var identifiers = GetIdentifyingValues(inventory);
        return inventory with
        {
            ComputerName = RedactIdentifier(inventory.ComputerName),
            Manufacturer = RedactOptionalText(inventory.Manufacturer, identifiers),
            Model = RedactOptionalText(inventory.Model, identifiers),
            SerialNumber = RedactOptionalValue(inventory.SerialNumber),
            OperatingSystem = inventory.OperatingSystem with
            {
                Name = RedactOptionalText(inventory.OperatingSystem.Name, identifiers),
                Version = RedactOptionalText(inventory.OperatingSystem.Version, identifiers),
                Build = RedactOptionalText(inventory.OperatingSystem.Build, identifiers)
            },
            Processor = inventory.Processor with { Name = RedactOptionalText(inventory.Processor.Name, identifiers) },
            GraphicsAdapters = inventory.GraphicsAdapters.Select(adapter => adapter with
            {
                Name = RedactText(adapter.Name, inventory)
            }).ToArray(),
            PhysicalDisks = inventory.PhysicalDisks.Select(disk => disk with
            {
                Name = RedactIdentifier(disk.Name),
                Model = RedactOptionalText(disk.Model, identifiers)
            }).ToArray(),
            Disks = inventory.Disks.Select(disk => disk with
            {
                Name = RedactIdentifier(disk.Name),
                Label = RedactOptionalValue(disk.Label),
                FileSystem = RedactOptionalText(disk.FileSystem, identifiers)
            }).ToArray(),
            Bios = inventory.Bios with
            {
                Manufacturer = RedactOptionalText(inventory.Bios.Manufacturer, identifiers),
                Version = RedactOptionalText(inventory.Bios.Version, identifiers),
                SerialNumber = RedactOptionalValue(inventory.Bios.SerialNumber)
            },
            Tpm = inventory.Tpm with { Manufacturer = RedactOptionalText(inventory.Tpm.Manufacturer, identifiers) },
            UserName = RedactOptionalValue(inventory.UserName),
            Domain = RedactOptionalValue(inventory.Domain),
            IPv4Addresses = Array.Empty<string>(),
            IPv6Addresses = Array.Empty<string>(),
            NetworkAdapters = inventory.NetworkAdapters.Select(adapter => adapter with
            {
                Name = RedactText(adapter.Name, inventory),
                Description = RedactText(adapter.Description, inventory),
                IPv4Addresses = Array.Empty<string>(),
                IPv6Addresses = Array.Empty<string>()
            }).ToArray()
        };
    }

    /// <summary>Redige todos os campos textuais de resultados, inclusive os recebidos de payloads legados/plugins.</summary>
    public static DiagnosticReport? RedactReport(DiagnosticReport? report, ComputerInventory? inventory = null)
    {
        if (report is null) return null;
        return report with { Results = report.Results.Select(result => RedactResult(result, inventory)).ToArray() };
    }

    /// <summary>Redige texto livre mantendo códigos e metadados estruturados necessários ao suporte.</summary>
    public static string RedactText(string? value, ComputerInventory? inventory = null)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        return RedactTextCore(value, GetIdentifyingValues(inventory));
    }

    private static string RedactTextCore(string value, IEnumerable<string> identifiers)
    {
        var text = value;
        foreach (var identifier in identifiers
                     .Where(identifier => identifier.Length >= 3)
                     .OrderByDescending(identifier => identifier.Length))
        {
            var pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(identifier)}(?![\p{{L}}\p{{N}}_])";
            text = Regex.Replace(text, pattern, "[redigido]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        text = SensitiveAssignmentPattern.Replace(text, match => $"{match.Groups["name"].Value}=[redigido]");
        text = EmailPattern.Replace(text, "[e-mail redigido]");
        text = WindowsPathPattern.Replace(text, "[caminho redigido]");
        text = UnixPathPattern.Replace(text, "[caminho redigido]");
        text = Ipv4Pattern.Replace(text, match => IPAddress.TryParse(match.Value, out _) ? "[IP redigido]" : match.Value);
        text = Ipv6Pattern.Replace(text, match => IPAddress.TryParse(match.Value, out _) ? "[IP redigido]" : match.Value);
        text = GuidPattern.Replace(text, "[ID redigido]");
        return text;
    }

    private static DiagnosticResult RedactResult(DiagnosticResult result, ComputerInventory? inventory)
    {
        var containsRawEventMessage =
            string.Equals(result.ScannerName, "Event Viewer", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(result.ScannerName, "Windows Update", StringComparison.OrdinalIgnoreCase) &&
             result.Title.StartsWith("Evento de falha do Windows Update", StringComparison.OrdinalIgnoreCase));

        return result with
        {
            ScannerName = RedactText(result.ScannerName, inventory),
            Category = RedactText(result.Category, inventory),
            Title = containsRawEventMessage ? RedactEventTitle(result.Title) : RedactText(result.Title, inventory),
            Description = containsRawEventMessage ? RedactEventMessage(result.Description) : RedactText(result.Description, inventory),
            Recommendation = containsRawEventMessage
                ? "Revise o evento na fonte do Windows; o texto original foi omitido por privacidade."
                : RedactText(result.Recommendation, inventory),
            Evidence = containsRawEventMessage ? RedactEventEvidence(result.Evidence) : RedactText(result.Evidence, inventory),
            SourceMetadata = result.SourceMetadata is null
                ? null
                : DiagnosticSourceMetadata.FromEventProvider(result.SourceMetadata.Provider),
            CbsEvidence = result.CbsEvidence is { } cbsEvidence
                && Enum.IsDefined(cbsEvidence.Type)
                && CbsPackageIdentityValidator.IsValid(cbsEvidence.PackageIdentity)
                    ? cbsEvidence
                    : null
        };
    }

    private static string RedactEventTitle(string? title)
    {
        var eventId = string.IsNullOrEmpty(title) ? null : EventIdPattern.Match(title);
        return eventId is { Success: true }
            ? $"Evento do Windows (ID={eventId.Groups[1].Value})"
            : "Evento do Windows";
    }

    private static string RedactEventMessage(string? message)
    {
        var codes = GetErrorCodes(message);
        return codes.Length == 0
            ? "Mensagem bruta do evento removida por privacidade."
            : $"Mensagem bruta do evento removida por privacidade. Códigos de erro: {string.Join(", ", codes)}.";
    }

    private static string RedactEventEvidence(string? evidence)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(evidence))
        {
            var log = EventLogPattern.Match(evidence);
            if (log.Success) parts.Add($"Log={CanonicalEventLog(log.Groups[1].Value)}");
            var eventId = EventIdPattern.Match(evidence);
            if (eventId.Success) parts.Add($"ID={eventId.Groups[1].Value}");
            var level = EventLevelPattern.Match(evidence);
            if (level.Success) parts.Add($"nível={CanonicalEventLevel(level.Groups[1].Value)}");
        }
        var codes = GetErrorCodes(evidence);
        if (codes.Length > 0) parts.Add($"códigos de erro={string.Join(", ", codes)}");
        return parts.Count == 0
            ? "Mensagem e evidências brutas removidas por privacidade."
            : $"Mensagem bruta removida por privacidade; metadados: {string.Join("; ", parts)}.";
    }

    private static string[] GetErrorCodes(string? value) => string.IsNullOrEmpty(value)
        ? Array.Empty<string>()
        : ErrorCodePattern.Matches(value).Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

    private static string CanonicalEventLog(string value) => value.Equals("system", StringComparison.OrdinalIgnoreCase)
        ? "System"
        : value.Equals("application", StringComparison.OrdinalIgnoreCase) ? "Application" : "Windows Update";

    private static string CanonicalEventLevel(string value) => value.Equals("critical", StringComparison.OrdinalIgnoreCase)
        ? "Critical"
        : value.Equals("error", StringComparison.OrdinalIgnoreCase) ? "Error"
        : value.Equals("warning", StringComparison.OrdinalIgnoreCase) || value.Equals("aviso", StringComparison.OrdinalIgnoreCase)
            ? "Warning" : "Information";

    private static string[] GetIdentifyingValues(ComputerInventory? inventory)
    {
        if (inventory is null) return Array.Empty<string>();
        return new[]
        {
            inventory.ComputerName, inventory.SerialNumber, inventory.UserName, inventory.Domain,
            inventory.Bios.SerialNumber
        }
        .Concat(inventory.PhysicalDisks.Select(disk => disk.Name))
        .Concat(inventory.Disks.SelectMany(disk => new[] { disk.Name, disk.Label ?? string.Empty }))
        .Concat(inventory.NetworkAdapters.SelectMany(adapter => new[] { adapter.Name, adapter.Description }))
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    private static string RedactIdentifier(string? value) => string.IsNullOrWhiteSpace(value) ? value ?? string.Empty : "[redigido]";

    private static string? RedactOptionalValue(string? value) => string.IsNullOrWhiteSpace(value) ? value : "[redigido]";

    private static string? RedactOptionalText(string? value, IEnumerable<string> knownIdentifiers) =>
        value is null ? null : RedactTextCore(value, knownIdentifiers);
}
