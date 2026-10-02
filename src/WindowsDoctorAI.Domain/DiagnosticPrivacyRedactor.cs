using System.Text.RegularExpressions;

namespace WindowsDoctorAI.Domain;

/// <summary>Minimiza dados pessoais em cópias persistidas/exportadas de execuções diagnósticas.</summary>
public static class DiagnosticPrivacyRedactor
{
    private static readonly Regex ErrorCodePattern = new(@"\b0x[0-9a-fA-F]{8}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Retorna uma cópia minimizada; não altera a execução em memória apresentada durante a coleta.</summary>
    public static DiagnosticRun Redact(DiagnosticRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var inventory = run.Inventory;
        var redactedInventory = inventory with
        {
            ComputerName = RedactValue(inventory.ComputerName),
            SerialNumber = RedactOptionalValue(inventory.SerialNumber),
            UserName = RedactOptionalValue(inventory.UserName),
            Domain = RedactOptionalValue(inventory.Domain),
            IPv4Addresses = Array.Empty<string>(),
            IPv6Addresses = Array.Empty<string>(),
            Bios = inventory.Bios with { SerialNumber = RedactOptionalValue(inventory.Bios.SerialNumber) },
            NetworkAdapters = inventory.NetworkAdapters.Select(adapter => adapter with
            {
                IPv4Addresses = Array.Empty<string>(),
                IPv6Addresses = Array.Empty<string>()
            }).ToArray()
        };

        var report = run.Report is null
            ? null
            : run.Report with { Results = run.Report.Results.Select(RedactResult).ToArray() };
        return run with { Inventory = redactedInventory, Report = report };
    }

    private static DiagnosticResult RedactResult(DiagnosticResult result)
    {
        var containsRawEventMessage =
            string.Equals(result.ScannerName, "Event Viewer", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(result.ScannerName, "Windows Update", StringComparison.OrdinalIgnoreCase) &&
             result.Title.StartsWith("Evento de falha do Windows Update", StringComparison.OrdinalIgnoreCase));
        return containsRawEventMessage
            ? result with { Description = RedactEventMessage(result.Description) }
            : result;
    }

    private static string RedactEventMessage(string? message)
    {
        var codes = string.IsNullOrEmpty(message)
            ? Array.Empty<string>()
            : ErrorCodePattern.Matches(message).Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray();
        return codes.Length == 0
            ? "Mensagem bruta do evento removida por privacidade; consulte a fonte do Windows pelo log, ID e horário indicados."
            : $"Mensagem bruta do evento removida por privacidade. Códigos de erro observados: {string.Join(", ", codes)}.";
    }

    private static string RedactValue(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : "[redigido]";

    private static string? RedactOptionalValue(string? value) => string.IsNullOrWhiteSpace(value) ? value : "[redigido]";
}
