using System.Globalization;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Descobre apenas metadados de Windows Server Backup; não valida integridade nem inicia restauração.</summary>
public sealed class WindowsBackupDiagnosticScanner(IWindowsBackupDataSource dataSource) : IDiagnosticScanner
{
    public string Name => "Windows Server Backup";
    public string Category => "Sistema";

    public async Task<IReadOnlyList<DiagnosticResult>> ScanAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        cancellationToken.ThrowIfCancellationRequested();
        var probe = await dataSource.ReadBackupSetsAsync(cancellationToken).ConfigureAwait(false);

        return [probe.Status switch
        {
            WindowsBackupProbeStatus.Available when
                probe.BackupSetCount is > 0 and <= 1_000_000 &&
                probe.LatestBackupTimeUtc is not null &&
                probe.BackupType is not null => CreateAvailableResult(probe),
            WindowsBackupProbeStatus.NoBackupSets when probe.BackupSetCount == 0 => DiagnosticResultFactory.Create(
                Name,
                Category,
                DiagnosticSeverity.Warning,
                DiagnosticStatus.Finding,
                "Nenhum conjunto de backup retornado",
                "A consulta de metadados não encontrou conjuntos visíveis nesta fonte. Isso não exclui backups mantidos por outros produtos ou destinos.",
                "Confirme a política e a fonte de backup com o responsável; esta verificação não cria nem modifica backups.",
                "Conjuntos retornados: 0."),
            WindowsBackupProbeStatus.ModuleUnavailable => Unavailable(
                "Windows Server Backup indisponível",
                "O cmdlet Get-WBBackupSet não está disponível no Windows PowerShell atual.",
                "Nenhum recurso foi instalado ou ativado. Use somente um ambiente onde o módulo já esteja disponível."),
            WindowsBackupProbeStatus.AccessDenied => Unavailable(
                "Acesso aos metadados de backup negado",
                "A consulta foi negada para a conta atual; nenhum detalhe bruto foi exibido ou registrado.",
                "A documentação do Windows Server Backup prevê acesso por Administrators ou Backup Operators; confirme o procedimento apropriado com o administrador responsável. O aplicativo não solicita elevação nem altera permissões."),
            WindowsBackupProbeStatus.InvalidResponse => Unavailable(
                "Resposta de backup inválida",
                "A fonte retornou uma resposta ausente ou fora do formato esperado; o conteúdo bruto foi descartado.",
                "Revise a disponibilidade da fonte por um procedimento administrativo separado; esta checagem não altera o sistema."),
            WindowsBackupProbeStatus.TimedOut => Unavailable(
                "Consulta de backup expirou",
                "A consulta somente de leitura excedeu o limite de tempo e foi encerrada.",
                "Tente novamente mais tarde ou verifique a fonte por um procedimento separado; nenhuma restauração foi iniciada."),
            _ => Unavailable(
                "Consulta de backup indisponível",
                "Não foi possível completar a consulta somente de leitura; detalhes internos foram omitidos.",
                "Verifique a disponibilidade da fonte por um procedimento separado; nenhuma alteração foi aplicada.")
        }];
    }

    private static DiagnosticResult CreateAvailableResult(WindowsBackupProbe probe)
    {
        var count = probe.BackupSetCount!.Value;
        var date = probe.LatestBackupTimeUtc!.Value.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        var type = probe.BackupType switch
        {
            WindowsBackupType.Full => "Completo",
            WindowsBackupType.Incremental => "Incremental",
            WindowsBackupType.Differential => "Diferencial",
            WindowsBackupType.Other => "Outro/indeterminado",
            _ => "indeterminado"
        };

        return DiagnosticResultFactory.Create(
            "Windows Server Backup",
            "Sistema",
            DiagnosticSeverity.Information,
            DiagnosticStatus.NotVerified,
            $"Metadados de backup: {count} conjunto(s)",
            "A consulta encontrou metadados de backup; disponibilidade não comprova integridade dos dados nem capacidade de restauração.",
            "Valide integridade e recuperabilidade em procedimento separado e autorizado. Nenhuma restauração foi iniciada.",
            $"Conjuntos retornados: {count}; data mais recente: {date}; tipo: {type}.");
    }

    private static DiagnosticResult Unavailable(string title, string description, string recommendation) =>
        DiagnosticResultFactory.Create(
            "Windows Server Backup",
            "Sistema",
            DiagnosticSeverity.Information,
            DiagnosticStatus.Unavailable,
            title,
            description,
            recommendation,
            "Nenhum caminho, host ou item de backup foi coletado ou exibido.");
}
