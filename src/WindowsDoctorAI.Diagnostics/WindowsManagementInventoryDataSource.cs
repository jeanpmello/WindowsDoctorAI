using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Fonte de inventário somente de leitura que usa WMI, registro local e APIs de rede do Windows.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsManagementInventoryDataSource(ILogger<WindowsManagementInventoryDataSource> logger) : IComputerInventoryDataSource
{
    private const string TpmNamespace = @"\\.\root\CIMV2\Security\MicrosoftTpm";

    public Task<ComputerInventory> CollectAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("A coleta de inventário requer Windows.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        // System.Management é síncrono; movemos as chamadas WMI para uma tarefa cancelável.
        return Task.Run(() => Collect(cancellationToken), cancellationToken);
    }

    private ComputerInventory Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var system = First("SELECT Manufacturer, Model, Domain, TotalPhysicalMemory FROM Win32_ComputerSystem");
        var biosRow = First("SELECT Manufacturer, SMBIOSBIOSVersion, Version, SerialNumber, ReleaseDate FROM Win32_BIOS");
        var osRow = First(WindowsOperatingSystemMetadata.WmiQuery);
        var processorRows = Rows("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
        var gpuRows = Rows("SELECT Name, AdapterRAM FROM Win32_VideoController");
        var physicalDiskRows = Rows("SELECT DeviceID, Model, Size, MediaType, InterfaceType FROM Win32_DiskDrive");
        var diskRows = Rows("SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace, DriveType FROM Win32_LogicalDisk");
        var tpmRows = Rows("SELECT SpecVersion, ManufacturerIdTxt, IsEnabled_InitialValue, IsActivated_InitialValue, IsOwned_InitialValue FROM Win32_Tpm", TpmNamespace);
        var (adapters, ipv4, ipv6) = ReadNetworkAdapters();

        cancellationToken.ThrowIfCancellationRequested();
        var biosRelease = ParseWmiDate(Text(biosRow, "ReleaseDate"));
        var lastBoot = ParseWmiDate(Text(osRow, "LastBootUpTime"));
        TimeSpan? uptime = lastBoot is null ? null : DateTimeOffset.Now - lastBoot.Value;
        var firmwareType = ReadFirmwareType();
        var secureBootState = firmwareType == "BIOS legado" ? false : ReadSecureBootState();
        var physicalMemory = ToUInt64(system, "TotalPhysicalMemory");
        var memoryModules = Rows("SELECT Capacity FROM Win32_PhysicalMemory");
        var moduleCapacities = (memoryModules ?? []).Select(row => ToUInt64(row, "Capacity")).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        ulong? moduleTotal = moduleCapacities.Length == 0 ? null : moduleCapacities.Aggregate(0UL, (sum, value) => checked(sum + value));
        if (moduleTotal is > 0) physicalMemory = moduleTotal;

        var tpm = tpmRows is null
            ? new TpmDetails()
            : new TpmDetails(
                IsPresent: tpmRows.Count > 0,
                SpecificationVersion: FirstText(tpmRows, "SpecVersion"),
                Manufacturer: FirstText(tpmRows, "ManufacturerIdTxt"),
                IsEnabled: FirstBoolean(tpmRows, "IsEnabled_InitialValue"),
                IsActivated: FirstBoolean(tpmRows, "IsActivated_InitialValue"),
                IsOwned: FirstBoolean(tpmRows, "IsOwned_InitialValue"));

        return new ComputerInventory
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            ComputerName = Environment.MachineName,
            Manufacturer = Text(system, "Manufacturer"),
            Model = Text(system, "Model"),
            SerialNumber = Text(biosRow, "SerialNumber"),
            OperatingSystem = new OperatingSystemDetails
            {
                Name = Text(osRow, "Caption"),
                Version = Text(osRow, "Version"),
                Build = OperatingSystemBuildNumber.Normalize(RawText(osRow, "BuildNumber")),
                ProductType = WindowsOperatingSystemMetadata.ParseProductType(RawText(osRow, "ProductType")),
                Uptime = uptime is { Ticks: >= 0 } ? uptime : null
            },
            Processor = new ProcessorDetails
            {
                Name = JoinDistinct(processorRows, "Name"),
                Cores = SumInt(processorRows, "NumberOfCores"),
                LogicalProcessors = SumInt(processorRows, "NumberOfLogicalProcessors")
            },
            InstalledMemoryBytes = physicalMemory,
            GraphicsAdapters = (gpuRows ?? []).Select(row => new GraphicsAdapter(Text(row, "Name") ?? "Desconhecido", ToUInt64(row, "AdapterRAM"))).ToArray(),
            PhysicalDisks = (physicalDiskRows ?? []).Select(row => new PhysicalDisk(
                Text(row, "DeviceID") ?? string.Empty, Text(row, "Model"), ToUInt64(row, "Size"), Text(row, "MediaType"), Text(row, "InterfaceType"))).ToArray(),
            Disks = (diskRows ?? []).Where(IsLocalOrRemovable).Select(row => new DiskVolume(
                Text(row, "DeviceID") ?? string.Empty, Text(row, "VolumeName"), Text(row, "FileSystem"),
                ToUInt64(row, "Size"), ToUInt64(row, "FreeSpace"))).ToArray(),
            Bios = new BiosDetails(Text(biosRow, "Manufacturer"), Text(biosRow, "SMBIOSBIOSVersion") ?? Text(biosRow, "Version"),
                Text(biosRow, "SerialNumber"), biosRelease),
            FirmwareType = firmwareType,
            SecureBootEnabled = secureBootState,
            Tpm = tpm,
            UserName = string.IsNullOrWhiteSpace(Environment.UserName) ? null : Environment.UserName,
            Domain = Text(system, "Domain") ?? Environment.UserDomainName,
            NetworkAdapters = adapters,
            IPv4Addresses = ipv4,
            IPv6Addresses = ipv6
        };
    }

    private Dictionary<string, string?>? First(string query, string? scope = null) => Rows(query, scope)?.FirstOrDefault();

    private List<Dictionary<string, string?>>? Rows(string query, string? scope = null)
    {
        try
        {
            using var searcher = scope is null
                ? new ManagementObjectSearcher(query)
                : new ManagementObjectSearcher(scope, query);
            using var results = searcher.Get();
            var rows = new List<Dictionary<string, string?>>();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyData property in item.Properties)
                    {
                        values[property.Name] = property.Value is null
                            ? null
                            : Convert.ToString(property.Value, CultureInfo.InvariantCulture);
                    }
                    rows.Add(values);
                }
            }
            return rows;
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            logger.LogWarning("Uma consulta local WMI não pôde ser concluída; detalhes omitidos por privacidade.");
            return null;
        }
    }

    private (IReadOnlyList<NetworkAdapterDetails> Adapters, IReadOnlyList<string> IPv4, IReadOnlyList<string> IPv6) ReadNetworkAdapters()
    {
        try
        {
            var all = new List<NetworkAdapterDetails>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                var addresses = adapter.GetIPProperties().UnicastAddresses
                    .Select(address => address.Address)
                    .Where(address => !IPAddress.IsLoopback(address) && !address.IsIPv6Multicast)
                    .ToArray();
                all.Add(new NetworkAdapterDetails(adapter.Name, adapter.Description, adapter.OperationalStatus.ToString(),
                    addresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork).Select(address => address.ToString()).ToArray(),
                    addresses.Where(address => address.AddressFamily == AddressFamily.InterNetworkV6).Select(address => address.ToString()).ToArray()));
            }

            return (all,
                all.SelectMany(adapter => adapter.IPv4Addresses).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                all.SelectMany(adapter => adapter.IPv6Addresses).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (NetworkInformationException)
        {
            logger.LogWarning("Não foi possível enumerar os adaptadores de rede locais; detalhes omitidos por privacidade.");
            return (Array.Empty<NetworkAdapterDetails>(), Array.Empty<string>(), Array.Empty<string>());
        }
    }

    private string? ReadFirmwareType()
    {
        if (GetFirmwareType(out var type)) return type switch { FirmwareType.Uefi => "UEFI", FirmwareType.Bios => "BIOS legado", _ => "Desconhecido" };
        logger.LogDebug("A API GetFirmwareType não conseguiu determinar o modo de firmware (erro {ErrorCode}).", Marshal.GetLastWin32Error());
        return null;
    }

    private bool? ReadSecureBootState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", writable: false);
            return key?.GetValue("UEFISecureBootEnabled") is int value ? value == 1 : null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            logger.LogDebug(exception, "Não foi possível ler o estado local do Secure Boot.");
            return null;
        }
    }

    private static bool IsLocalOrRemovable(IReadOnlyDictionary<string, string?> row) => ParseInt(Text(row, "DriveType")) is 2 or 3;
    private static string? Text(IReadOnlyDictionary<string, string?>? row, string key) => row is not null && row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
    private static string? FirstText(IEnumerable<IReadOnlyDictionary<string, string?>> rows, string key) => rows.Select(row => Text(row, key)).FirstOrDefault(value => value is not null);
    private static bool? FirstBoolean(IEnumerable<IReadOnlyDictionary<string, string?>> rows, string key)
    {
        var value = FirstText(rows, key);
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }
    private static int? ParseInt(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    private static int? SumInt(IEnumerable<IReadOnlyDictionary<string, string?>>? rows, string key)
    {
        if (rows is null) return null;
        var numbers = rows.Select(row => ParseInt(Text(row, key))).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return numbers.Length == 0 ? null : numbers.Sum();
    }
    private static ulong? ToUInt64(IReadOnlyDictionary<string, string?>? row, string key) =>
        ulong.TryParse(Text(row, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    private static string? RawText(IReadOnlyDictionary<string, string?>? row, string key) =>
        row is not null && row.TryGetValue(key, out var value) ? value : null;
    private static string? JoinDistinct(IEnumerable<IReadOnlyDictionary<string, string?>>? rows, string key)
    {
        var names = rows?.Select(row => Text(row, key)).Where(value => value is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return names is { Length: > 0 } ? string.Join("; ", names!) : null;
    }
    private static DateTimeOffset? ParseWmiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(value)); }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (FormatException) { return null; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out FirmwareType firmwareType);

    private enum FirmwareType : uint { Unknown = 0, Bios = 1, Uefi = 2 }
}
