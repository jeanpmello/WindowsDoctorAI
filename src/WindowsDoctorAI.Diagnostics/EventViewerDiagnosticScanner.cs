using System.Globalization;
using System.Text.RegularExpressions;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Plugin somente de leitura para eventos locais Critical/Error/Warning dos três logs solicitados.</summary>
public sealed class EventViewerDiagnosticScanner(IWindowsDiagnosticDataSource dataSource) : IDiagnosticScanner
{
    private const int MaximumEventsPerLog = 100;
    private const string LookbackDescription = "últimos 7 dias, até 100 registros recentes por log";
    private const string UnattributedCbsProvider = "Microsoft-Windows-WindowsUpdateClient";
    private const string UnattributedCbsChannel = "Microsoft-Windows-WindowsUpdateClient/Operational";
    private const uint UnattributedCbsHresult = 0x800F0831;
    private static readonly Regex HresultToken = new(
        @"(?<![A-Za-z0-9_])0x(?<value>[0-9A-Fa-f]{8})(?![A-Za-z0-9_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Name => "Event Viewer";
    public string Category => "Sistema";

    public async Task<IReadOnlyList<DiagnosticResult>> ScanAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        cancellationToken.ThrowIfCancellationRequested();
        var probe = await dataSource.ReadEventLogsAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<DiagnosticResult>();
        AddLogResults("System", probe.System, results);
        AddLogResults("Application", probe.Application, results);
        AddLogResults("Windows Update", probe.WindowsUpdate, results);
        return results;
    }

    private void AddLogResults(string logName, ProbeResult<IReadOnlyList<DiagnosticEvent>> probe, ICollection<DiagnosticResult> results)
    {
        if (!probe.IsAvailable || probe.Value is null)
        {
            results.Add(DiagnosticResultFactory.Unavailable(Name, Category, $"Event Viewer/{logName}", probe.UnavailableReason));
            return;
        }

        var relevantEvents = probe.Value.Where(item => item.Level is 1 or 2 or 3).ToArray();
        if (relevantEvents.Length == 0)
        {
            results.Add(DiagnosticResultFactory.Healthy(Name, Category, $"Event Viewer/{logName}",
                $"Nenhum evento crítico, erro ou aviso foi encontrado nos {LookbackDescription}.", $"Log={logName}; níveis 1, 2 e 3; {LookbackDescription}."));
            return;
        }

        // A supressão acontece antes da criação de DiagnosticResult: esse tuple não pode chegar a histórico ou HTML.
        // Event ID deliberadamente não integra a chave; sem correlação confiável com CBS, nenhum registro é atribuído.
        foreach (var item in relevantEvents.Where(item => !IsUnattributedCbsEvent(item)))
        {
            var severity = item.Level switch
            {
                1 => DiagnosticSeverity.Critical,
                2 => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Warning
            };
            var label = item.Level switch { 1 => "Critical", 2 => "Error", _ => "Warning" };
            var occurredAt = item.Timestamp?.ToLocalTime().ToString("u") ?? "horário indisponível";
            var description = string.IsNullOrWhiteSpace(item.Message)
                ? "O evento foi coletado, mas o Windows não retornou uma descrição legível."
                : item.Message;
            var result = DiagnosticResultFactory.Create(Name, Category, severity, DiagnosticStatus.Finding,
                $"{label} · {item.Provider} · evento {item.EventId}", description,
                "Investigue a origem e o contexto do evento antes de agir; o scanner não modifica a configuração do Windows.",
                $"Log={logName}; provedor={item.Provider}; ID={item.EventId}; nível={label}; data={occurredAt}.");
            results.Add(result with { SourceMetadata = DiagnosticSourceMetadata.FromEventProvider(item.Provider) });
        }
    }

    private static bool IsUnattributedCbsEvent(DiagnosticEvent item) =>
        string.Equals(item.Provider, UnattributedCbsProvider, StringComparison.OrdinalIgnoreCase)
        && string.Equals(item.LogName, UnattributedCbsChannel, StringComparison.OrdinalIgnoreCase)
        && ContainsUnattributedCbsHresult(item.Message);

    private static bool ContainsUnattributedCbsHresult(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        foreach (Match match in HresultToken.Matches(message))
        {
            if (uint.TryParse(match.Groups["value"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                && value == UnattributedCbsHresult)
                return true;
        }
        return false;
    }
}
