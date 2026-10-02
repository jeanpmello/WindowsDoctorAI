using System.Text.Json.Serialization;

namespace WindowsDoctorAI.Domain;

/// <summary>Score limitado a 0–100. O valor inicial do milestone é apenas demonstrativo.</summary>
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

    public static HealthScore InitialMilestoneScore => new(95);
}

/// <summary>Resultado imutável de uma execução local do scanner.</summary>
public sealed record DiagnosticRun(
    Guid Id,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    TimeSpan Duration,
    ComputerInventory Inventory,
    HealthScore HealthScore);

/// <summary>Preferências locais. Histórico é habilitado por padrão e pode ser desligado pelo usuário.</summary>
public sealed record UserSettings
{
    public bool SaveDiagnosticHistory { get; init; } = true;
}
