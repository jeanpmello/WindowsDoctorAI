using WindowsDoctorAI.Networking;

namespace WindowsDoctorAI.Tests.Networking;

public sealed class NetworkProfileAndRedactionTests
{
    [Fact]
    public void Default_profiles_cover_initial_vendor_families_without_write_commands()
    {
        var profiles = ReadOnlyProfileCatalog.CreateDefaults();

        Assert.Contains(profiles, profile => profile.Vendor == "MikroTik");
        Assert.Contains(profiles, profile => profile.Vendor == "Fortinet");
        Assert.Contains(profiles, profile => profile.Vendor == "Cisco");
        Assert.Contains(profiles, profile => profile.Vendor == "Ubiquiti");
        Assert.Contains(profiles, profile => profile.Vendor == "Aruba");
        Assert.DoesNotContain(profiles.SelectMany(profile => profile.Commands), command =>
            command.Contains("configure", StringComparison.OrdinalIgnoreCase)
            || command.Contains("reload", StringComparison.OrdinalIgnoreCase)
            || command.Contains("delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Redactor_removes_key_value_secrets_tokens_and_private_keys()
    {
        var redactor = new NetworkEvidenceRedactor();
        var content = "password: super-secret\nAuthorization: Bearer abc123\n-----BEGIN RSA PRIVATE KEY-----\nkey\n-----END RSA PRIVATE KEY-----";

        var result = redactor.Redact(content);

        Assert.DoesNotContain("super-secret", result);
        Assert.DoesNotContain("abc123", result);
        Assert.DoesNotContain("BEGIN RSA PRIVATE KEY", result);
        Assert.Contains("[REDACTED]", result);
        Assert.Contains("[REDACTED PRIVATE KEY]", result);
    }

    [Fact]
    public void Redactor_preserves_safe_diagnostic_values()
    {
        var redactor = new NetworkEvidenceRedactor();

        var result = redactor.Redact("interface=ether1\nstatus=up\naddress=192.0.2.10");

        Assert.Equal("interface=ether1\nstatus=up\naddress=192.0.2.10", result);
    }
}
