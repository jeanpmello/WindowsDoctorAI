using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Plugin somente de leitura para eventos locais Critical/Error/Warning dos três logs solicitados.</summary>
public sealed class EventViewerDiagnosticScanner(IWindowsDiagnosticDataSource dataSource) : IDiagnosticScanner
{
    private const int MaximumEventsPerLog = 100;
    private const string LookbackDescription = "últimos 7 dias, até 100 registros recentes por log";

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

        foreach (var item in relevantEvents)
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
}
