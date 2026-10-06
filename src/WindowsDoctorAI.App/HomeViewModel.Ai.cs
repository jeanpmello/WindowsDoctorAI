using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsDoctorAI.AI;

namespace WindowsDoctorAI.App;

/// <summary>Análise textual por IA local sobre a execução atual. Somente leitura: nada é executado a partir da resposta.</summary>
internal partial class HomeViewModel
{
    private const string AiIdleStatus = "Execute ou carregue um diagnóstico para habilitar a análise por IA local.";

    private CancellationTokenSource? _aiCancellation;

    [ObservableProperty] private string _aiStatusText = AiIdleStatus;
    [ObservableProperty] private string _aiAnswerText = string.Empty;
    [ObservableProperty] private string _aiPromptPreview = string.Empty;
    [ObservableProperty] private bool _isAnalyzingWithAi;
    [ObservableProperty] private bool _canAnalyzeWithAi;

    /// <summary>Chamado sempre que a execução atual muda; recalcula a prévia exata do que seria enviado ao modelo.</summary>
    internal void RefreshAiState()
    {
        ClearAiAnswer();
        if (_aiProvider is null)
        {
            AiPromptPreview = string.Empty;
            AiStatusText = "Nenhum provedor de IA está registrado nesta build.";
            SetCanAnalyze(false);
            return;
        }

        if (_currentRun is null)
        {
            AiPromptPreview = string.Empty;
            AiStatusText = AiIdleStatus;
            SetCanAnalyze(false);
            return;
        }

        var request = DiagnosticPromptBuilder.Build(_currentRun);
        AiPromptPreview = $"[Instruções ao modelo]\n{request.SystemPrompt}\n\n[Dados do diagnóstico (já com identificadores mascarados)]\n{request.UserPrompt}";
        AiStatusText = $"Pronto. {request.TotalCharacters:N0} caracteres serão enviados somente ao Ollama deste computador.";
        SetCanAnalyze(true);
    }

    private void ClearAiAnswer() => AiAnswerText = string.Empty;

    private void SetCanAnalyze(bool value)
    {
        CanAnalyzeWithAi = value && !IsAnalyzingWithAi;
        AnalyzeWithAiCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunAiAnalysis() => _aiProvider is not null && _currentRun is not null && !IsAnalyzingWithAi;

    [RelayCommand(CanExecute = nameof(CanRunAiAnalysis))]
    private async Task AnalyzeWithAiAsync()
    {
        var run = _currentRun;
        var provider = _aiProvider;
        if (run is null || provider is null)
        {
            return;
        }

        _aiCancellation?.Cancel();
        _aiCancellation?.Dispose();
        var cancellation = _aiCancellation = new CancellationTokenSource();

        IsAnalyzingWithAi = true;
        SetCanAnalyze(false);
        ClearAiAnswer();
        try
        {
            AiStatusText = "Verificando o Ollama local...";
            var availability = await provider.CheckAvailabilityAsync(cancellation.Token);
            if (!availability.IsReady)
            {
                AiStatusText = availability.Message;
                return;
            }

            AiStatusText = $"Analisando com {availability.Model} neste computador. A primeira resposta pode levar alguns minutos.";
            var result = await provider.AnalyzeAsync(DiagnosticPromptBuilder.Build(run), cancellation.Token);
            if (!ReferenceEquals(run, _currentRun))
            {
                AiStatusText = "O diagnóstico mudou durante a análise; o resultado foi descartado.";
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
            AiStatusText = "Análise cancelada.";
        }
        finally
        {
            IsAnalyzingWithAi = false;
            SetCanAnalyze(_currentRun is not null);
        }
    }

    [RelayCommand]
    private void CancelAiAnalysis() => _aiCancellation?.Cancel();
}
