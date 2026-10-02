using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsDoctorAI.Core;

namespace WindowsDoctorAI.App;

public sealed partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly INavigationService _navigationService;
    private IServiceScope? _pageScope;

    public MainWindow(IServiceProvider services, INavigationService navigationService)
    {
        InitializeComponent();
        _services = services;
        _navigationService = navigationService;
        _navigationService.Navigated += NavigationService_Navigated;
        Closed += MainWindow_Closed;
        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        _navigationService.NavigateTo("home");
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string route) _navigationService.NavigateTo(route);
    }

    private void NavigationService_Navigated(object? sender, string route)
    {
        PageFrame.Content = null;
        _pageScope?.Dispose();
        _pageScope = _services.CreateScope();
        PageFrame.Content = route switch
        {
            "home" => _pageScope.ServiceProvider.GetRequiredService<HomePage>(),
            "about" => _pageScope.ServiceProvider.GetRequiredService<AboutPage>(),
            "settings" => _pageScope.ServiceProvider.GetRequiredService<SettingsPage>(),
            _ => throw new InvalidOperationException($"Rota não registrada: {route}")
        };
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _navigationService.Navigated -= NavigationService_Navigated;
        PageFrame.Content = null;
        _pageScope?.Dispose();
        _pageScope = null;
    }
}
