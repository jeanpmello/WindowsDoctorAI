using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.App;

/// <summary>Estado de apresentação do dashboard. Não contém chamadas diretas a WMI ou SQLite.</summary>
public partial class HomeViewModel(
    RunComputerInventoryDiagnosticUseCase runDiagnostic,
    IDiagnosticRunRepository history,
    ILogger<HomeViewModel> logger) : ObservableObject
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusMessage = "Inicie um diagnóstico para coletar os dados deste computador.";
    [ObservableProperty] private string _lastDiagnosticText = "Nenhum diagnóstico registrado nesta sessão.";
    [ObservableProperty] private int _healthScore = 95;
    [ObservableProperty] private string _computerName = "Não coletado";
    [ObservableProperty] private string _manufacturerModel = "Não coletado";
    [ObservableProperty] private string _serialNumber = "Não coletado";
    [ObservableProperty] private string _operatingSystem = "Não coletado";
    [ObservableProperty] private string _processor = "Não coletado";
    [ObservableProperty] private string _memory = "Não coletado";
    [ObservableProperty] private string _graphics = "Não coletado";
    [ObservableProperty] private string _disks = "Não coletado";
    [ObservableProperty] private string _bios = "Não coletado";
    [ObservableProperty] private string _firmware = "Não coletado";
    [ObservableProperty] private string _tpm = "Não coletado";
    [ObservableProperty] private string _secureBoot = "Não coletado";
    [ObservableProperty] private string _userAndDomain = "Não coletado";
    [ObservableProperty] private string _uptime = "Não coletado";
    [ObservableProperty] private string _ipv4 = "Não coletado";
    [ObservableProperty] private string _ipv6 = "Não coletado";
    [ObservableProperty] private string _networkAdapters = "Não coletado";

    partial void OnIsScanningChanged(bool value) => StartDiagnosticCommand.NotifyCanExecuteChanged();

    private bool CanStartDiagnostic() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanStartDiagnostic))]
    private async Task StartDiagnosticAsync()
    {
        IsScanning = true;
        StatusMessage = "Coletando informações locais. Nenhuma correção será aplicada.";
        try
        {
            var outcome = await runDiagnostic.ExecuteAsync();
            HealthScore = outcome.Run.HealthScore.Value;
            DisplayInventory(outcome.Run.Inventory);
            LastDiagnosticText = $"Concluído às {outcome.Run.CompletedAtUtc.ToLocalTime():G} · {FormatDuration(outcome.Run.Duration)}";
            StatusMessage = outcome.PersistenceWarning ?? (outcome.HistorySaved
                ? "Diagnóstico concluído e salvo no histórico local."
                : "Diagnóstico concluído. O histórico está desativado nas Configurações.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "A coleta de inventário falhou.");
            StatusMessage = $"Não foi possível concluir o diagnóstico: {exception.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task LoadLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var latest = await history.GetLatestAsync(cancellationToken);
            if (latest is null) return;
            HealthScore = latest.HealthScore.Value;
            DisplayInventory(latest.Inventory);
            LastDiagnosticText = $"Concluído às {latest.CompletedAtUtc.ToLocalTime():G} · {FormatDuration(latest.Duration)}";
            StatusMessage = "Último diagnóstico carregado do histórico local.";
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "O último diagnóstico não pôde ser carregado do histórico local.");
            StatusMessage = "Não foi possível carregar o histórico local. Você ainda pode iniciar um novo diagnóstico.";
        }
    }

    private void DisplayInventory(ComputerInventory inventory)
    {
        ComputerName = Text(inventory.ComputerName);
        ManufacturerModel = $"{Text(inventory.Manufacturer)} · {Text(inventory.Model)}";
        SerialNumber = Text(inventory.SerialNumber);
        OperatingSystem = $"{Text(inventory.OperatingSystem.Name)} · versão {Text(inventory.OperatingSystem.Version)} · build {Text(inventory.OperatingSystem.Build)}";
        Processor = $"{Text(inventory.Processor.Name)} · {Text(inventory.Processor.Cores)} núcleos / {Text(inventory.Processor.LogicalProcessors)} processadores lógicos";
        Memory = inventory.InstalledMemoryBytes is ulong bytes ? $"{bytes / 1_073_741_824d:N1} GB" : "Indisponível";
        Graphics = inventory.GraphicsAdapters.Count == 0 ? "Indisponível" : string.Join(Environment.NewLine, inventory.GraphicsAdapters.Select(item => item.MemoryBytes is ulong gpuBytes
            ? $"{item.Name} · {gpuBytes / 1_073_741_824d:N1} GB"
            : item.Name));
        var physicalDisks = inventory.PhysicalDisks.Select(disk =>
        {
            var size = disk.SizeBytes is ulong bytes ? $"{bytes / 1_073_741_824d:N0} GB" : "capacidade indisponível";
            return $"{Text(disk.Model)} · {size} · {Text(disk.MediaType)} · {Text(disk.InterfaceType)}";
        });
        var volumes = inventory.Disks.Select(disk =>
        {
            var capacity = disk.CapacityBytes is ulong total ? $"{total / 1_073_741_824d:N0} GB" : "capacidade indisponível";
            var free = disk.FreeBytes is ulong available ? $"{available / 1_073_741_824d:N0} GB livres" : "espaço livre indisponível";
            return $"{disk.Name} {Text(disk.Label)} · {capacity} · {free} · {Text(disk.FileSystem)}";
        });
        var diskSummaries = physicalDisks.Concat(volumes).ToArray();
        Disks = diskSummaries.Length == 0 ? "Indisponível" : string.Join(Environment.NewLine, diskSummaries);
        Bios = $"{Text(inventory.Bios.Manufacturer)} · {Text(inventory.Bios.Version)} · série {Text(inventory.Bios.SerialNumber)}";
        Firmware = Text(inventory.FirmwareType);
        Tpm = inventory.Tpm.IsPresent switch
        {
            true => $"Presente · versão {Text(inventory.Tpm.SpecificationVersion)} · fabricante {Text(inventory.Tpm.Manufacturer)} · habilitado {BoolText(inventory.Tpm.IsEnabled)} · ativado {BoolText(inventory.Tpm.IsActivated)}",
            false => "Não detectado",
            _ => "Indisponível"
        };
        SecureBoot = BoolText(inventory.SecureBootEnabled);
        UserAndDomain = $"{Text(inventory.UserName)} · domínio/grupo {Text(inventory.Domain)}";
        Uptime = inventory.OperatingSystem.Uptime is TimeSpan span ? $"{span.Days}d {span.Hours}h {span.Minutes}min" : "Indisponível";
        IPv4 = Join(inventory.IPv4Addresses);
        IPv6 = Join(inventory.IPv6Addresses);
        NetworkAdapters = inventory.NetworkAdapters.Count == 0 ? "Indisponível" : string.Join(Environment.NewLine,
            inventory.NetworkAdapters.Select(adapter => $"{adapter.Name} · {adapter.Description} · {adapter.Status}"));
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "Indisponível" : value;
    private static string BoolText(bool? value) => value switch { true => "Ativado", false => "Desativado", _ => "Indisponível" };
    private static string Join(IReadOnlyList<string> addresses) => addresses.Count == 0 ? "Nenhum endereço encontrado" : string.Join(" · ", addresses);
    private static string FormatDuration(TimeSpan duration) => duration.TotalSeconds < 1 ? "menos de 1 segundo" : $"{duration.TotalSeconds.ToString("N1", BrazilianCulture)} s";
}
