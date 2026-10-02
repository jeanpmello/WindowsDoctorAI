namespace WindowsDoctorAI.Domain;

/// <summary>Snapshot de inventário local. Valores ausentes permanecem nulos ou vazios; não são tratados como falhas do computador.</summary>
public sealed record ComputerInventory
{
    public DateTimeOffset CollectedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ComputerName { get; init; } = string.Empty;
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public OperatingSystemDetails OperatingSystem { get; init; } = new();
    public ProcessorDetails Processor { get; init; } = new();
    public ulong? InstalledMemoryBytes { get; init; }
    public IReadOnlyList<GraphicsAdapter> GraphicsAdapters { get; init; } = Array.Empty<GraphicsAdapter>();
    public IReadOnlyList<PhysicalDisk> PhysicalDisks { get; init; } = Array.Empty<PhysicalDisk>();
    public IReadOnlyList<DiskVolume> Disks { get; init; } = Array.Empty<DiskVolume>();
    public BiosDetails Bios { get; init; } = new();
    public string? FirmwareType { get; init; }
    public bool? SecureBootEnabled { get; init; }
    public TpmDetails Tpm { get; init; } = new();
    public string? UserName { get; init; }
    public string? Domain { get; init; }
    public IReadOnlyList<NetworkAdapterDetails> NetworkAdapters { get; init; } = Array.Empty<NetworkAdapterDetails>();
    public IReadOnlyList<string> IPv4Addresses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> IPv6Addresses { get; init; } = Array.Empty<string>();
}

public sealed record OperatingSystemDetails
{
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? Build { get; init; }
    public TimeSpan? Uptime { get; init; }
}

public sealed record ProcessorDetails
{
    public string? Name { get; init; }
    public int? Cores { get; init; }
    public int? LogicalProcessors { get; init; }
}

public sealed record GraphicsAdapter(string Name, ulong? MemoryBytes);
public sealed record PhysicalDisk(string Name, string? Model, ulong? SizeBytes, string? MediaType, string? InterfaceType);
public sealed record DiskVolume(string Name, string? Label, string? FileSystem, ulong? CapacityBytes, ulong? FreeBytes);
public sealed record BiosDetails(string? Manufacturer = null, string? Version = null, string? SerialNumber = null, DateTimeOffset? ReleaseDate = null);
public sealed record TpmDetails(bool? IsPresent = null, string? SpecificationVersion = null, string? Manufacturer = null, bool? IsEnabled = null, bool? IsActivated = null, bool? IsOwned = null);
public sealed record NetworkAdapterDetails(string Name, string Description, string Status, IReadOnlyList<string> IPv4Addresses, IReadOnlyList<string> IPv6Addresses);
