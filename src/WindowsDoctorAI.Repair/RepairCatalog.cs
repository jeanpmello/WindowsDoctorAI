namespace WindowsDoctorAI.Repair;

/// <summary>Metadados de uma possível ação futura. Proposta não significa execução de reparo.</summary>
public sealed record RepairProposal(string Id, string Title, string Description, bool RequiresExplicitApproval);

/// <summary>O primeiro milestone é somente de leitura; nenhum executor de reparo é fornecido.</summary>
public interface IRepairCatalog
{
    IReadOnlyList<RepairProposal> GetAvailableProposals();
}

public sealed class EmptyRepairCatalog : IRepairCatalog
{
    public IReadOnlyList<RepairProposal> GetAvailableProposals() => Array.Empty<RepairProposal>();
}
