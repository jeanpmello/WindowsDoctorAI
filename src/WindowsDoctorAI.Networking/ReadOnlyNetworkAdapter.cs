using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace WindowsDoctorAI.Networking;

/// <summary>
/// Adaptador inicial para equipamentos que aceitam comandos de diagnóstico via SSH/CLI.
/// Não conhece credenciais e não aceita comandos arbitrários: somente perfis versionados.
/// </summary>
public sealed class ReadOnlyNetworkAdapter : INetworkDeviceAdapter
{
    private static readonly Regex UnsafeCommand = new(
        @"(^|\s)(configure|conf t|write|copy|erase|delete|reload|reboot|shutdown|set |add |remove |create |commit|apply|save)(\s|$)|[;&|`]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IReadOnlyCommandTransport _transport;
    private readonly ReadOnlyCollection<ReadOnlyCommandProfile> _profiles;
    private readonly int _port;

    public ReadOnlyNetworkAdapter(
        IReadOnlyCommandTransport transport,
        IEnumerable<ReadOnlyCommandProfile> profiles,
        int port = 22)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _profiles = new ReadOnlyCollection<ReadOnlyCommandProfile>(
            (profiles ?? throw new ArgumentNullException(nameof(profiles)))
            .Where(profile => profile is not null)
            .ToList());
        if (_profiles.Count == 0)
            throw new ArgumentException("Pelo menos um perfil de leitura é necessário.", nameof(profiles));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
    }

    public string Name => "CLI/SSH somente leitura";

    public bool CanHandle(NetworkDeviceIdentity device) =>
        device is not null && _profiles.Any(profile => Matches(profile, device));

    public async Task<NetworkInspectionResult> InspectAsync(
        NetworkDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var profile = _profiles.FirstOrDefault(candidate => Matches(candidate, device));
        if (profile is null)
        {
            return new NetworkInspectionResult(
                device,
                DateTimeOffset.UtcNow,
                false,
                [],
                ["Nenhum perfil de leitura compatível foi encontrado para este equipamento."]);
        }

        var evidence = new List<NetworkEvidence>();
        var warnings = new List<string>();
        foreach (var command in profile.Commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(command) || UnsafeCommand.IsMatch(command))
            {
                warnings.Add($"Comando bloqueado pelo modo somente leitura: {RedactCommand(command)}");
                continue;
            }

            try
            {
                var output = await _transport.ExecuteAsync(device.Host, _port, command, cancellationToken)
                    .ConfigureAwait(false);
                evidence.Add(new NetworkEvidence(command, output, DateTimeOffset.UtcNow));
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
            {
                warnings.Add($"Falha ao coletar '{RedactCommand(command)}': {ex.Message}");
            }
        }

        return new NetworkInspectionResult(
            device,
            DateTimeOffset.UtcNow,
            evidence.Count > 0 && warnings.Count == 0,
            evidence,
            warnings);
    }

    private static bool Matches(ReadOnlyCommandProfile profile, NetworkDeviceIdentity device) =>
        (profile.Kind == NetworkDeviceKind.Unknown || profile.Kind == device.Kind)
        && (string.IsNullOrWhiteSpace(profile.Vendor)
            || string.Equals(profile.Vendor, device.Vendor, StringComparison.OrdinalIgnoreCase));

    private static string RedactCommand(string? command) =>
        string.IsNullOrWhiteSpace(command) ? "(vazio)" : command.Length <= 80 ? command : command[..80] + "…";
}

/// <summary>Orquestrador que preserva a evidência de falta de suporte como estado incompleto.</summary>
public sealed class NetworkInspectionService : INetworkInspectionService
{
    private readonly IReadOnlyList<INetworkDeviceAdapter> _adapters;

    public NetworkInspectionService(IEnumerable<INetworkDeviceAdapter> adapters)
    {
        _adapters = (adapters ?? throw new ArgumentNullException(nameof(adapters))).ToArray();
    }

    public Task<NetworkInspectionResult> InspectAsync(
        NetworkDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var adapter = _adapters.FirstOrDefault(candidate => candidate.CanHandle(device));
        return adapter is null
            ? Task.FromResult(new NetworkInspectionResult(
                device,
                DateTimeOffset.UtcNow,
                false,
                [],
                ["Este fabricante/modelo ainda não possui adaptador de leitura habilitado."]))
            : adapter.InspectAsync(device, cancellationToken);
    }
}
