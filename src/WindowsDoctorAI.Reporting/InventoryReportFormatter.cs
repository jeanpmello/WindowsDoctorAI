using System.Globalization;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Reporting;

/// <summary>Gera texto simples sem transmitir o inventário para fora do dispositivo.</summary>
public sealed class InventoryReportFormatter
{
    public string Format(ComputerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var lines = new List<string>
        {
            "Windows Doctor AI — Inventário do computador",
            $"Coletado (UTC): {inventory.CollectedAtUtc:O}",
            $"Computador: {inventory.ComputerName}",
            $"Fabricante / modelo: {Value(inventory.Manufacturer)} / {Value(inventory.Model)}",
            $"Número de série: {Value(inventory.SerialNumber)}",
            $"Sistema operacional: {Value(inventory.OperatingSystem.Name)} (build {Value(inventory.OperatingSystem.Build)})",
            $"CPU: {Value(inventory.Processor.Name)}; núcleos: {Value(inventory.Processor.Cores)}",
            $"RAM instalada: {(inventory.InstalledMemoryBytes is ulong bytes ? $"{bytes / 1_073_741_824d:N1} GB" : "Indisponível")}",
            $"BIOS: {Value(inventory.Bios.Manufacturer)} {Value(inventory.Bios.Version)}",
            $"Firmware: {Value(inventory.FirmwareType)}; Secure Boot: {inventory.SecureBootEnabled?.ToString(culture) ?? "Indisponível"}",
            $"TPM: {inventory.Tpm.IsPresent?.ToString(culture) ?? "Indisponível"}",
            $"Usuário / domínio: {Value(inventory.UserName)} / {Value(inventory.Domain)}",
            $"IPv4: {string.Join(", ", inventory.IPv4Addresses)}",
            $"IPv6: {string.Join(", ", inventory.IPv6Addresses)}",
            "GPU: " + (inventory.GraphicsAdapters.Count == 0 ? "Indisponível" : string.Join("; ", inventory.GraphicsAdapters.Select(item => item.Name))),
            "Discos físicos: " + (inventory.PhysicalDisks.Count == 0 ? "Indisponível" : string.Join("; ", inventory.PhysicalDisks.Select(item => item.Model ?? item.Name))),
            "Volumes: " + (inventory.Disks.Count == 0 ? "Indisponível" : string.Join("; ", inventory.Disks.Select(item => item.Name)))
        };
        return string.Join(Environment.NewLine, lines);
    }

    private static string Value<T>(T? value) => value?.ToString() is { Length: > 0 } text ? text : "Indisponível";
}
