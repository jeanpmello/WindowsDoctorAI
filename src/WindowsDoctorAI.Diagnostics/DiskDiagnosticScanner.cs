using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Plugin de leitura de saúde/SMART, volumes e telemetria; não altera discos nem arquivos.</summary>
public sealed class DiskDiagnosticScanner(IWindowsDiagnosticDataSource dataSource) : IDiagnosticScanner
{
    public string Name => "Disk";
    public string Category => "Hardware";

    public async Task<IReadOnlyList<DiagnosticResult>> ScanAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        cancellationToken.ThrowIfCancellationRequested();
        var probe = await dataSource.ReadDisksAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<DiagnosticResult>();

        CheckSmart(probe.SmartStatuses, results);
        CheckDiskHealth(probe.DiskHealth, results);
        CheckVolumes(probe.Volumes, results);
        CheckTemperatures(probe.Temperatures, results);
        return results;
    }

    private void CheckSmart(ProbeResult<IReadOnlyList<DiskSmartStatus>> probe, ICollection<DiagnosticResult> results)
    {
        if (!probe.IsAvailable || probe.Value is null)
        {
            results.Add(DiagnosticResultFactory.Unavailable(Name, Category, "SMART", probe.UnavailableReason));
            return;
        }
        if (probe.Value.Count == 0)
        {
            results.Add(DiagnosticResultFactory.NotVerified(Name, Category, "SMART", "A consulta terminou sem discos expostos pela interface SMART; isso não confirma que os discos estão saudáveis.", "MSStorageDriver_FailurePredictStatus não retornou unidades."));
            return;
        }
        var failing = probe.Value.Where(item => item.PredictFailure == true).ToArray();
        if (failing.Length > 0)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Critical, DiagnosticStatus.Finding,
                $"SMART prevê falha em {failing.Length} disco(s)", "A interface SMART do Windows retornou PredictFailure=true para uma ou mais unidades.",
                "Faça backup dos dados importantes e consulte o diagnóstico do fabricante; o scanner não altera o disco.",
                string.Join("; ", failing.Select(item => item.InstanceName))));
        }
        var unknown = probe.Value.Where(item => item.PredictFailure is null).ToArray();
        if (unknown.Length > 0)
        {
            results.Add(DiagnosticResultFactory.NotVerified(Name, Category, "Estado SMART", "A consulta retornou discos, mas não forneceu um estado preditivo legível para todos eles.", string.Join("; ", unknown.Select(item => item.InstanceName))));
        }
        if (failing.Length == 0 && unknown.Length == 0)
        {
            results.Add(DiagnosticResultFactory.Healthy(Name, Category, "Estado SMART", "A interface consultada não sinalizou falha preditiva nas unidades enumeradas; isso não substitui as ferramentas do fabricante.",
                string.Join("; ", probe.Value.Select(item => $"{item.InstanceName}: PredictFailure=false"))));
        }
    }

    private void CheckDiskHealth(ProbeResult<IReadOnlyList<DiskHealthInfo>> probe, ICollection<DiagnosticResult> results)
    {
        if (!probe.IsAvailable || probe.Value is null)
        {
            results.Add(DiagnosticResultFactory.Unavailable(Name, Category, "Saúde reportada pelo Windows", probe.UnavailableReason));
            return;
        }
        if (probe.Value.Count == 0)
        {
            results.Add(DiagnosticResultFactory.NotVerified(Name, Category, "Saúde reportada pelo Windows", "A enumeração não retornou discos físicos.", "Win32_DiskDrive retornou zero registros."));
            return;
        }
        var criticalStatuses = probe.Value.Where(item => IsCriticalDiskStatus(item.Status)).ToArray();
        var warningStatuses = probe.Value.Where(item => IsWarningDiskStatus(item.Status)).ToArray();
        var unclassified = probe.Value.Where(item => !IsCriticalDiskStatus(item.Status) && !IsWarningDiskStatus(item.Status) &&
            item.Status?.Equals("OK", StringComparison.OrdinalIgnoreCase) != true).ToArray();
        if (criticalStatuses.Length > 0)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Critical, DiagnosticStatus.Finding,
                $"Windows reportou estado crítico em {criticalStatuses.Length} unidade(s)", "O WMI retornou um estado explicitamente associado a erro, falha preditiva ou condição não recuperável.",
                "Revise o status e faça backup antes de qualquer intervenção; nenhum reparo foi executado.",
                string.Join("; ", criticalStatuses.Select(item => $"{item.DeviceId} · {item.Model} · Status={item.Status}"))));
        }
        if (warningStatuses.Length > 0)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
                $"Windows reportou estado de atenção em {warningStatuses.Length} unidade(s)", "O WMI retornou um estado não ideal, mas diferente dos estados tratados como falha crítica.",
                "Investigue a unidade e consulte SMART/ferramenta do fabricante; o scanner não altera o disco.",
                string.Join("; ", warningStatuses.Select(item => $"{item.DeviceId} · {item.Model} · Status={item.Status}"))));
        }
        if (unclassified.Length > 0)
        {
            results.Add(DiagnosticResultFactory.NotVerified(Name, Category, "Saúde de disco", "O Windows enumerou discos sem status ou com um valor não classificado; esses dados não foram tratados como saudáveis nem como falha confirmada.",
                string.Join("; ", unclassified.Select(item => $"{item.DeviceId} · {item.Model} · Status={item.Status ?? "não informado"}"))));
        }
        if (criticalStatuses.Length == 0 && warningStatuses.Length == 0 && unclassified.Length == 0)
        {
            results.Add(DiagnosticResultFactory.Healthy(Name, Category, "Saúde reportada pelo Windows", "Todos os dispositivos físicos enumerados retornaram Status=OK; isso não garante ausência de falha futura.",
                string.Join("; ", probe.Value.Select(item => $"{item.DeviceId} · {item.Model} · Status=OK"))));
        }
    }

    private void CheckVolumes(ProbeResult<IReadOnlyList<DiskVolumeInfo>> probe, ICollection<DiagnosticResult> results)
    {
        if (!probe.IsAvailable || probe.Value is null)
        {
            results.Add(DiagnosticResultFactory.Unavailable(Name, Category, "Espaço livre", probe.UnavailableReason));
            return;
        }
        var measured = probe.Value.Where(volume => volume.CapacityBytes is > 0 && volume.FreeBytes.HasValue).ToArray();
        if (measured.Length == 0)
        {
            results.Add(DiagnosticResultFactory.NotVerified(Name, Category, "Espaço livre", "Nenhum volume local retornou capacidade e espaço livre suficientes para calcular ocupação.", $"Volumes enumerados: {probe.Value.Count}."));
            return;
        }
        var critical = measured.Where(volume => (double)volume.FreeBytes!.Value / volume.CapacityBytes!.Value < 0.10).ToArray();
        var warning = measured.Where(volume => (double)volume.FreeBytes!.Value / volume.CapacityBytes!.Value >= 0.10 &&
            (double)volume.FreeBytes!.Value / volume.CapacityBytes!.Value < 0.20).ToArray();
        if (critical.Length > 0)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Critical, DiagnosticStatus.Finding,
                $"Espaço livre abaixo de 10% em {critical.Length} volume(s)", "O espaço livre medido está abaixo do limiar operacional configurado; o limiar é uma regra geral, não uma garantia de falha.",
                "Revise o uso do volume e mantenha espaço livre suficiente; nenhum arquivo foi removido.",
                string.Join("; ", critical.Select(FormatVolume))));
        }
        if (warning.Length > 0)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
                $"Espaço livre abaixo de 20% em {warning.Length} volume(s)", "O espaço livre medido está entre 10% e 20% do volume.",
                "Acompanhe o volume e planeje a gestão de espaço sem excluir arquivos automaticamente.",
                string.Join("; ", warning.Select(FormatVolume))));
        }
        if (critical.Length == 0 && warning.Length == 0)
        {
            results.Add(DiagnosticResultFactory.Healthy(Name, Category, "Espaço livre", "Os volumes locais medidos tinham pelo menos 20% de espaço livre.",
                string.Join("; ", measured.Select(FormatVolume))));
        }
    }

    private void CheckTemperatures(ProbeResult<IReadOnlyList<DiskTemperatureInfo>> probe, ICollection<DiagnosticResult> results)
    {
        if (!probe.IsAvailable || probe.Value is null)
        {
            results.Add(DiagnosticResultFactory.Unavailable(Name, Category, "Temperatura SMART", probe.UnavailableReason));
            return;
        }
        if (probe.Value.Count == 0)
        {
            results.Add(DiagnosticResultFactory.NotVerified(Name, Category, "Temperatura SMART", "A interface consultada não retornou um atributo de temperatura legível; vários discos/controladores não expõem esta telemetria.", "MSStorageDriver_ATAPISmartData consultado; nenhum atributo 190/194 válido foi retornado."));
            return;
        }
        results.Add(DiagnosticResultFactory.NotVerified(Name, Category, "Temperatura SMART observada", "O valor abaixo foi lido do atributo SMART; não se aplica um limite universal porque a faixa aceitável depende do fabricante e modelo.",
            string.Join("; ", probe.Value.Select(item => $"{item.InstanceName}: {item.Celsius} °C"))));
    }

    private static string FormatVolume(DiskVolumeInfo volume)
    {
        var percent = (double)volume.FreeBytes!.Value / volume.CapacityBytes!.Value * 100;
        return $"{volume.Name}: {percent:N1}% livre ({volume.FreeBytes.Value / 1_073_741_824d:N1} / {volume.CapacityBytes.Value / 1_073_741_824d:N1} GB)";
    }

    private static bool IsCriticalDiskStatus(string? status) => status is not null &&
        (status.Equals("Pred Fail", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("NonRecoverable", StringComparison.OrdinalIgnoreCase));

    private static bool IsWarningDiskStatus(string? status) => status is not null &&
        (status.Equals("Degraded", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("Stressed", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("No Contact", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("Lost Comm", StringComparison.OrdinalIgnoreCase));
}
