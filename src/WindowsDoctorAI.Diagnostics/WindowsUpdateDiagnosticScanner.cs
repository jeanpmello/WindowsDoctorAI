using System.Text.RegularExpressions;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Plugin somente de leitura para Windows Update Agent, registro de reinicialização e log operacional.</summary>
public sealed class WindowsUpdateDiagnosticScanner(IWindowsDiagnosticDataSource dataSource) : IDiagnosticScanner
{
    private static readonly Regex FailureText = new(@"(fail|failed|failure|error|falha|falhou|erro)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ErrorCode = new(@"\b0x[0-9a-fA-F]{8}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Name => "Windows Update";
    public string Category => "Sistema";

    public async Task<IReadOnlyList<DiagnosticResult>> ScanAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        cancellationToken.ThrowIfCancellationRequested();
        var probe = await dataSource.ReadWindowsUpdateAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<DiagnosticResult>();

        if (!probe.PendingUpdates.IsAvailable || probe.PendingUpdates.Value is null)
        {
            results.Add(DiagnosticResultFactory.Unavailable(Name, Category, "Atualizações pendentes", probe.PendingUpdates.UnavailableReason));
        }
        else if (probe.PendingUpdates.Value.Count == 0)
        {
            results.Add(DiagnosticResultFactory.Healthy(Name, Category, "Atualizações pendentes", "A consulta ao Windows Update Agent não encontrou atualizações disponíveis para instalação.", "Busca local: IsInstalled=0 e IsHidden=0; nenhum item retornado."));
        }
        else
        {
            var updates = probe.PendingUpdates.Value;
            var titles = string.Join("; ", updates.Take(10).Select(update => update.Title));
            var remainder = updates.Count > 10 ? $"; e mais {updates.Count - 10}" : string.Empty;
            var ids = string.Join(", ", updates.Take(10).Where(update => !string.IsNullOrWhiteSpace(update.UpdateId)).Select(update => update.UpdateId));
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
                $"{updates.Count} atualização(ões) pendente(s)",
                "O Windows Update Agent retornou atualizações não instaladas e não ocultas; isso não confirma falha de instalação.",
                "Revise a lista no Windows Update e decida quando instalar; este diagnóstico não inicia atualizações.",
                $"Títulos: {titles}{remainder}. Identificadores: {(ids.Length == 0 ? "não retornados" : ids)}."));
        }

        if (!probe.RebootPending.IsAvailable)
        {
            results.Add(DiagnosticResultFactory.Unavailable(Name, Category, "Reinicialização pendente", probe.RebootPending.UnavailableReason));
        }
        else if (probe.RebootPending.Value)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
                "Reinicialização pendente detectada", "Uma das chaves locais consultadas indica reinicialização pendente após atualização ou operação do Windows.",
                "Considere reiniciar em um momento apropriado; nenhuma reinicialização foi iniciada.",
                "Marcador encontrado em CBS, Windows Update ou PendingFileRenameOperations."));
        }
        else
        {
            results.Add(DiagnosticResultFactory.Healthy(Name, Category, "Reinicialização pendente", "Os marcadores locais consultados não indicam reinicialização pendente.",
                "Chaves CBS/Windows Update e PendingFileRenameOperations consultadas; nenhum marcador presente."));
        }

        if (!probe.RecentEvents.IsAvailable || probe.RecentEvents.Value is null)
        {
            results.Add(DiagnosticResultFactory.Unavailable(Name, Category, "Falhas recentes do Windows Update", probe.RecentEvents.UnavailableReason));
        }
        else
        {
            var failures = probe.RecentEvents.Value
                .Where(item => item.Level is 1 or 2 || item.Level == 3 && FailureText.IsMatch(item.Message ?? string.Empty))
                .ToArray();
            if (failures.Length == 0)
            {
                results.Add(DiagnosticResultFactory.Healthy(Name, Category, "Falhas recentes do Windows Update", "Nenhum evento crítico, erro ou aviso com texto de falha foi encontrado no log operacional consultado.",
                    "Microsoft-Windows-WindowsUpdateClient/Operational; últimos 14 dias; até 100 eventos lidos."));
            }
            else
            {
                foreach (var failure in failures)
                {
                    var code = ErrorCode.Match(failure.Message ?? string.Empty).Value;
                    var severity = failure.Level == 1 ? DiagnosticSeverity.Critical : DiagnosticSeverity.Warning;
                    var time = failure.Timestamp?.ToLocalTime().ToString("u") ?? "horário indisponível";
                    var result = DiagnosticResultFactory.Create(Name, Category, severity, DiagnosticStatus.Finding,
                        $"Evento de falha do Windows Update (ID {failure.EventId})",
                        string.IsNullOrWhiteSpace(failure.Message) ? "O log registrou um evento de erro/criticidade sem descrição legível." : failure.Message,
                        "Pesquise o código e os detalhes deste evento no Windows Update; nenhuma correção foi aplicada.",
                        $"Log=Windows Update; ID={failure.EventId}; nível={(failure.Level == 1 ? "Critical" : "Error")}; data={time}; código {(code.Length == 0 ? "não identificado no texto" : code)}.");
                    results.Add(result with { SourceMetadata = DiagnosticSourceMetadata.FromEventProvider(failure.Provider) });
                }
            }
        }

        return results;
    }
}
