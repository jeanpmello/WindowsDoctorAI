using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Diagnostics.Eventing.Reader;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Coleta local, somente de leitura, para os plugins de diagnóstico do Windows.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiagnosticDataSource(ILogger<WindowsDiagnosticDataSource> logger) : IWindowsDiagnosticDataSource
{
    private const string WindowsUpdateLog = "Microsoft-Windows-WindowsUpdateClient/Operational";
    private const string WmiRoot = @"\\.\root\wmi";
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(14);
    public Task<WindowsUpdateProbe> ReadWindowsUpdateAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new WindowsUpdateProbe(
            Capture(ReadPendingUpdates, "Windows Update Agent"),
            Capture(ReadRebootPending, "Registro de reinicialização pendente"),
            Capture(() => ReadEvents(WindowsUpdateLog, RecentWindow, 100), "Log operacional do Windows Update"));
    }, cancellationToken);

    public Task<ProbeResult<IReadOnlyList<ServiceDiagnosticInfo>>> ReadServicesAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Capture(ReadServices, "Win32_Service");
    }, cancellationToken);

    public Task<ProbeResult<IReadOnlyList<DeviceDiagnosticInfo>>> ReadDevicesAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Capture(ReadDevices, "Win32_PnPEntity");
    }, cancellationToken);

    public Task<DiskProbe> ReadDisksAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new DiskProbe(
            Capture(ReadSmartStatuses, "MSStorageDriver_FailurePredictStatus"),
            Capture(ReadDiskHealth, "Win32_DiskDrive"),
            Capture(ReadVolumes, "Win32_LogicalDisk"),
            Capture(ReadTemperatures, "MSStorageDriver_ATAPISmartData"));
    }, cancellationToken);

    public Task<EventLogProbe> ReadEventLogsAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new EventLogProbe(
            Capture(() => ReadEvents("System", TimeSpan.FromDays(7), 100), "Event Viewer/System"),
            Capture(() => ReadEvents("Application", TimeSpan.FromDays(7), 100), "Event Viewer/Application"),
            Capture(() => ReadEvents(WindowsUpdateLog, TimeSpan.FromDays(7), 100), "Event Viewer/Windows Update"));
    }, cancellationToken);

    private ProbeResult<T> Capture<T>(Func<T> collector, string source)
    {
        try
        {
            return ProbeResult<T>.Available(collector());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning(exception, "A coleta de diagnóstico local {Source} não está disponível.", source);
            return ProbeResult<T>.Unavailable($"A fonte {source} está indisponível ou sem permissão ({exception.GetType().Name}).");
        }
    }

    private static IReadOnlyList<PendingWindowsUpdate> ReadPendingUpdates()
    {
        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
            ?? throw new PlatformNotSupportedException("Windows Update Agent não registrado.");
        object session = Activator.CreateInstance(sessionType)
            ?? throw new InvalidOperationException("Não foi possível iniciar uma sessão de consulta do Windows Update.");
        object? searcherObject = null;
        object? searchResultObject = null;
        object? updatesObject = null;
        try
        {
            dynamic sessionCom = session;
            searcherObject = sessionCom.CreateUpdateSearcher();
            dynamic searcher = searcherObject;
            searchResultObject = searcher.Search("IsInstalled=0 and IsHidden=0");
            dynamic searchResult = searchResultObject;
            updatesObject = searchResult.Updates;
            dynamic updates = updatesObject;
            var count = Convert.ToInt32(updates.Count, CultureInfo.InvariantCulture);
            var pending = new List<PendingWindowsUpdate>(count);
            for (var index = 1; index <= count; index++)
            {
                object? updateObject = null;
                try
                {
                    updateObject = updates.Item(index);
                    dynamic update = updateObject;
                    var title = Convert.ToString(update.Title, CultureInfo.InvariantCulture) ?? "Atualização sem título";
                    string? updateId = null;
                    try
                    {
                        updateId = Convert.ToString(update.Identity.UpdateID, CultureInfo.InvariantCulture);
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        // O identificador é complementar; a existência e o título da atualização continuam observáveis.
                    }
                    pending.Add(new PendingWindowsUpdate(title, updateId));
                }
                finally
                {
                    ReleaseComObject(updateObject);
                }
            }
            return pending;
        }
        finally
        {
            ReleaseComObject(updatesObject);
            ReleaseComObject(searchResultObject);
            ReleaseComObject(searcherObject);
            ReleaseComObject(session);
        }
    }

    private static bool ReadRebootPending()
    {
        using var cbs = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing", writable: false);
        using var windowsUpdate = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update", writable: false);
        using var sessionManager = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Session Manager", writable: false);
        using var componentKey = cbs?.OpenSubKey("RebootPending", writable: false);
        using var updateKey = windowsUpdate?.OpenSubKey("RebootRequired", writable: false);
        var componentPending = componentKey is not null;
        var updatePending = updateKey is not null;
        var fileRenameValue = sessionManager?.GetValue("PendingFileRenameOperations", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var fileRenamePending = fileRenameValue switch
        {
            string[] values => values.Length > 0,
            string value => !string.IsNullOrWhiteSpace(value),
            _ => false
        };
        return componentPending || updatePending || fileRenamePending;
    }

    private static IReadOnlyList<ServiceDiagnosticInfo> ReadServices() => ReadWmiRows(
            "SELECT Name, DisplayName, State, StartMode, Dependencies FROM Win32_Service")
        .Select(row => new ServiceDiagnosticInfo(
            GetText(row, "Name") ?? string.Empty,
            GetText(row, "DisplayName") ?? GetText(row, "Name") ?? string.Empty,
            GetText(row, "State") ?? "Unknown",
            GetText(row, "StartMode") ?? "Unknown",
            row.TryGetValue("Dependencies", out var dependencies) && dependencies is IEnumerable<string> names
                ? names.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray()
                : Array.Empty<string>()))
        .ToArray();

    private static IReadOnlyList<DeviceDiagnosticInfo> ReadDevices() => ReadWmiRows(
            "SELECT Name, PNPDeviceID, ConfigManagerErrorCode, Status FROM Win32_PnPEntity")
        .Select(row => new DeviceDiagnosticInfo(
            GetText(row, "Name") ?? "Dispositivo sem nome",
            GetText(row, "PNPDeviceID") ?? "Identificador indisponível",
            GetInt(row, "ConfigManagerErrorCode"),
            GetText(row, "Status")))
        .ToArray();

    private static IReadOnlyList<DiskSmartStatus> ReadSmartStatuses() => ReadWmiRows(
            "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", WmiRoot)
        .Select(row => new DiskSmartStatus(
            GetText(row, "InstanceName") ?? "Disco sem identificador",
            GetBoolean(row, "PredictFailure")))
        .ToArray();

    private static IReadOnlyList<DiskHealthInfo> ReadDiskHealth() => ReadWmiRows(
            "SELECT DeviceID, Model, Status FROM Win32_DiskDrive")
        .Select(row => new DiskHealthInfo(
            GetText(row, "DeviceID") ?? "Disco sem identificador",
            GetText(row, "Model") ?? "Modelo indisponível",
            GetText(row, "Status")))
        .ToArray();

    private static IReadOnlyList<DiskVolumeInfo> ReadVolumes() => ReadWmiRows(
            "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3")
        .Select(row => new DiskVolumeInfo(
            GetText(row, "DeviceID") ?? "Volume sem identificador",
            GetUInt64(row, "Size"),
            GetUInt64(row, "FreeSpace")))
        .ToArray();

    private static IReadOnlyList<DiskTemperatureInfo> ReadTemperatures()
    {
        var rows = ReadWmiRows("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData", WmiRoot);
        var temperatures = new List<DiskTemperatureInfo>();
        foreach (var row in rows)
        {
            if (!row.TryGetValue("VendorSpecific", out var dataObject) || dataObject is not byte[] data) continue;
            for (var offset = 2; offset + 12 <= data.Length; offset += 12)
            {
                var attributeId = data[offset];
                if (attributeId is not (194 or 190)) continue;
                var celsius = data[offset + 5];
                if (celsius is < 1 or > 100) continue;
                temperatures.Add(new DiskTemperatureInfo(GetText(row, "InstanceName") ?? "Disco sem identificador", celsius));
                break;
            }
        }
        return temperatures;
    }

    private static IReadOnlyList<DiagnosticEvent> ReadEvents(string logName, TimeSpan lookback, int maximum)
    {
        var milliseconds = Math.Max(1, (long)lookback.TotalMilliseconds);
        var xpath = $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {milliseconds}]]]";
        var query = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        var events = new List<DiagnosticEvent>(Math.Min(maximum, 100));
        while (events.Count < maximum)
        {
            using var record = reader.ReadEvent();
            if (record is null) break;
            var level = record.Level;
            if (level is not (1 or 2 or 3)) continue;
            string? message;
            try { message = record.FormatDescription(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { message = null; }
            events.Add(new DiagnosticEvent(
                record.Id,
                logName,
                record.ProviderName ?? "Provedor indisponível",
                level.Value,
                record.TimeCreated is DateTime created ? new DateTimeOffset(created) : null,
                message));
        }
        return events;
    }

    private static IReadOnlyList<Dictionary<string, object?>> ReadWmiRows(string query, string? scope = null)
    {
        using var searcher = scope is null
            ? new ManagementObjectSearcher(query)
            : new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementBaseObject item in results)
        {
            using (item)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (PropertyData property in item.Properties)
                {
                    row[property.Name] = property.Value;
                }
                rows.Add(row);
            }
        }
        return rows;
    }

    private static string? GetText(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null
            ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim()
            : null;

    private static int? GetInt(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : null;

    private static ulong? GetUInt64(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null
            ? Convert.ToUInt64(value, CultureInfo.InvariantCulture)
            : null;

    private static bool? GetBoolean(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null
            ? Convert.ToBoolean(value, CultureInfo.InvariantCulture)
            : null;

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}

/// <summary>Fonte usada fora do Windows; cada dado dependente de APIs do sistema é explicitamente indisponível.</summary>
public sealed class UnsupportedWindowsDiagnosticDataSource : IWindowsDiagnosticDataSource
{
    private const string Reason = "Esta verificação depende de APIs locais do Windows e não foi executada neste sistema.";

    public Task<WindowsUpdateProbe> ReadWindowsUpdateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new WindowsUpdateProbe(
            ProbeResult<IReadOnlyList<PendingWindowsUpdate>>.Unavailable(Reason),
            ProbeResult<bool>.Unavailable(Reason),
            ProbeResult<IReadOnlyList<DiagnosticEvent>>.Unavailable(Reason)));
    }

    public Task<ProbeResult<IReadOnlyList<ServiceDiagnosticInfo>>> ReadServicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<IReadOnlyList<ServiceDiagnosticInfo>>(cancellationToken));

    public Task<ProbeResult<IReadOnlyList<DeviceDiagnosticInfo>>> ReadDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<IReadOnlyList<DeviceDiagnosticInfo>>(cancellationToken));

    public Task<DiskProbe> ReadDisksAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DiskProbe(
            Unavailable<IReadOnlyList<DiskSmartStatus>>(),
            Unavailable<IReadOnlyList<DiskHealthInfo>>(),
            Unavailable<IReadOnlyList<DiskVolumeInfo>>(),
            Unavailable<IReadOnlyList<DiskTemperatureInfo>>()));
    }

    public Task<EventLogProbe> ReadEventLogsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EventLogProbe(
            Unavailable<IReadOnlyList<DiagnosticEvent>>(),
            Unavailable<IReadOnlyList<DiagnosticEvent>>(),
            Unavailable<IReadOnlyList<DiagnosticEvent>>()));
    }

    private static ProbeResult<T> Unavailable<T>(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ProbeResult<T>.Unavailable(Reason);
    }
}
