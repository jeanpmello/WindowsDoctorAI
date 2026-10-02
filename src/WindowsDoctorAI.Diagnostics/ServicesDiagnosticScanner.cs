using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Plugin de observação de serviços; não inicia, para nem reconfigura serviços.</summary>
public sealed class ServicesDiagnosticScanner(IWindowsDiagnosticDataSource dataSource) : IDiagnosticScanner
{
    private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "DcomLaunch", "EventLog"
    };

    public string Name => "Services";
    public string Category => "Sistema";

    public async Task<IReadOnlyList<DiagnosticResult>> ScanAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        cancellationToken.ThrowIfCancellationRequested();
        var probe = await dataSource.ReadServicesAsync(cancellationToken).ConfigureAwait(false);
        if (!probe.IsAvailable || probe.Value is null)
        {
            return [DiagnosticResultFactory.Unavailable(Name, Category, "Serviços do Windows", probe.UnavailableReason)];
        }
        if (probe.Value.Count == 0)
        {
            return [DiagnosticResultFactory.NotVerified(Name, Category, "Serviços do Windows", "A consulta foi concluída, mas não retornou serviços; nenhum estado foi presumido.", "Win32_Service retornou zero registros.")];
        }

        var services = probe.Value;
        var byName = services.ToDictionary(service => service.Name, StringComparer.OrdinalIgnoreCase);
        var results = new List<DiagnosticResult>();
        var missingCritical = CriticalServices.Where(name => !byName.ContainsKey(name)).ToArray();
        if (missingCritical.Length > 0)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Critical, DiagnosticStatus.Finding,
                "Serviço crítico não encontrado", "Um ou mais serviços centrais esperados não apareceram na consulta local.",
                "Confirme a edição e integridade do Windows antes de qualquer ação.", $"Serviços ausentes da enumeração: {string.Join(", ", missingCritical)}."));
        }

        var criticalStopped = services.Where(service => CriticalServices.Contains(service.Name) && !IsRunning(service.State)).ToArray();
        foreach (var service in criticalStopped)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Critical, DiagnosticStatus.Finding,
                $"Serviço crítico parado: {service.DisplayName}", $"O serviço {service.Name} foi observado no estado {service.State}.",
                "Revise o estado e os eventos relacionados antes de decidir qualquer intervenção; o scanner não alterou o serviço.",
                $"Nome={service.Name}; estado={service.State}; inicialização={service.StartMode}."));
        }

        var startupMisconfigured = services.Where(service => CriticalServices.Contains(service.Name) &&
            !service.StartMode.Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
            !service.StartMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var service in startupMisconfigured)
        {
            var severity = service.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                ? DiagnosticSeverity.Critical
                : DiagnosticSeverity.Warning;
            results.Add(DiagnosticResultFactory.Create(Name, Category, severity, DiagnosticStatus.Finding,
                $"Inicialização incomum: {service.DisplayName}", $"O serviço central {service.Name} está configurado como {service.StartMode}, em vez de inicialização automática.",
                "Compare com a configuração suportada pela edição e política deste Windows; nenhuma configuração foi modificada.",
                $"Nome={service.Name}; inicialização={service.StartMode}; estado={service.State}."));
        }

        var autoStopped = services.Where(service => !CriticalServices.Contains(service.Name) &&
            IsAutomatic(service.StartMode) && !IsRunning(service.State)).ToArray();
        if (autoStopped.Length > 0)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
                $"{autoStopped.Length} serviço(s) automático(s) parado(s)", "A consulta encontrou serviços com inicialização automática que estavam parados no momento da leitura; alguns podem usar gatilhos ou estar em estado transitório.",
                "Revise individualmente o serviço e seus gatilhos antes de qualquer intervenção.",
                string.Join("; ", autoStopped.Take(30).Select(service => $"{service.Name} ({service.State}, {service.StartMode})"))));
        }

        var brokenDependencies = new List<string>();
        foreach (var service in services.Where(service => IsRunning(service.State)))
        {
            foreach (var dependencyName in service.Dependencies.Where(name => !name.StartsWith('+')))
            {
                if (!byName.TryGetValue(dependencyName, out var dependency))
                {
                    brokenDependencies.Add($"{service.Name} → {dependencyName} (não listado)");
                }
                else if (!IsRunning(dependency.State))
                {
                    brokenDependencies.Add($"{service.Name} → {dependencyName} ({dependency.State})");
                }
            }
        }
        if (brokenDependencies.Count > 0)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
                "Dependências de serviços inconsistentes", "Alguns serviços em execução referenciaram dependências que não estavam listadas ou não estavam em execução.",
                "Confirme a relação no Windows Services e nos logs antes de agir.", string.Join("; ", brokenDependencies.Take(30))));
        }

        if (results.Count == 0)
        {
            results.Add(DiagnosticResultFactory.Healthy(Name, Category, "Serviços e dependências", "Os serviços centrais consultados estavam presentes e ativos; não foram observados serviços automáticos parados nem dependências inconsistentes.",
                $"Win32_Service consultado; {services.Count} serviços enumerados."));
        }
        return results;
    }

    private static bool IsRunning(string state) => state.Equals("Running", StringComparison.OrdinalIgnoreCase);
    private static bool IsAutomatic(string startMode) => startMode.Equals("Auto", StringComparison.OrdinalIgnoreCase) || startMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase);
}
