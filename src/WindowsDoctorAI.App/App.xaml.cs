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

namespace WindowsDoctorAI.App;

/// <summary>Composition root: configuração, logging e registro explícito das implementações de cada camada.</summary>
public partial class App : Application
{
    private IHost? _host;
    private MainWindow? _mainWindow;

    internal MainWindow? MainWindow => _mainWindow;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsDoctorAI");
            var databasePath = Path.Combine(dataDirectory, "windowsdoctorai.db");

            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.SetBasePath(AppContext.BaseDirectory);
                    configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    configuration.AddEnvironmentVariables(prefix: "WINDOWSDOCTORAI_");
                })
                .ConfigureLogging((_, logging) => logging.AddDebug())
                .ConfigureServices((_, services) =>
                {
                    services.AddComputerInventoryDiagnostics();
                    services.AddWindowsDoctorInfrastructure(databasePath);
                    services.AddSingleton<IDiagnosticEngine, DiagnosticEngine>();
                    services.AddTransient<RunComputerInventoryDiagnosticUseCase>();
                    services.AddSingleton<RecommendationEngine>();
                    services.AddSingleton<RootCauseAnalyzer>();
                    services.AddSingleton<HtmlDiagnosticReportFormatter>();
                    services.AddTransient<DiagnosticAssessmentService>();
                    services.AddTransient<KnowledgeJsonImporter>();
                    services.AddTransient<RepairEngine>();
                    services.AddSingleton<IRepairCatalog, EmptyRepairCatalog>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddTransient<MainWindow>();
                    services.AddTransient<HomeViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<HomePage>();
                    services.AddTransient<AboutPage>();
                    services.AddTransient<SettingsPage>();
                })
                .Build();

            await _host.StartAsync();
            try
            {
                await _host.Services.InitializeWindowsDoctorDatabaseAsync();
            }
            catch (Exception exception)
            {
                _host.Services.GetRequiredService<ILogger<App>>().LogError(exception, "Não foi possível inicializar o banco SQLite local.");
            }

            _mainWindow = _host.Services.GetRequiredService<MainWindow>();
            _mainWindow.Closed += MainWindow_Closed;
            _mainWindow.Activate();
        }
        catch (Exception exception)
        {
            _host?.Services.GetService<ILogger<App>>()?.LogCritical(exception, "A inicialização do Windows Doctor AI falhou.");
            throw;
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
