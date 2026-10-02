using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Repair;

/// <summary>Catálogo local de propostas registradas em código; ausência de proposta não implica reparo disponível.</summary>
public interface IRepairCatalog
{
    IReadOnlyList<RepairProposal> GetAvailableProposals();
}

/// <summary>A composição atual não registra reparos de sistema, apenas o framework e a demonstração inerte.</summary>
public sealed class EmptyRepairCatalog : IRepairCatalog
{
    public IReadOnlyList<RepairProposal> GetAvailableProposals() => Array.Empty<RepairProposal>();
}
