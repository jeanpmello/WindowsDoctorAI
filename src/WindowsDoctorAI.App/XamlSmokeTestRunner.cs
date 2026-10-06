using System.Text;
using Microsoft.UI.Dispatching;
using WindowsDoctorAI.Core;

namespace WindowsDoctorAI.App;

/// <summary>Executa uma verificação isolada de inicialização XAML pelo ponto de entrada WinUI real.</summary>
internal static class XamlSmokeTestRunner
{
    private const string SmokeTestSwitch = "--xaml-smoke-test";
    private const string ResultPathSwitch = "--xaml-smoke-test-result";
    private static string? _resultPath;

    internal static bool IsRequested { get; private set; }

    internal static void Configure(string[] args)
    {
        var smokeTestIndex = Array.FindIndex(args, argument =>
            string.Equals(argument, SmokeTestSwitch, StringComparison.OrdinalIgnoreCase));
        if (smokeTestIndex < 0) return;

        IsRequested = true;
        _resultPath = Path.Combine(Path.GetTempPath(), "WindowsDoctorAI-xaml-smoke-test.txt");

        var resultPathIndex = Array.FindIndex(args, argument =>
            string.Equals(argument, ResultPathSwitch, StringComparison.OrdinalIgnoreCase));
        if (resultPathIndex < 0 || resultPathIndex == args.Length - 1)
            throw new ArgumentException($"O modo smoke requer o caminho após {ResultPathSwitch}.", nameof(args));

        _resultPath = Path.GetFullPath(args[resultPathIndex + 1]);
    }

    internal static void Run()
    {
        StartupFailureContext.Reset();
        try
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("A thread de inicialização WinUI não está em STA.");
            if (DispatcherQueue.GetForCurrentThread() is null)
                throw new InvalidOperationException("A thread de inicialização não possui DispatcherQueue WinUI.");

            var navigationService = new InertNavigationService();
            _ = new MainWindow(new NoServiceProvider(), navigationService);

            if (navigationService.NavigateRequests != 1)
                throw new InvalidOperationException("MainWindow não solicitou exatamente uma navegação inicial.");

            Complete(
                "PASS: MainWindow foi construída na thread UI/STA do WinUI; InitializeComponent() concluiu; a navegação foi inerte; a janela não foi ativada; nenhum serviço real foi resolvido.",
                0);
        }
        catch (Exception exception)
        {
            var details = StartupFailureDetails.ForOnLaunched(exception, StartupFailureContext.CurrentStage);
            var innerException = details.InnerExceptionType ?? "nenhuma";
            Complete(
                $"FAIL: estágio={details.Stage}; exceção={details.ExceptionType}; HRESULT={details.HResultCode}; exceção_interna={innerException}",
                1);
        }
    }

    internal static void FailAtAppEntryPoint(Exception exception)
    {
        if (!IsRequested) return;

        var details = StartupFailureDetails.ForAppEntryPoint(exception);
        Complete(
            $"FAIL: estágio=AppEntryPoint; exceção={details.ExceptionType}; HRESULT={details.HResultCode}; exceção_interna={details.InnerExceptionType ?? "nenhuma"}",
            1);
    }

    private static void Complete(string result, int exitCode)
    {
        try
        {
            if (_resultPath is null)
                throw new InvalidOperationException("O arquivo de resultado do smoke test não foi configurado.");

            File.WriteAllText(_resultPath, result + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Environment.Exit(exitCode);
        }
        catch (Exception)
        {
            Environment.Exit(2);
        }
    }

    private sealed class InertNavigationService : INavigationService
    {
        public event EventHandler<string>? Navigated
        {
            add { }
            remove { }
        }

        internal int NavigateRequests { get; private set; }

        public void NavigateTo(string destination) => NavigateRequests++;
    }

    private sealed class NoServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            throw new InvalidOperationException("O smoke test não permite resolver serviços da aplicação.");
    }
}
