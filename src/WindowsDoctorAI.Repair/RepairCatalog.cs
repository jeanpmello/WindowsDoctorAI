using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Repair;

/// <summary>Catálogo local de propostas registradas em código; ausência de proposta não implica reparo disponível.</summary>
public interface IRepairCatalog
{
    IReadOnlyList<RepairProposal> GetAvailableProposals();
}

/// <summary>A composição de produção não registra propostas nem plugins executores.</summary>
public sealed class EmptyRepairCatalog : IRepairCatalog
{
    public IReadOnlyList<RepairProposal> GetAvailableProposals() => Array.Empty<RepairProposal>();
}
