using WindowsDoctorAI.Networking;

namespace WindowsDoctorAI.Tests.Networking;

public sealed class ReadOnlyNetworkAdapterTests
{
    private sealed class FakeTransport : IReadOnlyCommandTransport
    {
        public List<string> Commands { get; } = [];

        public Task<string> ExecuteAsync(string host, int port, string command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult($"host={host}; command={command}");
        }
    }

    [Fact]
    public async Task Inspect_executes_only_allowlisted_read_commands()
    {
        var transport = new FakeTransport();
        var adapter = new ReadOnlyNetworkAdapter(transport, [
            new ReadOnlyCommandProfile("MikroTik básico", ["/system resource print", "/interface print"], "MikroTik", NetworkDeviceKind.Router)
        ]);

        var result = await adapter.InspectAsync(new NetworkDeviceIdentity("r1", "192.0.2.1", NetworkDeviceKind.Router, "MikroTik"));

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Evidence.Count);
        Assert.Equal(["/system resource print", "/interface print"], transport.Commands);
    }

    [Fact]
    public async Task Inspect_blocks_write_like_commands_and_marks_result_incomplete()
    {
        var transport = new FakeTransport();
        var adapter = new ReadOnlyNetworkAdapter(transport, [
            new ReadOnlyCommandProfile("Cisco", ["show version", "configure terminal", "show interfaces"], "Cisco", NetworkDeviceKind.Switch)
        ]);

        var result = await adapter.InspectAsync(new NetworkDeviceIdentity("sw1", "198.51.100.10", NetworkDeviceKind.Switch, "Cisco"));

        Assert.False(result.IsComplete);
        Assert.Equal(["show version", "show interfaces"], transport.Commands);
        Assert.Contains(result.Warnings, warning => warning.Contains("bloqueado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Service_reports_unsupported_device_without_calling_transport()
    {
        var transport = new FakeTransport();
        var adapter = new ReadOnlyNetworkAdapter(transport, [
            new ReadOnlyCommandProfile("Fortinet", ["get system status"], "Fortinet", NetworkDeviceKind.Firewall)
        ]);
        var service = new NetworkInspectionService([adapter]);

        var result = await service.InspectAsync(new NetworkDeviceIdentity("ap1", "203.0.113.5", NetworkDeviceKind.AccessPoint, "Aruba"));

        Assert.False(result.IsComplete);
        Assert.Empty(result.Evidence);
        Assert.Empty(transport.Commands);
        Assert.Contains("não possui adaptador", result.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }
}
