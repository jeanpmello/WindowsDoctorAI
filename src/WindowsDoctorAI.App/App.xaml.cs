using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using WindowsDoctorAI.AI;
using WindowsDoctorAI.Application;
using WindowsDoctorAI.Core;
using WindowsDoctorAI.Database;
using WindowsDoctorAI.Diagnostics;
using WindowsDoctorAI.Infrastructure;
using WindowsDoctorAI.Reporting;
using WindowsDoctorAI.Repair;
using WinRT.Interop;

namespace WindowsDoctorAI.App;

/// <summary>Composition root: configuração, logging e registro explícito das implementações de cada camada.</summary>
public partial class App : global::Microsoft.UI.Xaml.Application
{
    private IHost? _host;
    private MainWindow? _mainWindow;

    internal MainWindow? MainWindow => _mainWindow;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (XamlSmokeTestRunner.IsRequested)
        {
            XamlSmokeTestRunner.Run();
            return;
        }

        StartupFailureContext.Reset();
        try
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsDoctorAI");
            var databasePath = Path.Combine(dataDirectory, "windowsdoctorai.db");

            StartupFailureContext.SetStage(StartupFailureStage.HostCreation);
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.SetBasePath(AppContext.BaseDirectory);
                    configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    configuration.AddEnvironmentVariables(prefix: "WINDOWSDOCTORAI_");
                })
                .ConfigureLogging((_, logging) => logging.AddDebug())
                .ConfigureServices((context, services) =>
                {
                    var ollamaOptions = context.Configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>() ?? new OllamaOptions();
                    services.AddSingleton(ollamaOptions);
                    var embeddedAiOptions = context.Configuration.GetSection(EmbeddedAiOptions.SectionName).Get<EmbeddedAiOptions>() ?? new EmbeddedAiOptions();
                    services.AddSingleton(embeddedAiOptions);
                    services.AddSingleton<EmbeddedOnnxGenAiProvider>();
                    services.AddSingleton<OllamaDiagnosticAiProvider>(provider => new OllamaDiagnosticAiProvider(
                        OllamaDiagnosticAiProvider.CreateHttpClient(),
                        provider.GetRequiredService<OllamaOptions>()));
                    services.AddSingleton<IDiagnosticAiProvider>(provider => embeddedAiOptions.Enabled
                        ? provider.GetRequiredService<EmbeddedOnnxGenAiProvider>()
                        : provider.GetRequiredService<OllamaDiagnosticAiProvider>());
                    services.AddSingleton<IDiagnosticAiConversationProvider>(provider => embeddedAiOptions.Enabled
                        ? provider.GetRequiredService<EmbeddedOnnxGenAiProvider>()
                        : provider.GetRequiredService<OllamaDiagnosticAiProvider>());
                    services.AddComputerInventoryDiagnostics();
                    services.AddWindowsDoctorInfrastructure(databasePath);
                    services.AddSingleton<IDiagnosticEngine, DiagnosticEngine>();
                    services.AddTransient<RunComputerInventoryDiagnosticUseCase>();
                    services.AddSingleton<ICbsLogMarkerClassifier, CbsLogMarkerClassifier>();
                    services.AddTransient<CbsLogImportService>();
                    services.AddTransient<ICbsLogFilePicker>(_ => new WinUiCbsLogFilePicker(() =>
                        _mainWindow is null
                            ? throw new InvalidOperationException("A janela principal não está disponível para abrir o seletor de arquivos.")
                            : WindowNative.GetWindowHandle(_mainWindow)));
                    services.AddTransient<DiagnosticHistoryMaintenanceService>();
                    services.AddTransient<DiagnosticPreferencesService>();
                    services.AddSingleton<RecommendationEngine>();
                    services.AddSingleton<RootCauseAnalyzer>();
                    services.AddSingleton<HtmlDiagnosticReportFormatter>();
                    services.AddTransient<DiagnosticAssessmentService>();
                    services.AddTransient<KnowledgeJsonImporter>();
                    services.AddSingleton<IRepairProposalAllowlist, CodeRepairProposalAllowlist>();
                    services.AddTransient<RepairProposalBuilder>();
                    services.AddTransient<IRepairPreconditionEvaluator, DiagnosticRepairPreconditionEvaluator>();
                    services.AddTransient<RepairEngine>();
                    services.AddSingleton<IRepairCatalog, EmptyRepairCatalog>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddTransient<MainWindow>();
                    services.AddTransient<HomeViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<HomePage>(provider => new HomePage(provider.GetRequiredService<HomeViewModel>()));
                    services.AddTransient<AboutPage>();
                    services.AddTransient<SettingsPage>();
                })
                .Build();

            StartupFailureContext.SetStage(StartupFailureStage.HostStart);
            await _host.StartAsync();
            StartupFailureContext.SetStage(StartupFailureStage.DatabaseInitialization);
            try
            {
                await _host.Services.InitializeWindowsDoctorDatabaseAsync();
            }
            catch (Exception)
            {
                _host.Services.GetRequiredService<ILogger<App>>().LogError("Não foi possível inicializar o banco SQLite local; detalhes omitidos por privacidade.");
            }

            StartupFailureContext.SetStage(StartupFailureStage.MainWindowCreation);
            _mainWindow = _host.Services.GetRequiredService<MainWindow>();
            _mainWindow.Closed += MainWindow_Closed;
            StartupFailureContext.SetStage(StartupFailureStage.MainWindowActivation);
            _mainWindow.Activate();
        }
        catch (Exception exception)
        {
            var details = StartupFailureDetails.ForOnLaunched(exception, StartupFailureContext.CurrentStage);
            _host?.Services.GetService<ILogger<App>>()?.LogCritical(
                "A inicialização do Windows Doctor AI falhou; estágio: {StartupStage}; tipo de exceção: {ExceptionType}; HRESULT: {HResultCode}; tipo da exceção interna: {InnerExceptionType}.",
                details.Stage,
                details.ExceptionType,
                details.HResultCode,
                details.InnerExceptionType);
            StartupFailureDialog.Show(details);
            Environment.Exit(1);
        }
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_host is null) return;
        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }
}
