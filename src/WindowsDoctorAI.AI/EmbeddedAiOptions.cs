namespace WindowsDoctorAI.AI;

/// <summary>Configuração do runtime ONNX GenAI distribuído junto com o aplicativo.</summary>
public sealed class EmbeddedAiOptions
{
    public const string SectionName = "Ai:Embedded";

    /// <summary>Permanece desligado até o pacote conter um modelo compatível e validado.</summary>
    public bool Enabled { get; set; }

    /// <summary>Diretório absoluto ou relativo à pasta do executável.</summary>
    public string ModelDirectory { get; set; } = "Models/ai";

    public string ModelName { get; set; } = "embedded-onnx";

    public int MinimumRamGb { get; set; } = 16;

    public int MaxNewTokens { get; set; } = 768;

    public int TimeoutSeconds { get; set; } = 180;

    public bool TryGetModelDirectory(out string directory)
    {
        directory = string.Empty;
        if (string.IsNullOrWhiteSpace(ModelDirectory))
            return false;

        directory = Path.IsPathRooted(ModelDirectory)
            ? ModelDirectory
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ModelDirectory));
        return true;
    }
}
