namespace WindowsDoctorAI.Core;

/// <summary>
/// Gate em processo: invalida snapshots quando um diagnóstico/importação começa e serializa a revalidação final
/// com a gravação durável de Started. Não executa nem altera recursos do sistema operacional.
/// </summary>
public sealed class RepairEvidenceGate : IRepairEvidenceGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private long _generation;
    private string? _currentDiagnosticRunId;

    public long CurrentGeneration => Interlocked.Read(ref _generation);
    public Guid? CurrentDiagnosticRunId => Guid.TryParse(Volatile.Read(ref _currentDiagnosticRunId), out var id) ? id : null;

    public async ValueTask<IRepairEvidenceLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(this);
    }

    private long AdvanceGeneration()
    {
        var next = Interlocked.Increment(ref _generation);
        if (next <= 0)
            throw new InvalidOperationException("A geração de evidência excedeu o limite suportado.");
        Volatile.Write(ref _currentDiagnosticRunId, null);
        return next;
    }

    private void MarkDiagnosticRunCurrent(Guid diagnosticRunId, long expectedGeneration)
    {
        if (diagnosticRunId == Guid.Empty) throw new ArgumentException("O DiagnosticRunId deve ser válido.", nameof(diagnosticRunId));
        if (CurrentGeneration != expectedGeneration)
            throw new InvalidOperationException("O run não corresponde à geração de evidência atual.");
        Volatile.Write(ref _currentDiagnosticRunId, diagnosticRunId.ToString("D"));
    }

    private sealed class Lease(RepairEvidenceGate owner) : IRepairEvidenceLease
    {
        private int _disposed;

        public long CurrentGeneration => owner.CurrentGeneration;

        public long AdvanceGeneration()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return owner.AdvanceGeneration();
        }

        public void MarkDiagnosticRunCurrent(Guid diagnosticRunId, long expectedGeneration)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            owner.MarkDiagnosticRunCurrent(diagnosticRunId, expectedGeneration);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner._semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
