namespace WindowsDoctorAI.Diagnostics;

/// <summary>Representa um valor coletado ou uma lacuna explicitamente identificada.</summary>
public sealed record ProbeResult<T>(T? Value, bool IsAvailable, string? UnavailableReason = null)
{
    public static ProbeResult<T> Available(T value) => new(value, true);

    public static ProbeResult<T> Unavailable(string reason) => new(default, false, reason);
}

public sealed record PendingWindowsUpdate(string Title, string? UpdateId);
public sealed record ServiceDiagnosticInfo(string Name, string DisplayName, string State, string StartMode, IReadOnlyList<string> Dependencies);
public sealed record DeviceDiagnosticInfo(string Name, string DeviceId, int? ErrorCode, string? Status);
public sealed record DiskSmartStatus(string InstanceName, bool? PredictFailure);
public sealed record DiskHealthInfo(string DeviceId, string Model, string? Status);
public sealed record DiskVolumeInfo(string Name, ulong? CapacityBytes, ulong? FreeBytes);
public sealed record DiskTemperatureInfo(string InstanceName, int Celsius);
public sealed record DiagnosticEvent(int EventId, string LogName, string Provider, int Level, DateTimeOffset? Timestamp, string? Message);

public sealed record WindowsUpdateProbe(
    ProbeResult<IReadOnlyList<PendingWindowsUpdate>> PendingUpdates,
    ProbeResult<bool> RebootPending,
    ProbeResult<IReadOnlyList<DiagnosticEvent>> RecentEvents);

public sealed record DiskProbe(
    ProbeResult<IReadOnlyList<DiskSmartStatus>> SmartStatuses,
    ProbeResult<IReadOnlyList<DiskHealthInfo>> DiskHealth,
    ProbeResult<IReadOnlyList<DiskVolumeInfo>> Volumes,
    ProbeResult<IReadOnlyList<DiskTemperatureInfo>> Temperatures);

public sealed record EventLogProbe(
    ProbeResult<IReadOnlyList<DiagnosticEvent>> System,
    ProbeResult<IReadOnlyList<DiagnosticEvent>> Application,
    ProbeResult<IReadOnlyList<DiagnosticEvent>> WindowsUpdate);

/// <summary>Porta das consultas locais; permite exercitar os plugins sem Windows ou hardware real.</summary>
public interface IWindowsDiagnosticDataSource
{
    Task<WindowsUpdateProbe> ReadWindowsUpdateAsync(CancellationToken cancellationToken = default);
    Task<ProbeResult<IReadOnlyList<ServiceDiagnosticInfo>>> ReadServicesAsync(CancellationToken cancellationToken = default);
    Task<ProbeResult<IReadOnlyList<DeviceDiagnosticInfo>>> ReadDevicesAsync(CancellationToken cancellationToken = default);
    Task<DiskProbe> ReadDisksAsync(CancellationToken cancellationToken = default);
    Task<EventLogProbe> ReadEventLogsAsync(CancellationToken cancellationToken = default);
}
