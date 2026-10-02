using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Plugin de observação de dispositivos Plug and Play; não instala nem remove drivers.</summary>
public sealed class DriversDiagnosticScanner(IWindowsDiagnosticDataSource dataSource) : IDiagnosticScanner
{
    private static readonly HashSet<int> NonErrorStates = [0, 22, 24, 45];

    public string Name => "Drivers";
    public string Category => "Drivers";

    public async Task<IReadOnlyList<DiagnosticResult>> ScanAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        cancellationToken.ThrowIfCancellationRequested();
        var probe = await dataSource.ReadDevicesAsync(cancellationToken).ConfigureAwait(false);
        if (!probe.IsAvailable || probe.Value is null)
        {
            return [DiagnosticResultFactory.Unavailable(Name, Category, "Dispositivos e drivers", probe.UnavailableReason)];
        }
        if (probe.Value.Count == 0)
        {
            return [DiagnosticResultFactory.NotVerified(Name, Category, "Dispositivos e drivers", "A consulta foi concluída, mas não retornou dispositivos Plug and Play.", "Win32_PnPEntity retornou zero registros.")];
        }

        var devices = probe.Value;
        var results = new List<DiagnosticResult>();
        var missingDrivers = devices.Where(device => device.ErrorCode == 28).ToArray();
        if (missingDrivers.Length > 0)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
                $"{missingDrivers.Length} driver(s) ausente(s)", "O código de configuração 28 do Windows indica que não há driver instalado para o dispositivo.",
                "Identifique o fabricante e obtenha um driver compatível por uma fonte confiável; nenhuma instalação foi iniciada.",
                string.Join("; ", missingDrivers.Take(30).Select(device => $"{device.Name} · código 28 · {device.DeviceId}"))));
        }

        var errors = devices.Where(device => device.ErrorCode is int code && !NonErrorStates.Contains(code) && code != 28).ToArray();
        if (errors.Length > 0)
        {
            var criticalCodes = errors.Where(device => device.ErrorCode is 10 or 31 or 43).ToArray();
            var otherErrors = errors.Except(criticalCodes).ToArray();
            if (criticalCodes.Length > 0)
            {
                results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Critical, DiagnosticStatus.Finding,
                    $"{criticalCodes.Length} dispositivo(s) com erro crítico", "O Windows reportou códigos de configuração que indicam falha de inicialização, carregamento ou funcionamento do dispositivo.",
                    "Confirme o código e a versão do driver do fabricante; nenhuma alteração foi feita.",
                    string.Join("; ", criticalCodes.Take(30).Select(device => $"{device.Name} · código {device.ErrorCode} · {device.DeviceId}"))));
            }
            if (otherErrors.Length > 0)
            {
                results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
                    $"{otherErrors.Length} dispositivo(s) com código de erro", "O Windows reportou códigos de configuração diferentes de dispositivo desativado/ausente e sem driver (códigos 22, 24, 28 e 45 são tratados separadamente).",
                    "Revise o código no Gerenciador de Dispositivos antes de decidir uma ação.",
                    string.Join("; ", otherErrors.Take(30).Select(device => $"{device.Name} · código {device.ErrorCode} · {device.DeviceId}"))));
            }
        }

        var unknownDevices = devices.Where(device => device.Name.Contains("unknown device", StringComparison.OrdinalIgnoreCase) ||
            device.Name.Contains("dispositivo desconhecido", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (unknownDevices.Length > 0)
        {
            results.Add(DiagnosticResultFactory.Create(Name, Category, DiagnosticSeverity.Warning, DiagnosticStatus.Finding,
                $"{unknownDevices.Length} dispositivo(s) identificado(s) como desconhecido(s)", "O nome retornado pela enumeração Plug and Play identifica explicitamente dispositivo desconhecido.",
                "Confira o hardware e o identificador Plug and Play para localizar um driver compatível; nada foi instalado.",
                string.Join("; ", unknownDevices.Take(30).Select(device => $"{device.Name} · código {device.ErrorCode?.ToString() ?? "não retornado"} · {device.DeviceId}"))));
        }

        if (results.Count == 0)
        {
            results.Add(DiagnosticResultFactory.Healthy(Name, Category, "Dispositivos e drivers", "Não foram observados drivers ausentes, erros de configuração ou nomes explicitamente desconhecidos.",
                $"Win32_PnPEntity consultado; {devices.Count} dispositivos enumerados. Dispositivos desativados ou não conectados não foram classificados como erro de driver."));
        }
        return results;
    }
}
