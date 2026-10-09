using System.Diagnostics;
using System.Text;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace WindowsDoctorAI.AI;

/// <summary>
/// Provider de geração textual usando ONNX Runtime GenAI distribuído com o aplicativo.
/// O modelo é carregado somente quando a primeira análise é solicitada; nenhuma ação de sistema é executada.
/// </summary>
public sealed class EmbeddedOnnxGenAiProvider : IDiagnosticAiProvider, IDiagnosticAiConversationProvider
{
    private const int MaxAnswerCharacters = 8_000;
    private readonly EmbeddedAiOptions _options;
    private readonly SemaphoreSlim _generationGate = new(1, 1);
    private readonly object _modelLock = new();
    private Model? _model;
    private Tokenizer? _tokenizer;

    public EmbeddedOnnxGenAiProvider(EmbeddedAiOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "ONNX Runtime GenAI (embutido)";

    public Task<AiProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.Enabled)
            return Task.FromResult(new AiProviderAvailability(false, "O runtime de IA embutido está desligado nesta configuração."));

        if (!_options.TryGetModelDirectory(out var directory) || !Directory.Exists(directory))
        {
            return Task.FromResult(new AiProviderAvailability(false,
                "O pacote não contém o diretório do modelo ONNX embutido. Esta distribuição não pode usar IA local ainda.",
                _options.ModelName));
        }

        bool hasOnnx;
        try
        {
            hasOnnx = Directory.EnumerateFiles(directory, "*.onnx", SearchOption.TopDirectoryOnly).Any();
        }
        catch (IOException)
        {
            return Task.FromResult(new AiProviderAvailability(false,
                "O diretório do modelo embutido não pôde ser lido.", _options.ModelName));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(new AiProviderAvailability(false,
                "O diretório do modelo embutido não pode ser acessado pela aplicação.", _options.ModelName));
        }
        if (!hasOnnx)
        {
            return Task.FromResult(new AiProviderAvailability(false,
                "O diretório do modelo embutido não contém um arquivo ONNX válido.", _options.ModelName));
        }

        return Task.FromResult(new AiProviderAvailability(true,
            $"Runtime ONNX GenAI pronto para {_options.ModelName}.", _options.ModelName));
    }

    public async Task<AiAnalysisResult> AnalyzeAsync(
        AiAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_options.Enabled)
            return new AiAnalysisResult(AiAnalysisStatus.NotConfigured, "O runtime de IA embutido está desligado.", Model: _options.ModelName);

        if (!_options.TryGetModelDirectory(out var directory) || !Directory.Exists(directory))
        {
            return new AiAnalysisResult(AiAnalysisStatus.ModelNotInstalled,
                "O modelo ONNX não está incluído no pacote desta instalação.", Model: _options.ModelName);
        }

        await _generationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var timeoutSeconds = Math.Clamp(_options.TimeoutSeconds, 10, 900);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var model = GetModel(directory);
            var prompt = $"<|system|>{request.SystemPrompt}<|end|><|user|>{request.UserPrompt}<|end|><|assistant|>";
            var stopwatch = Stopwatch.StartNew();
            var answer = await Task.Run(() => Generate(model, prompt, timeout.Token), timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(answer))
            {
                return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse,
                    "O runtime embutido não produziu uma resposta.", Model: _options.ModelName);
            }

            if (answer.Length > MaxAnswerCharacters)
                answer = answer[..MaxAnswerCharacters] + "\n(resposta truncada)";

            return new AiAnalysisResult(
                AiAnalysisStatus.Completed,
                $"Análise local concluída em {stopwatch.Elapsed.TotalSeconds:F1}s.",
                answer.Trim(),
                _options.ModelName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AiAnalysisResult(AiAnalysisStatus.Cancelled, "Análise cancelada.", Model: _options.ModelName);
        }
        catch (OperationCanceledException)
        {
            return new AiAnalysisResult(AiAnalysisStatus.Timeout,
                "O modelo embutido demorou demais. Tente o perfil compacto ou aumente o limite.", Model: _options.ModelName);
        }
        catch (Exception)
        {
            return new AiAnalysisResult(AiAnalysisStatus.ProviderUnavailable,
                "O runtime ONNX embutido não conseguiu carregar ou executar o modelo.", Model: _options.ModelName);
        }
        finally
        {
            _generationGate.Release();
        }
    }

    public async Task<AiAnalysisResult> ChatAsync(
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count is < 1 or > 20 || messages.Any(message => message is null || !message.IsValid || message.Content.Length > 4_000))
        {
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse,
                "A conversa embutida excedeu o limite de mensagens ou caracteres.", Model: _options.ModelName);
        }

        var system = string.Join("\n", messages.Where(message => message.Role == "system").Select(message => message.Content));
        var user = string.Join("\n", messages.Where(message => message.Role != "system")
            .Select(message => $"[{message.Role}] {message.Content}"));
        return await AnalyzeAsync(new AiAnalysisRequest(system, user), cancellationToken).ConfigureAwait(false);
    }

    public Task<AiAnalysisResult> AnalyzeScreenshotAsync(
        string prompt,
        ReadOnlyMemory<byte> image,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AiAnalysisResult(AiAnalysisStatus.ProviderUnavailable,
            "O modelo ONNX embutido desta versão é textual e ainda não possui o componente multimodal para screenshots.",
            Model: _options.ModelName));
    }

    private Model GetModel(string directory)
    {
        if (_model is not null && _tokenizer is not null)
            return _model;

        lock (_modelLock)
        {
            if (_model is null)
            {
                _model = new Model(directory);
                _tokenizer = new Tokenizer(_model);
            }

            return _model;
        }
    }

    private string Generate(Model model, string prompt, CancellationToken cancellationToken)
    {
        var tokenizer = _tokenizer ?? throw new InvalidOperationException("Tokenizer não inicializado.");
        var sequences = tokenizer.Encode(prompt);
        using var generatorParams = new GeneratorParams(model);
        generatorParams.SetSearchOption("max_length", Math.Clamp(_options.MaxNewTokens, 64, 4096));
        generatorParams.SetInputSequences(sequences);
        using var generator = new Generator(model, generatorParams);
        using var tokenizerStream = tokenizer.CreateStream();
        var answer = new StringBuilder();

        while (!generator.IsDone())
        {
            cancellationToken.ThrowIfCancellationRequested();
            generator.GenerateNextToken();
            var token = generator.GetSequence(0)[^1];
            var part = tokenizerStream.Decode(token);
            if (string.IsNullOrEmpty(part))
                continue;

            answer.Append(part);
            if (answer.ToString().Contains("<|end|>", StringComparison.Ordinal)
                || answer.ToString().Contains("<|user|>", StringComparison.Ordinal)
                || answer.ToString().Contains("<|system|>", StringComparison.Ordinal))
            {
                break;
            }
        }

        return answer.ToString();
    }
}
