using WindowsDoctorAI.Core;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.Repair;

/// <summary>
/// Allowlist de produção intencionalmente vazia. Entradas devem ser definições imutáveis compiladas;
/// dados importados nunca podem registrar uma operação ou transformar uma regra ManualOnly em proposta.
/// </summary>
public sealed class CodeRepairProposalAllowlist : IRepairProposalAllowlist
{
    public bool TryGetDefinition(string ruleId, int ruleVersion, out RepairProposalDefinition? definition)
    {
        definition = null;
        return false;
    }
}
