using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.AI;

public sealed record InventoryAnalysis(string Summary, IReadOnlyList<string> Recommendations);

/// <summary>Abstração do assistente. Milestone 1 não registra nem ativa qualquer provedor remoto.</summary>
public interface IInventoryAnalysisProvider
{
    Task<InventoryAnalysis> AnalyzeAsync(ComputerInventory inventory, CancellationToken cancellationToken = default);
}
