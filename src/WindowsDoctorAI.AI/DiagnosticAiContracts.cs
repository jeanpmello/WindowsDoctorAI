namespace WindowsDoctorAI.AI;

/// <summary>Texto exato que será entregue ao modelo. Existe como tipo próprio para a UI poder exibi-lo antes do envio.</summary>
public sealed record AiAnalysisRequest(string SystemPrompt, string UserPrompt)
{
    /// <summary>Tamanho total em caracteres, útil para a prévia de consentimento.</summary>
    public int TotalCharacters => SystemPrompt.Length + UserPrompt.Length;
}

public enum AiAnalysisStatus
{
    Completed,
    NotConfigured,
    ProviderUnavailable,
    ModelNotInstalled,
    Timeout,
    InvalidResponse,
    Cancelled
}

/// <summary>Resultado da análise. <see cref="Text"/> só existe quando o status é <see cref="AiAnalysisStatus.Completed"/>.</summary>
public sealed record AiAnalysisResult(AiAnalysisStatus Status, string Message, string? Text = null, string? Model = null)
{
    public bool IsSuccess => Status == AiAnalysisStatus.Completed && !string.IsNullOrWhiteSpace(Text);
}

/// <summary>Estado do provedor, sem enviar nenhum dado do diagnóstico.</summary>
public sealed record AiProviderAvailability(bool IsReady, string Message, string? Model = null);

/// <summary>Provedor de explicação textual. Nunca executa ações; só devolve texto para leitura.</summary>
public interface IDiagnosticAiProvider
{
    string Name { get; }

    Task<AiProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<AiAnalysisResult> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Mensagem de conversa enviada ao modelo local; conteúdo nunca é executado pelo aplicativo.</summary>
public sealed record AiChatMessage(string Role, string Content)
{
    public bool IsValid => Role is "user" or "assistant" or "system" && !string.IsNullOrWhiteSpace(Content);
}

/// <summary>Porta opcional para conversa contextual e interpretação de imagens no provedor local.</summary>
public interface IDiagnosticAiConversationProvider
{
    Task<AiAnalysisResult> ChatAsync(
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken = default);

    Task<AiAnalysisResult> AnalyzeScreenshotAsync(
        string prompt,
        ReadOnlyMemory<byte> image,
        string mediaType,
        CancellationToken cancellationToken = default);
}
