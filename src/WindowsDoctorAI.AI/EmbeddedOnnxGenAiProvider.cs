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

        var availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var minimumMemory = Math.Max(_options.MinimumRamGb, 1) * 1024L * 1024L * 1024L;
        if (availableMemory > 0 && availableMemory < minimumMemory)
        {
            return Task.FromResult(new AiProviderAvailability(false,
                $"A memória disponível pode ser insuficiente para o modelo {_options.ModelName}; mínimo recomendado: {_options.MinimumRamGb} GB.",
                _options.ModelName));
        }

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
        return AnalyzeScreenshotCoreAsync(prompt, image, mediaType, cancellationToken);
    }

    private async Task<AiAnalysisResult> AnalyzeScreenshotCoreAsync(
        string prompt,
        ReadOnlyMemory<byte> image,
        string mediaType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4_000)
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse,
                "O texto para análise da screenshot é inválido ou excede o limite.", Model: _options.ModelName);

        if (image.Length is 0 or > 8 * 1024 * 1024)
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse,
                "A screenshot está vazia ou excede o limite de 8 MiB.", Model: _options.ModelName);

        var extension = mediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => string.Empty
        };
        if (extension.Length == 0)
            return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse,
                "O formato da screenshot não é suportado.", Model: _options.ModelName);

        if (!_options.Enabled)
            return new AiAnalysisResult(AiAnalysisStatus.NotConfigured, "O runtime de IA embutido está desligado.", Model: _options.ModelName);

        if (!_options.TryGetModelDirectory(out var directory) || !Directory.Exists(directory))
            return new AiAnalysisResult(AiAnalysisStatus.ModelNotInstalled,
                "O modelo ONNX multimodal não está incluído no pacote desta instalação.", Model: _options.ModelName);

        await _generationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporaryImage = Path.Combine(Path.GetTempPath(), $"WindowsDoctorAI-{Guid.NewGuid():N}{extension}");
        try
        {
            await File.WriteAllBytesAsync(temporaryImage, image.ToArray(), cancellationToken).ConfigureAwait(false);
            var timeoutSeconds = Math.Clamp(_options.TimeoutSeconds, 10, 900);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var model = GetModel(directory);
            var fullPrompt = $"<|system|>Você é um assistente técnico. Extraia apenas evidências visíveis, códigos legíveis e hipóteses claramente marcadas; não execute ações.<|end|><|user|><|image_1|>{prompt}<|end|><|assistant|>";
            var answer = await Task.Run(() => GenerateFromImage(model, fullPrompt, temporaryImage, timeout.Token), timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(answer))
                return new AiAnalysisResult(AiAnalysisStatus.InvalidResponse, "O modelo não produziu uma leitura da screenshot.", Model: _options.ModelName);

            if (answer.Length > MaxAnswerCharacters)
                answer = answer[..MaxAnswerCharacters] + "\n(resposta truncada)";
            return new AiAnalysisResult(AiAnalysisStatus.Completed, "Screenshot analisada localmente.", answer.Trim(), _options.ModelName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AiAnalysisResult(AiAnalysisStatus.Cancelled, "Análise da screenshot cancelada.", Model: _options.ModelName);
        }
        catch (OperationCanceledException)
        {
            return new AiAnalysisResult(AiAnalysisStatus.Timeout, "A análise da screenshot demorou demais.", Model: _options.ModelName);
        }
        catch (Exception)
        {
            return new AiAnalysisResult(AiAnalysisStatus.ProviderUnavailable,
                "O runtime ONNX multimodal não conseguiu processar a screenshot.", Model: _options.ModelName);
        }
        finally
        {
            _generationGate.Release();
            try { File.Delete(temporaryImage); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
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
            generator.ComputeLogits();
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

    private string GenerateFromImage(Model model, string prompt, string imagePath, CancellationToken cancellationToken)
    {
        using var image = Images.Load(imagePath);
        using var processor = new MultiModalProcessor(model);
        using var tokenizerStream = processor.CreateStream();
        var inputs = processor.ProcessImages(prompt, image);
        using var generatorParams = new GeneratorParams(model);
        generatorParams.SetSearchOption("max_length", Math.Clamp(_options.MaxNewTokens, 128, 4096));
        generatorParams.SetInputs(inputs);
        using var generator = new Generator(model, generatorParams);
        var answer = new StringBuilder();

        while (!generator.IsDone())
        {
            cancellationToken.ThrowIfCancellationRequested();
            generator.ComputeLogits();
            generator.GenerateNextToken();
            var part = tokenizerStream.Decode(generator.GetSequence(0)[^1]);
            if (string.IsNullOrEmpty(part))
                continue;
            answer.Append(part);
            if (answer.ToString().Contains("<|end|>", StringComparison.Ordinal))
                break;
        }

        return answer.ToString();
    }
}
