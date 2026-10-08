using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsDoctorAI.AI;
using WindowsDoctorAI.Domain;

namespace WindowsDoctorAI.App;

/// <summary>Análise textual por IA local sobre a execução atual. Somente leitura: nada é executado a partir da resposta.</summary>
internal partial class HomeViewModel
{
    private const string AiIdleStatus = "Execute ou carregue um diagnóstico para habilitar a análise por IA local.";

    private sealed record AiPromptSnapshot(DiagnosticRun Run, AiAnalysisRequest Request);

    private CancellationTokenSource? _aiCancellation;
    private AiPromptSnapshot? _aiPromptSnapshot;
    private int _aiOperationId;

    [ObservableProperty] private string _aiStatusText = AiIdleStatus;
    [ObservableProperty] private string _aiAnswerText = string.Empty;
    [ObservableProperty] private string _aiPromptPreview = string.Empty;
    [ObservableProperty] private bool _isAnalyzingWithAi;
    [ObservableProperty] private bool _isTestingAi;
    [ObservableProperty] private bool _isAiOperationRunning;
    [ObservableProperty] private bool _canAnalyzeWithAi;
    [ObservableProperty] private bool _canTestAi;

    /// <summary>Chamado sempre que a execução atual muda; calcula e guarda a requisição exata exibida na prévia.</summary>
    internal void RefreshAiState()
    {
        CancelAiForContextChange();
        ClearAiAnswer();
        _aiPromptSnapshot = null;
        if (_aiProvider is null)
        {
            AiPromptPreview = string.Empty;
            AiStatusText = "Nenhum provedor de IA está registrado nesta build.";
            SetCanAnalyze(false);
            SetCanTestAi();
            return;
        }

        var run = _currentRun;
        if (run is null)
        {
            AiPromptPreview = string.Empty;
            AiStatusText = AiIdleStatus;
            SetCanAnalyze(false);
            SetCanTestAi();
            return;
        }

        var guidanceSnapshot = ManualGuidanceFindings.ToArray();
        var request = DiagnosticPromptBuilder.Build(run, guidanceSnapshot);
        _aiPromptSnapshot = new AiPromptSnapshot(run, request);
        AiPromptPreview = $"[Instruções ao modelo]\n{request.SystemPrompt}\n\n[Dados do diagnóstico e orientações ManualOnly (identificadores conhecidos redigidos; fontes não autenticadas)]\n{request.UserPrompt}";
        AiStatusText = $"Pronto. {request.TotalCharacters:N0} caracteres serão enviados pelo app ao endpoint Ollama configurado em loopback neste computador.";
        SetCanAnalyze(true);
        SetCanTestAi();
    }

    private void ClearAiAnswer() => AiAnswerText = string.Empty;

    private void SetCanAnalyze(bool value)
    {
        CanAnalyzeWithAi = value
            && _aiProvider is not null
            && _currentRun is not null
            && _aiPromptSnapshot is not null
            && ReferenceEquals(_aiPromptSnapshot.Run, _currentRun)
            && !IsAnalyzingWithAi
            && !IsTestingAi
            && !IsAiOperationRunning
            && !IsScanning
            && !IsLoadingHistory;
        AnalyzeWithAiCommand.NotifyCanExecuteChanged();
    }

    private void SetCanTestAi()
    {
        CanTestAi = _aiProvider is not null
            && !IsTestingAi
            && !IsAnalyzingWithAi
            && !IsAiOperationRunning
            && !IsScanning
            && !IsLoadingHistory;
        TestAiCommand.NotifyCanExecuteChanged();
    }

    internal void CancelAiForContextChange(string? status = null)
    {
        _aiOperationId++;
        var cancellation = _aiCancellation;
        _aiCancellation = null;
        cancellation?.Cancel();
        IsAnalyzingWithAi = false;
        IsTestingAi = false;
        IsAiOperationRunning = false;
        SetCanAnalyze(_currentRun is not null);
        SetCanTestAi();
        if (status is not null)
        {
            AiStatusText = status;
        }
    }

    partial void OnIsLoadingHistoryChanged(bool value)
    {
        if (value)
        {
            CancelAiForContextChange("Análise cancelada porque o histórico está sendo carregado.");
            _aiPromptSnapshot = null;
            AiPromptPreview = string.Empty;
            ClearAiAnswer();
            AiStatusText = "Análise de IA indisponível enquanto o histórico está sendo carregado.";
        }
        else if (_aiPromptSnapshot is null)
        {
            AiStatusText = _currentRun is null
                ? AiIdleStatus
                : "Análise desabilitada: não há um snapshot válido para a execução carregada.";
        }

        SetCanAnalyze(_currentRun is not null);
        SetCanTestAi();
    }

    private bool CanRunAiAnalysis() => _aiProvider is not null
        && _currentRun is not null
        && _aiPromptSnapshot is not null
        && ReferenceEquals(_aiPromptSnapshot.Run, _currentRun)
        && !IsAnalyzingWithAi
        && !IsTestingAi
        && !IsScanning
        && !IsLoadingHistory;

    private bool IsCurrentAiOperation(int operationId, AiPromptSnapshot snapshot, CancellationTokenSource cancellation) =>
        operationId == _aiOperationId
        && ReferenceEquals(_aiCancellation, cancellation)
        && ReferenceEquals(snapshot, _aiPromptSnapshot)
        && ReferenceEquals(snapshot.Run, _currentRun)
        && !IsScanning
        && !IsLoadingHistory;

    [RelayCommand(CanExecute = nameof(CanRunAiAnalysis))]
    private async Task AnalyzeWithAiAsync()
    {
        var snapshot = _aiPromptSnapshot;
        var run = snapshot?.Run;
        var provider = _aiProvider;
        if (snapshot is null || run is null || provider is null
            || !ReferenceEquals(run, _currentRun) || IsScanning || IsLoadingHistory)
        {
            return;
        }

        _aiCancellation?.Cancel();
        _aiCancellation?.Dispose();
        var cancellation = _aiCancellation = new CancellationTokenSource();
        var operationId = ++_aiOperationId;

        IsAnalyzingWithAi = true;
        IsAiOperationRunning = true;
        SetCanAnalyze(false);
        SetCanTestAi();
        ClearAiAnswer();
        try
        {
            AiStatusText = "Verificando o Ollama local...";
            var availability = await provider.CheckAvailabilityAsync(cancellation.Token);
            if (!IsCurrentAiOperation(operationId, snapshot, cancellation))
            {
                return;
            }

            if (!availability.IsReady)
            {
                AiStatusText = availability.Message;
                return;
            }

            AiStatusText = $"Analisando com {availability.Model} neste computador. A primeira resposta pode levar alguns minutos.";
            // Revalidar o mesmo run e o mesmo snapshot imediatamente antes do POST.
            if (!IsCurrentAiOperation(operationId, snapshot, cancellation))
            {
                return;
            }

            var result = await provider.AnalyzeAsync(snapshot.Request, cancellation.Token);
            if (!IsCurrentAiOperation(operationId, snapshot, cancellation))
            {
                return;
            }

            if (result.IsSuccess)
            {
                AiAnswerText = result.Text!;
                AiStatusText = $"Gerado por IA local ({result.Model}). É uma explicação, não um diagnóstico definitivo; revise antes de agir.";
            }
            else
            {
                AiStatusText = result.Message;
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentAiOperation(operationId, snapshot, cancellation))
            {
                AiStatusText = "Análise cancelada.";
            }
        }
        finally
        {
            if (_aiOperationId == operationId && ReferenceEquals(_aiCancellation, cancellation))
            {
                _aiCancellation = null;
                IsAnalyzingWithAi = false;
                IsAiOperationRunning = false;
                SetCanAnalyze(_currentRun is not null);
                SetCanTestAi();
            }

            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private void CancelAiAnalysis() => _aiCancellation?.Cancel();

    private bool CanTestAiLocally() => _aiProvider is not null
        && !IsTestingAi
        && !IsAnalyzingWithAi
        && !IsAiOperationRunning
        && !IsScanning
        && !IsLoadingHistory;

    private bool IsCurrentAiTestOperation(int operationId, CancellationTokenSource cancellation) =>
        operationId == _aiOperationId
        && ReferenceEquals(_aiCancellation, cancellation)
        && !IsScanning
        && !IsLoadingHistory;

    /// <summary>Confirma conectividade e geração do modelo usando texto sintético, sem enviar dados do computador.</summary>
    [RelayCommand(CanExecute = nameof(CanTestAiLocally))]
    private async Task TestAiAsync()
    {
        var provider = _aiProvider;
        if (provider is null || IsScanning || IsLoadingHistory)
        {
            return;
        }

        _aiCancellation?.Cancel();
        _aiCancellation?.Dispose();
        var cancellation = _aiCancellation = new CancellationTokenSource();
        var operationId = ++_aiOperationId;
        IsTestingAi = true;
        IsAiOperationRunning = true;
        SetCanAnalyze(_currentRun is not null);
        SetCanTestAi();
        ClearAiAnswer();

        try
        {
            AiStatusText = "Verificando o Ollama local. Nenhum dado do computador será enviado.";
            var availability = await provider.CheckAvailabilityAsync(cancellation.Token);
            if (!IsCurrentAiTestOperation(operationId, cancellation)) return;
            if (!availability.IsReady)
            {
                AiStatusText = availability.Message;
                return;
            }

            AiStatusText = $"Ollama disponível ({availability.Model}). Testando uma resposta sintética, sem diagnóstico do computador...";
            var request = new AiAnalysisRequest(
                "Responda em português brasileiro, com uma única frase curta. Este é um teste técnico sintético. Não sugira ações no computador.",
                "Responda exatamente: IA local pronta.");
            var result = await provider.AnalyzeAsync(request, cancellation.Token);
            if (!IsCurrentAiTestOperation(operationId, cancellation)) return;

            if (result.IsSuccess)
            {
                AiAnswerText = result.Text!;
                AiStatusText = $"Teste concluído com {result.Model}. Só uma mensagem sintética foi enviada; nenhum dado do diagnóstico foi incluído.";
            }
            else
            {
                AiStatusText = result.Message;
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentAiTestOperation(operationId, cancellation)) AiStatusText = "Teste da IA cancelado.";
        }
        finally
        {
            if (_aiOperationId == operationId && ReferenceEquals(_aiCancellation, cancellation))
            {
                _aiCancellation = null;
                IsTestingAi = false;
                IsAiOperationRunning = false;
                SetCanAnalyze(_currentRun is not null);
                SetCanTestAi();
            }

            cancellation.Dispose();
        }
    }
}
