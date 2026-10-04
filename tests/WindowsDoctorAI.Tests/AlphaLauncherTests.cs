namespace WindowsDoctorAI.Tests;

public sealed class AlphaLauncherTests
{
    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Alpha", name));

    [Fact]
    public void LauncherChecksX64VisualCRuntimeBeforeStartingTheApplication()
    {
        var launcher = ReadFixture("Start-WindowsDoctorAI.cmd");
        var runtimeCheck = launcher.IndexOf("Runtimes\\x64", StringComparison.OrdinalIgnoreCase);
        var startCommand = launcher.IndexOf("start \"\" /wait \"%APP%\"", StringComparison.OrdinalIgnoreCase);

        Assert.True(runtimeCheck >= 0, "O launcher deve verificar a chave do runtime VC++ x64.");
        Assert.Contains("/v Installed", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/reg:64", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x1$", launcher, StringComparison.Ordinal);
        Assert.True(startCommand > runtimeCheck, "O app só pode iniciar depois da pré-checagem do VC++ x64.");
        Assert.Contains("learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist", launcher, StringComparison.Ordinal);
        Assert.Contains("APP_EXIT_CODE", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void LauncherDoesNotDownloadInstallOrElevate()
    {
        var launcher = ReadFixture("Start-WindowsDoctorAI.cmd");
        var forbiddenCommands = new[]
        {
            "winget install",
            "msiexec",
            "Invoke-WebRequest",
            "Start-Process -Verb RunAs",
            "runas ",
            "bitsadmin",
            "certutil",
            "reg add",
            "reg delete",
            "ExecutionPolicy Bypass"
        };

        foreach (var command in forbiddenCommands)
            Assert.DoesNotContain(command, launcher, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlphaPackageWorkflowStagesAndRequiresTheLauncher()
    {
        var workflow = ReadFixture("alpha-package.yml");

        Assert.Contains("packaging\\Start-WindowsDoctorAI.cmd", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'Start-WindowsDoctorAI.cmd'", workflow, StringComparison.Ordinal);
        Assert.Contains("README-ALPHA.md", workflow, StringComparison.Ordinal);
    }
}
