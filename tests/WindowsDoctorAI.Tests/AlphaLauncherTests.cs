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
    public void MissingVcBranchShowsOfficialLinkPausesAndReturnsExitCodeTwo()
    {
        var launcher = ReadFixture("Start-WindowsDoctorAI.cmd");
        var lines = launcher.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var missingVcLabel = Array.FindIndex(lines, line =>
            string.Equals(line.Trim(), ":missing_vc", StringComparison.OrdinalIgnoreCase));
        Assert.True(missingVcLabel >= 0, "O launcher deve ter a ramificacao :missing_vc.");

        var nextLabel = Array.FindIndex(lines, missingVcLabel + 1, line => line.TrimStart().StartsWith(':'));
        Assert.True(nextLabel > missingVcLabel, "A ramificacao :missing_vc deve terminar no proximo label.");

        var branchLines = lines[(missingVcLabel + 1)..nextLabel];
        var branch = string.Join("\n", branchLines);
        var pauseIndex = Array.FindIndex(branchLines, line =>
            string.Equals(line.Trim(), "pause", StringComparison.OrdinalIgnoreCase));
        var returnIndex = Array.FindIndex(branchLines, line =>
            string.Equals(line.Trim(), "exit /b 2", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist", branch, StringComparison.Ordinal);
        Assert.True(pauseIndex >= 0, "A ramificacao :missing_vc deve pausar antes de retornar.");
        Assert.True(returnIndex > pauseIndex, "A pausa deve ocorrer antes do retorno com exit code 2.");
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
