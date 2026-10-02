namespace WindowsDoctorAI.Diagnostics;

/// <summary>Representa a ausência de Windows sem afirmar que um módulo real foi consultado.</summary>
public sealed class UnsupportedWindowsBackupDataSource : IWindowsBackupDataSource
{
    public Task<WindowsBackupProbe> ReadBackupSetsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new WindowsBackupProbe(WindowsBackupProbeStatus.QueryFailed));
    }
}
