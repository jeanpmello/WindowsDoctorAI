namespace WindowsDoctorAI.AI;

/// <summary>Configuração do Ollama local. O endereço precisa ser loopback: este provedor nunca fala com a rede.</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ai:Ollama";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    public string Model { get; set; } = "qwen3-vl:8b";

    /// <summary>Modelos locais podem demorar na primeira resposta (carregamento em memória).</summary>
    public int TimeoutSeconds { get; set; } = 180;

    public double Temperature { get; set; } = 0.2;

    public int ContextTokens { get; set; } = 8192;

    /// <summary>Retorna o endereço base somente se for HTTP(S) e apontar para a própria máquina.</summary>
    public bool TryGetLoopbackBaseUri(out Uri baseUri)
    {
        baseUri = null!;
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(parsed.UserInfo))
        {
            return false;
        }

        var isLoopback = parsed.IsLoopback
            || string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        if (!isLoopback)
        {
            return false;
        }

        baseUri = parsed;
        return true;
    }
}
