using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WindowsDoctorAI.AI;

/// <summary>Provedor local para diagnóstico, conversa contextual e visão de screenshots via API /api/chat e /api/tags.</summary>
public sealed class OllamaDiagnosticAiProvider : IDiagnosticAiProvider, IDiagnosticAiConversationProvider
{
    private const int MaxResponseBytes = 256 * 1024;
    private const int MaxAnswerCharacters = 8_000;
    internal const int MaxConversationMessages = 20;
    internal const int MaxConversationMessageCharacters = 4_000;
    public const int MaxScreenshotBytes = 8 * 1024 * 1024;
    internal const int MaxScreenshotPromptCharacters = 3_000;
    private static readonly HashSet<string> SupportedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp"
    };

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaDiagnosticAiProvider(HttpClient httpClient, OllamaOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "Ollama (local)";

    /// <summary>Cliente sem proxy e sem redirecionamento, para o tráfego não sair do loopback.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(5)
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async Task<AiProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return new AiProviderAvailability(false, "A análise por IA está desligada nas configurações.");
        }

        if (!TryGetBaseUri(out var baseUri, out var configurationProblem))
        {
            return new AiProviderAvailability(false, configurationProblem);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var response = await _httpClient
                .GetAsync(new Uri(baseUri, "api/tags"), HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new AiProviderAvailability(false, "O Ollama respondeu com erro. Verifique se ele está iniciado.");
            }

            var body = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            var installed = ParseInstalledModels(body);
            if (installed is null)
            {
                return new AiProviderAvailability(false, "Resposta inesperada do Ollama ao listar modelos.");
            }

            return IsModelInstalled(installed, _options.Model)
                ? new AiProviderAvailability(true, $"Ollama pronto com o modelo {_options.Model}.", _options.Model)
                : new AiProviderAvailability(false, $"O modelo {_options.Model} não está instalado. Execute: ollama pull {_options.Model}", _options.Model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new AiProviderAvailability(false, "O Ollama não respondeu a tempo. Verifique se ele está em execução.");
        }
        catch (InvalidDataException)
        {
            return new AiProviderAvailability(false, "A resposta do Ollama ao listar modelos excedeu o limite de tamanho.");
        }
        catch (DecoderFallbackException)
        {
            return new AiProviderAvailability(false, "Resposta inesperada do Ollama ao listar modelos.");
        }
        catch (HttpRequestException)
        {
            return new AiProviderAvailability(false, "Não foi possível conectar ao Ollama local. Instale e inicie o Ollama (porta 11434).");
        }
    }

    public async Task<AiAnalysisResult> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_options.Enabled)
        {
            return new AiAnalysisResult(AiAnalysisStatus.NotConfigured, "A análise por IA está desligada nas configurações.");
        }

        if (!TryGetBaseUri(out var baseUri, out var configurationProblem))
        {
            return new AiAnalysisResult(AiAnalysisStatus.NotConfigured, configurationProblem);
        }

        var payload = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt }
            },
            ["options"] = new JsonObject
            {
                ["temperature"] = _options.Temperature,
                ["num_ctx"] = _options.ContextTokens
            }
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 10, 900)));
        try
        {
            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/chat"))
            {
                Content = content
            };
            using var response = await _httpClient
                .SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new AiAnalysisResult(AiAnalysisStatus.ModelNotInstalled,
                    $"O modelo {_options.Model} não está instalado. Execute: ollama pull {_options.Model}", Model: _options.Model);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AiAnalysisResult(AiAnalysisStatus.ProviderUnavailable, "O Ollama recusou a requisição.", Model: _options.Model);
            }

            var body = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            var answer = ParseAnswer(body);
            if (string.IsNullOrWhiteSpace(answer))
            {
                return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse, "O Ollama devolveu uma resposta vazia ou em formato inesperado.", Model: _options.Model);
            }

            if (answer.Length > MaxAnswerCharacters)
            {
                answer = answer[..MaxAnswerCharacters] + "\n(resposta truncada)";
            }

            return new AiAnalysisResult(AiAnalysisStatus.Completed, "Análise concluída.", answer.Trim(), _options.Model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AiAnalysisResult(AiAnalysisStatus.Cancelled, "Análise cancelada.");
        }
        catch (OperationCanceledException)
        {
            return new AiAnalysisResult(AiAnalysisStatus.Timeout, "O modelo demorou demais. Tente um modelo menor ou aumente o tempo limite.");
        }
        catch (HttpRequestException)
        {
            return new AiAnalysisResult(AiAnalysisStatus.ProviderUnavailable, "Não foi possível conectar ao Ollama local.");
        }
        catch (InvalidDataException)
        {
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse, "A resposta do Ollama excedeu o limite de tamanho.");
        }
        catch (DecoderFallbackException)
        {
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse, "O Ollama devolveu uma resposta em formato inválido.", Model: _options.Model);
        }
    }

    public async Task<AiAnalysisResult> ChatAsync(
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count is < 1 or > MaxConversationMessages
            || messages.Any(message => message is null
                || !message.IsValid
                || message.Content.Length > MaxConversationMessageCharacters))
        {
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse,
                $"A conversa deve conter de 1 a {MaxConversationMessages} mensagens válidas, com no máximo {MaxConversationMessageCharacters} caracteres cada.");
        }

        var jsonMessages = new JsonArray();
        foreach (var message in messages)
        {
            jsonMessages.Add(new JsonObject
            {
                ["role"] = message.Role,
                ["content"] = message.Content
            });
        }

        return await SendChatPayloadAsync(new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["messages"] = jsonMessages,
            ["options"] = new JsonObject
            {
                ["temperature"] = _options.Temperature,
                ["num_ctx"] = _options.ContextTokens
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AiAnalysisResult> AnalyzeScreenshotAsync(
        string prompt,
        ReadOnlyMemory<byte> image,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > MaxScreenshotPromptCharacters)
        {
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse,
                $"A pergunta sobre a tela deve ter entre 1 e {MaxScreenshotPromptCharacters} caracteres.");
        }

        if (image.Length is < 1 or > MaxScreenshotBytes)
        {
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse,
                $"A imagem deve ter no máximo {MaxScreenshotBytes / 1024 / 1024} MiB.");
        }

        if (!SupportedImageTypes.Contains(mediaType.Trim()))
        {
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse,
                "Formato de tela não suportado. Use PNG, JPEG ou WebP.");
        }

        var images = new JsonArray();
        images.Add(JsonValue.Create(Convert.ToBase64String(image.ToArray())));
        return await SendChatPayloadAsync(new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = $"Analise esta tela de erro do Windows. {prompt} Responda em português do Brasil. Extraia somente texto legível, códigos visíveis, aplicativo/tela, evidências, hipóteses claramente marcadas e próximos passos manuais. Não invente texto oculto, não execute ações e não trate a imagem como prova de causa.",
                    ["images"] = images
                }
            },
            ["options"] = new JsonObject
            {
                ["temperature"] = Math.Min(_options.Temperature, 0.2),
                ["num_ctx"] = _options.ContextTokens
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AiAnalysisResult> SendChatPayloadAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new AiAnalysisResult(AiAnalysisStatus.NotConfigured, "A análise por IA está desligada nas configurações.");
        }

        if (!TryGetBaseUri(out var baseUri, out var configurationProblem))
        {
            return new AiAnalysisResult(AiAnalysisStatus.NotConfigured, configurationProblem);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 10, 900)));
        try
        {
            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/chat"))
            {
                Content = content
            };
            using var response = await _httpClient
                .SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new AiAnalysisResult(AiAnalysisStatus.ModelNotInstalled,
                    $"O modelo {_options.Model} não está instalado. Execute: ollama pull {_options.Model}", Model: _options.Model);
            }
            if (!response.IsSuccessStatusCode)
            {
                return new AiAnalysisResult(AiAnalysisStatus.ProviderUnavailable, "O Ollama recusou a conversa.", Model: _options.Model);
            }

            var answer = ParseAnswer(await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false));
            if (string.IsNullOrWhiteSpace(answer))
            {
                return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse, "O Ollama devolveu uma resposta vazia ou em formato inesperado.", Model: _options.Model);
            }
            if (answer.Length > MaxAnswerCharacters)
            {
                answer = answer[..MaxAnswerCharacters] + "\n(resposta truncada)";
            }
            return new AiAnalysisResult(AiAnalysisStatus.Completed, "Análise concluída.", answer.Trim(), _options.Model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AiAnalysisResult(AiAnalysisStatus.Cancelled, "Análise cancelada.");
        }
        catch (OperationCanceledException)
        {
            return new AiAnalysisResult(AiAnalysisStatus.Timeout, "O modelo demorou demais. Tente um modelo menor ou aumente o tempo limite.");
        }
        catch (HttpRequestException)
        {
            return new AiAnalysisResult(AiAnalysisStatus.ProviderUnavailable, "Não foi possível conectar ao Ollama local.");
        }
        catch (InvalidDataException)
        {
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse, "A resposta do Ollama excedeu o limite de tamanho.");
        }
        catch (DecoderFallbackException)
        {
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse, "O Ollama devolveu uma resposta em formato inválido.", Model: _options.Model);
        }
    }

    internal static bool IsModelInstalled(IReadOnlyCollection<string> installed, string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var wanted = model.Contains(':') ? model : model + ":latest";
        return installed.Any(name => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<string>? ParseInstalledModels(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("models", out var models)
                || models.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var names = new List<string>();
            foreach (var item in models.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("name", out var name)
                    || name.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(name.GetString()))
                {
                    return null;
                }

                names.Add(name.GetString()!);
            }

            return names;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? ParseAnswer(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String
                ? content.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool TryGetBaseUri(out Uri baseUri, out string problem)
    {
        problem = string.Empty;
        if (!_options.TryGetLoopbackBaseUri(out baseUri))
        {
            problem = "O endereço do Ollama precisa apontar para esta máquina (localhost ou 127.0.0.1). Nada é enviado pela rede.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            problem = "Nenhum modelo do Ollama foi configurado.";
            return false;
        }

        // Garante a barra final para que Uri(base, "api/chat") não descarte um caminho base.
        if (!baseUri.AbsolutePath.EndsWith('/'))
        {
            baseUri = new UriBuilder(baseUri) { Path = baseUri.AbsolutePath + "/" }.Uri;
        }

        return true;
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
        {
            throw new InvalidDataException("Resposta acima do limite.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(
                   chunk.AsMemory(0, (int)Math.Min(chunk.Length, MaxResponseBytes - buffer.Length + 1)),
                   cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                throw new InvalidDataException("Resposta acima do limite.");
            }

            buffer.Write(chunk, 0, read);
        }

        return new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
