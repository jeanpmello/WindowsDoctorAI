using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Application;

/// <summary>Executa os plugins registrados, mede cada coleta e consolida somente evidências observadas.</summary>
public sealed class DiagnosticEngine : IDiagnosticEngine
{
    private const int CriticalPenalty = 25;
    private const int WarningPenalty = 8;
    private readonly IReadOnlyList<IDiagnosticScanner> _scanners;
    private readonly ILogger<DiagnosticEngine> _logger;

    public DiagnosticEngine(IEnumerable<IDiagnosticScanner> scanners, ILogger<DiagnosticEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(scanners);
        ArgumentNullException.ThrowIfNull(logger);
        _scanners = scanners.ToArray();
        _logger = logger;
    }

    public async Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = DateTimeOffset.UtcNow;
        var totalTimer = Stopwatch.StartNew();
        var resultsByScanner = new IReadOnlyList<DiagnosticResult>[_scanners.Count];
        var parallelTasks = new Dictionary<int, Task<IReadOnlyList<DiagnosticResult>>>();
        var serialIndexes = new List<int>();

        for (var index = 0; index < _scanners.Count; index++)
        {
            if (_scanners[index].SupportsParallelExecution)
            {
                parallelTasks[index] = RunScannerSafelyAsync(_scanners[index], cancellationToken);
            }
            else
            {
                serialIndexes.Add(index);
            }
        }

        // Plugins que declaram segurança para concorrência iniciam juntos; os demais executam em sequência.
        var parallelCompletion = Task.WhenAll(parallelTasks.Values);
        foreach (var index in serialIndexes)
        {
            resultsByScanner[index] = await RunScannerSafelyAsync(_scanners[index], cancellationToken).ConfigureAwait(false);
        }
        await parallelCompletion.ConfigureAwait(false);
        foreach (var (index, task) in parallelTasks)
        {
            resultsByScanner[index] = await task.ConfigureAwait(false);
        }

        var consolidated = resultsByScanner.SelectMany(results => results ?? Array.Empty<DiagnosticResult>()).ToArray();
        if (_scanners.Count == 0)
        {
            consolidated =
            [
                DiagnosticResultFactoryForEngine.CreateNoScannerResult()
            ];
        }

        totalTimer.Stop();
        var completedAt = DateTimeOffset.UtcNow;
        var score = CalculateHealthScore(consolidated);
        return new DiagnosticReport(consolidated, startedAt, completedAt, totalTimer.Elapsed, score);
    }

    private async Task<IReadOnlyList<DiagnosticResult>> RunScannerSafelyAsync(IDiagnosticScanner scanner, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var completedAt = DateTimeOffset.UtcNow;
        try
        {
            var scannerResults = await scanner.ScanAsync(cancellationToken).ConfigureAwait(false);
            timer.Stop();
            completedAt = DateTimeOffset.UtcNow;
            if (scannerResults is null || scannerResults.Count == 0)
            {
                return
                [
                    NewResult(scanner, DiagnosticSeverity.Information, DiagnosticStatus.NotVerified,
                        "O scanner não retornou resultados", "Nenhuma observação foi produzida por este plugin.",
                        "Verifique o contrato e a disponibilidade da fonte de dados; ausência de resultados não foi tratada como saúde.",
                        "Nenhuma evidência retornada pelo scanner.", timer.Elapsed, completedAt)
                ];
            }

            return scannerResults.Select(result => Normalize(scanner, result, timer.Elapsed, completedAt)).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            timer.Stop();
            completedAt = DateTimeOffset.UtcNow;
            _logger.LogError("Um scanner falhou; detalhes omitidos por privacidade. Os demais scanners continuarão.");
            return
            [
                NewResult(scanner, DiagnosticSeverity.Information, DiagnosticStatus.Unavailable,
                    "Scanner indisponível", "A execução deste plugin falhou; o resultado não permite concluir que o sistema está saudável ou com problema.",
                    "Revise a disponibilidade da fonte local, permissões e logs. Nenhuma alteração foi aplicada.",
                    "Falha isolada; não há evidência coletada.", timer.Elapsed, completedAt)
            ];
        }
    }

    private static DiagnosticResult Normalize(IDiagnosticScanner scanner, DiagnosticResult result, TimeSpan duration, DateTimeOffset timestamp)
    {
        if (result is null)
        {
            return NewResult(scanner, DiagnosticSeverity.Information, DiagnosticStatus.NotVerified,
                "Resultado inválido", "O plugin retornou um item nulo, sem observação verificável.",
                "Revise a implementação do plugin.", "Nenhuma evidência disponível.", duration, timestamp);
        }

        var severity = Enum.IsDefined(result.Severity) ? result.Severity : DiagnosticSeverity.Information;
        var status = Enum.IsDefined(result.Status) ? result.Status : DiagnosticStatus.NotVerified;
        return new DiagnosticResult(
            scanner.Name,
            string.IsNullOrWhiteSpace(scanner.Category) ? "Sem categoria" : scanner.Category,
            severity,
            status,
            string.IsNullOrWhiteSpace(result.Title) ? "Resultado sem título" : result.Title,
            result.Description ?? string.Empty,
            result.Recommendation ?? string.Empty,
            result.Evidence ?? "Evidência não fornecida pelo plugin.",
            duration,
            timestamp);
    }

    private static DiagnosticResult NewResult(
        IDiagnosticScanner scanner,
        DiagnosticSeverity severity,
        DiagnosticStatus status,
        string title,
        string description,
        string recommendation,
        string evidence,
        TimeSpan duration,
        DateTimeOffset timestamp) => new(
            scanner.Name,
            string.IsNullOrWhiteSpace(scanner.Category) ? "Sem categoria" : scanner.Category,
            severity,
            status,
            title,
            description,
            recommendation,
            evidence,
            duration,
            timestamp);

    private static HealthScore? CalculateHealthScore(IReadOnlyCollection<DiagnosticResult> results)
    {
        if (!results.Any(result => result.Status is DiagnosticStatus.Healthy or DiagnosticStatus.Finding)) return null;
        var critical = results.Count(result => result.Status == DiagnosticStatus.Finding && result.Severity == DiagnosticSeverity.Critical);
        var warnings = results.Count(result => result.Status == DiagnosticStatus.Finding && result.Severity == DiagnosticSeverity.Warning);
        var score = Math.Max(0L, 100L - (long)critical * CriticalPenalty - (long)warnings * WarningPenalty);
        return new HealthScore((int)score);
    }

    private static class DiagnosticResultFactoryForEngine
    {
        internal static DiagnosticResult CreateNoScannerResult() => new(
            "Diagnostic Engine",
            "Sistema",
            DiagnosticSeverity.Information,
            DiagnosticStatus.NotVerified,
            "Nenhum scanner registrado",
            "Não há plugins IDiagnosticScanner registrados para esta execução.",
            "Registre os plugins compatíveis no contêiner de dependências.",
            "Nenhuma verificação foi executada.",
            TimeSpan.Zero,
            DateTimeOffset.UtcNow);
    }
}
