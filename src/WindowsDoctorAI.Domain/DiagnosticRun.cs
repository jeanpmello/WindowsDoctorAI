using System.Text.Json.Serialization;

namespace WindowsDoctorAI.Domain;

/// <summary>Score limitado a 0–100, calculado somente a partir de verificações observadas.</summary>
public readonly record struct HealthScore
{
    public int Value { get; }

    [JsonConstructor]
    public HealthScore(int value)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Health Score deve estar entre 0 e 100.");
        }

        Value = value;
    }
}

/// <summary>Uma execução preserva o inventário do Milestone 1 e pode conter um relatório de scanners.</summary>
public sealed record DiagnosticRun(
    Guid Id,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    TimeSpan Duration,
    ComputerInventory Inventory,
    DiagnosticReport? Report = null)
{
    /// <summary>Epoch do processo que invalida planos durante diagnósticos/importações concorrentes; não é dado persistido.</summary>
    [JsonIgnore]
    public long EvidenceGeneration { get; init; }
}

/// <summary>Preferências locais. Histórico é opt-in; retenção zero conserva registros sem expurgo por idade.</summary>
public sealed record UserSettings
{
    public bool SaveDiagnosticHistory { get; init; }

    /// <summary>Prazo em dias; zero conserva o histórico sem expurgo automático.</summary>
    public int DiagnosticRetentionDays { get; init; }
}
