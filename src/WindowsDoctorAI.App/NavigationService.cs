using WindowsDoctorAI.Core;

namespace WindowsDoctorAI.App;

/// <summary>Publica navegação por identificadores; a janela resolve a página correspondente via DI.</summary>
public sealed class NavigationService : INavigationService
{
    private static readonly HashSet<string> Routes = new(StringComparer.OrdinalIgnoreCase) { "home", "about", "settings" };
    private string? _currentRoute;
    public event EventHandler<string>? Navigated;

    public void NavigateTo(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (!Routes.Contains(destination)) throw new ArgumentOutOfRangeException(nameof(destination), destination, "Rota de navegação desconhecida.");
        if (string.Equals(_currentRoute, destination, StringComparison.OrdinalIgnoreCase)) return;

        // Set before notifying subscribers so a re-entrant request for this route is also a no-op.
        _currentRoute = destination;
        Navigated?.Invoke(this, destination);
    }
}
