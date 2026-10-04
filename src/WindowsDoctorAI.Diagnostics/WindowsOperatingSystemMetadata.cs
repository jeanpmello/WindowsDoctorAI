using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Diagnostics;

/// <summary>Metadados e conversões puros do inventário WMI do sistema operacional.</summary>
internal static class WindowsOperatingSystemMetadata
{
    internal const string WmiQuery = "SELECT Caption, Version, BuildNumber, ProductType, LastBootUpTime FROM Win32_OperatingSystem";

    internal static OperatingSystemProductType? ParseProductType(string? value) => value switch
    {
        "1" => OperatingSystemProductType.Workstation,
        "2" => OperatingSystemProductType.DomainController,
        "3" => OperatingSystemProductType.Server,
        _ => null
    };
}
