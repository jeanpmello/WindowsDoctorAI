namespace WindowsDoctorAI.Networking;

/// <summary>Perfis iniciais de consulta; não contêm credenciais nem comandos de alteração.</summary>
public static class ReadOnlyProfileCatalog
{
    public static IReadOnlyList<ReadOnlyCommandProfile> CreateDefaults() =>
    [
        new(
            "MikroTik RouterOS",
            [
                "/system identity print",
                "/system resource print",
                "/interface print",
                "/ip address print",
                "/ip route print",
                "/ip firewall filter print terse"
            ],
            "MikroTik",
            NetworkDeviceKind.Router),
        new(
            "Fortinet FortiGate",
            [
                "get system status",
                "get system interface",
                "get router info routing-table all",
                "show firewall policy"
            ],
            "Fortinet",
            NetworkDeviceKind.Firewall),
        new(
            "Cisco IOS",
            [
                "show version",
                "show inventory",
                "show interfaces status",
                "show vlan brief",
                "show ip interface brief",
                "show ip route"
            ],
            "Cisco",
            NetworkDeviceKind.Switch),
        new(
            "pfSense",
            [
                "uname -a",
                "ifconfig",
                "netstat -rn",
                "pfctl -sr"
            ],
            "pfSense",
            NetworkDeviceKind.Firewall),
        new(
            "OPNsense",
            [
                "uname -a",
                "ifconfig",
                "netstat -rn",
                "pfctl -sr"
            ],
            "OPNsense",
            NetworkDeviceKind.Firewall),
        new(
            "Ubiquiti EdgeOS",
            [
                "show version",
                "show interfaces",
                "show ip route",
                "show configuration commands"
            ],
            "Ubiquiti",
            NetworkDeviceKind.Router),
        new(
            "ArubaOS",
            [
                "show version",
                "show interfaces brief",
                "show vlan",
                "show ip route"
            ],
            "Aruba",
            NetworkDeviceKind.Switch)
    ];
}
