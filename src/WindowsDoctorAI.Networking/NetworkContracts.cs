namespace WindowsDoctorAI.Networking;

/// <summary>Categoria lógica do equipamento de infraestrutura.</summary>
public enum NetworkDeviceKind
{
    Unknown,
    Firewall,
    Router,
    Switch,
    AccessPoint,
    Controller
}

/// <summary>Identidade não secreta de um equipamento descoberto.</summary>
public sealed record NetworkDeviceIdentity(
    string DeviceId,
    string Host,
    NetworkDeviceKind Kind = NetworkDeviceKind.Unknown,
    string? Vendor = null,
    string? Model = null,
    string? Firmware = null);

/// <summary>Comandos de leitura aprovados para um perfil de equipamento.</summary>
public sealed record ReadOnlyCommandProfile(
    string Name,
    IReadOnlyList<string> Commands,
    string? Vendor = null,
    NetworkDeviceKind Kind = NetworkDeviceKind.Unknown);

/// <summary>Resultado de uma inspeção sem indicar saúde quando a coleta falha.</summary>
public sealed record NetworkInspectionResult(
    NetworkDeviceIdentity Device,
    DateTimeOffset CollectedAtUtc,
    bool IsComplete,
    IReadOnlyList<NetworkEvidence> Evidence,
    IReadOnlyList<string> Warnings);

/// <summary>Evidência coletada; o conteúdo pode conter configuração sensível e não deve ir para a IA sem redação.</summary>
public sealed record NetworkEvidence(
    string Source,
    string Content,
    DateTimeOffset CollectedAtUtc,
    bool RequiresRedaction = true);

/// <summary>Transporte substituível usado pelo adaptador SSH/CLI. A implementação deve recusar comandos de escrita.</summary>
public interface IReadOnlyCommandTransport
{
    Task<string> ExecuteAsync(string host, int port, string command, CancellationToken cancellationToken = default);
}

/// <summary>Adaptador de inspeção de um fabricante ou família de equipamentos.</summary>
public interface INetworkDeviceAdapter
{
    string Name { get; }
    bool CanHandle(NetworkDeviceIdentity device);
    Task<NetworkInspectionResult> InspectAsync(
        NetworkDeviceIdentity device,
        CancellationToken cancellationToken = default);
}

/// <summary>Orquestra a inspeção pelo primeiro adaptador compatível, sem executar alterações.</summary>
public interface INetworkInspectionService
{
    Task<NetworkInspectionResult> InspectAsync(
        NetworkDeviceIdentity device,
        CancellationToken cancellationToken = default);
}
